using ScreenPortal.Host.Domain.Video;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;

namespace ScreenPortal.Host.Encode;

/// <summary>
/// H.264 por hardware via MFT assíncrono do fabricante (AMF / NVENC / QuickSync), entrada NV12 na GPU.
/// Low-latency: MF_LOW_LATENCY, sem B-frames, CBR, IDR periódico.
/// Duas threads: eventos do MFT (NeedInput/HaveOutput) e envio de entrada. A entrada é um
/// "último frame pendente": frame novo substitui o que ainda não entrou no encoder.
/// </summary>
public sealed unsafe class MfH264Encoder : IVideoEncoder
{
    private const int MF_E_TRANSFORM_STREAM_CHANGE = unchecked((int)0xC00D6D61);
    private const int MF_E_TRANSFORM_NEED_MORE_INPUT = unchecked((int)0xC00D6D72);
    private const uint ProfileMain = 77;

    private readonly EncoderSettings _settings;
    private readonly IEncodedFrameSink _sink;
    private readonly IMFTransform _transform;
    private readonly IMFMediaEventGenerator _events;
    private readonly IMFDXGIDeviceManager _deviceManager;
    private readonly CodecApi? _codec;
    private readonly bool _providesSamples;
    private readonly int _outputBufferSize;

    private readonly SemaphoreSlim _inputCredits = new(0);
    private readonly SemaphoreSlim _frameReady = new(0, 1);
    private readonly object _pendingLock = new();
    private ID3D11Texture2D? _pendingTexture;
    private ulong _pendingTimeUs;

    // Um IMFSample por textura do anel do conversor, reaproveitado (sem alocação por frame).
    private readonly (IntPtr texture, IMFSample sample)[] _samples = new (IntPtr, IMFSample)[Nv12Converter.RingSize * 2];
    private int _sampleCount;

    private readonly byte[] _parameterSets = new byte[512];
    private int _parameterSetsLength;
    private byte[] _patchBuffer = new byte[1024 * 1024];

    private readonly Thread _eventThread;
    private readonly Thread _inputThread;
    private volatile bool _stop;
    private volatile bool _keyframeRequested = true;
    private long _lastKeyframeUs;

    public string Name { get; }

    public long FramesSubmitted;
    public long FramesEncoded;
    public long FramesReplaced;

    public MfH264Encoder(ID3D11Device device, EncoderSettings settings, IEncodedFrameSink sink)
    {
        _settings = settings;
        _sink = sink;

        MediaFactory.MFStartup(false).CheckError();

        using (var activates = MediaFactory.MFTEnumEx(
            TransformCategoryGuids.VideoEncoder,
            (uint)(EnumFlag.EnumFlagHardware | EnumFlag.EnumFlagSortandfilter),
            new RegisterTypeInfo { GuidMajorType = MediaTypeGuids.Video, GuidSubtype = VideoFormatGuids.NV12 },
            new RegisterTypeInfo { GuidMajorType = MediaTypeGuids.Video, GuidSubtype = VideoFormatGuids.H264 }))
        {
            IMFActivate? chosen = null;
            foreach (var activate in activates) { chosen ??= activate; }
            if (chosen is null)
                throw new NotSupportedException("Nenhum encoder H.264 de hardware (MFT) encontrado.");
            Name = string.IsNullOrWhiteSpace(chosen.FriendlyName) ? "H.264 hardware MFT" : chosen.FriendlyName;
            _transform = chosen.ActivateObject<IMFTransform>();
        }

        var attributes = _transform.Attributes;
        attributes.Set(TransformAttributeKeys.TransformAsyncUnlock, 1u);
        attributes.Set(CodecApi.AVLowLatencyMode, 1u); // MF_LOW_LATENCY tem o mesmo GUID

        _deviceManager = MediaFactory.MFCreateDXGIDeviceManager();
        _deviceManager.ResetDevice(device).CheckError();
        _transform.ProcessMessage(TMessageType.MessageSetD3DManager, (nuint)_deviceManager.NativePointer);

        _codec = CodecApi.TryGet(_transform.NativePointer);
        if (_codec is not null)
        {
            _codec.SetBool(CodecApi.AVLowLatencyMode, true);
            _codec.SetUInt32(CodecApi.AVEncCommonRateControlMode, CodecApi.RateControlCbr);
            _codec.SetUInt32(CodecApi.AVEncCommonMeanBitRate, (uint)settings.BitrateKbps * 1000);
            _codec.SetUInt32(CodecApi.AVEncMPVDefaultBPictureCount, 0);
            _codec.SetUInt32(CodecApi.AVEncMPVGOPSize, (uint)Math.Max(1, settings.Fps * settings.KeyframeIntervalMs / 1000));
        }

        using (var output = MediaFactory.MFCreateMediaType())
        {
            output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            output.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
            output.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)settings.BitrateKbps * 1000);
            SetVideoFormat(output, settings);
            output.Set(MediaTypeAttributeKeys.Mpeg2Profile, ProfileMain);
            _transform.SetOutputType(0, output, 0);
        }

        using (var input = MediaFactory.MFCreateMediaType())
        {
            input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            input.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
            SetVideoFormat(input, settings);
            _transform.SetInputType(0, input, 0);
        }

        var info = _transform.GetOutputStreamInfo(0);
        _providesSamples = (info.Flags & (int)(OutputStreamInfoFlags.OutputStreamProvidesSamples | OutputStreamInfoFlags.OutputStreamCanProvideSamples)) != 0;
        _outputBufferSize = Math.Max(info.Size, settings.Width * settings.Height);

        _events = _transform.QueryInterface<IMFMediaEventGenerator>();
        _transform.ProcessMessage(TMessageType.MessageNotifyBeginStreaming, 0);
        _transform.ProcessMessage(TMessageType.MessageNotifyStartOfStream, 0);

        _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "mft-events", Priority = ThreadPriority.AboveNormal };
        _inputThread = new Thread(InputLoop) { IsBackground = true, Name = "mft-input", Priority = ThreadPriority.AboveNormal };
        _eventThread.Start();
        _inputThread.Start();
    }

    private static void SetVideoFormat(IMFMediaType type, EncoderSettings s)
    {
        type.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)s.Width << 32) | (uint)s.Height);
        type.Set(MediaTypeAttributeKeys.FrameRate, ((ulong)s.Fps << 32) | 1u);
        type.Set(MediaTypeAttributeKeys.PixelAspectRatio, (1UL << 32) | 1u);
        type.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
    }

    public void Submit(ID3D11Texture2D nv12, ulong captureTimeUs)
    {
        lock (_pendingLock)
        {
            if (_pendingTexture is not null) FramesReplaced++;
            _pendingTexture = nv12;
            _pendingTimeUs = captureTimeUs;
        }
        FramesSubmitted++;
        if (_frameReady.CurrentCount == 0)
        {
            try { _frameReady.Release(); } catch (SemaphoreFullException) { }
        }
    }

    public void RequestKeyframe() => _keyframeRequested = true;

    private void InputLoop()
    {
        long durationHns = 10_000_000L / _settings.Fps;
        while (!_stop)
        {
            _inputCredits.Wait();
            _frameReady.Wait();
            if (_stop) break;

            ID3D11Texture2D? texture;
            ulong timeUs;
            lock (_pendingLock)
            {
                texture = _pendingTexture;
                timeUs = _pendingTimeUs;
                _pendingTexture = null;
            }
            if (texture is null) { _inputCredits.Release(); continue; }

            var sample = SampleFor(texture);
            sample.SampleTime = (long)timeUs * 10;
            sample.SampleDuration = durationHns;

            if (_keyframeRequested || (long)timeUs - _lastKeyframeUs >= _settings.KeyframeIntervalMs * 1000L)
            {
                _codec?.SetUInt32(CodecApi.AVEncVideoForceKeyFrame, 1);
                _keyframeRequested = false;
                _lastKeyframeUs = (long)timeUs;
            }

            _transform.ProcessInput(0, sample, 0);
        }
    }

    private IMFSample SampleFor(ID3D11Texture2D texture)
    {
        for (int i = 0; i < _sampleCount; i++)
            if (_samples[i].texture == texture.NativePointer) return _samples[i].sample;

        var buffer = MediaFactory.MFCreateDXGISurfaceBuffer(typeof(ID3D11Texture2D).GUID, texture, 0, false);
        var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        buffer.Dispose();
        _samples[_sampleCount++] = (texture.NativePointer, sample);
        return sample;
    }

    private void EventLoop()
    {
        while (!_stop)
        {
            IMFMediaEvent ev;
            try { ev = _events.GetEvent(0); }
            catch (SharpGenException) when (_stop) { break; }

            using (ev)
            {
                switch (ev.EventType)
                {
                    case MediaEventTypes.TransformNeedInput:
                        _inputCredits.Release();
                        break;
                    case MediaEventTypes.TransformHaveOutput:
                        DrainOutput();
                        break;
                    case MediaEventTypes.TransformDrainComplete:
                        return;
                }
            }
        }
    }

    private void DrainOutput()
    {
        var buffer = new OutputDataBuffer { StreamID = 0 };
        IMFSample? own = null;
        if (!_providesSamples)
        {
            own = MediaFactory.MFCreateSample();
            using var mem = MediaFactory.MFCreateMemoryBuffer(_outputBufferSize);
            own.AddBuffer(mem);
            buffer.Sample = own;
        }

        Result hr = _transform.ProcessOutput(ProcessOutputFlags.None, 1, ref buffer, out _);
        buffer.Events?.Dispose();

        if (hr.Code == MF_E_TRANSFORM_STREAM_CHANGE)
        {
            using var type = _transform.GetOutputAvailableType(0, 0);
            _transform.SetOutputType(0, type, 0);
            return;
        }
        if (hr.Code == MF_E_TRANSFORM_NEED_MORE_INPUT) return;
        hr.CheckError();

        using var sample = buffer.Sample!;
        if (own is not null && !ReferenceEquals(own, sample)) own.Dispose();

        ulong captureUs = (ulong)(sample.SampleTime / 10);
        using var contiguous = sample.ConvertToContiguousBuffer();
        contiguous.Lock(out IntPtr data, out _, out int length);
        try
        {
            Publish(new ReadOnlySpan<byte>((void*)data, length), captureUs);
        }
        finally { contiguous.Unlock(); }
    }

    private void Publish(ReadOnlySpan<byte> au, ulong captureUs)
    {
        var nal = H264Nal.Scan(au);
        if (nal.HasSps && nal.HasPps)
        {
            _parameterSetsLength = H264Nal.ExtractParameterSets(au, _parameterSets);
        }
        else if (nal.HasIdr && _parameterSetsLength > 0)
        {
            // Alguns MFTs só emitem SPS/PPS no primeiro IDR; o receptor precisa deles em todo IDR
            // para conseguir entrar no meio do stream.
            int total = _parameterSetsLength + au.Length;
            if (_patchBuffer.Length < total) _patchBuffer = new byte[total * 2];
            _parameterSets.AsSpan(0, _parameterSetsLength).CopyTo(_patchBuffer);
            au.CopyTo(_patchBuffer.AsSpan(_parameterSetsLength));
            au = _patchBuffer.AsSpan(0, total);
        }

        FramesEncoded++;
        _sink.OnEncodedFrame(au, nal.HasIdr, captureUs);
    }

    public void Dispose()
    {
        _stop = true;
        try { _transform.ProcessMessage(TMessageType.MessageNotifyEndOfStream, 0); } catch { }
        try { _transform.ProcessMessage(TMessageType.MessageCommandDrain, 0); } catch { }
        _inputCredits.Release();
        try { _frameReady.Release(); } catch (SemaphoreFullException) { }
        _inputThread.Join(500);
        _eventThread.Join(500);

        for (int i = 0; i < _sampleCount; i++) _samples[i].sample.Dispose();
        _codec?.Dispose();
        _events.Dispose();
        _transform.Dispose();
        _deviceManager.Dispose();
    }
}
