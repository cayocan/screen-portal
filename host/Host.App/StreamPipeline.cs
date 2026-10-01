using System.Diagnostics;
using ScreenPortal.Host.Capture;
using ScreenPortal.Host.Domain.Timing;
using ScreenPortal.Host.Domain.Video;
using ScreenPortal.Host.Encode;
using ScreenPortal.Host.Transport;

namespace ScreenPortal.Host.App;

/// <summary>
/// Capture ► Convert(NV12) ► Encode ► Packetize ► Send (brief §8.7).
/// Fase 0: um encode, replicado em N streams (portas base..base+N-1) para medir quantos
/// decoders simultâneos o Quest 3 aguenta. Isso mede o headset, não o encoder do PC.
/// </summary>
public sealed class StreamPipeline : ICaptureFrameSink, IEncodedFrameSink, IDisposable
{
    private readonly ICaptureSource _capture;
    private readonly Nv12Converter _converter;
    private readonly MfH264Encoder _encoder;
    private readonly VideoPacketizer[] _packetizers;
    private readonly UdpVideoSender[] _senders;
    private readonly Stream? _dump;
    private long _minFrameIntervalUs; // 2F: muda ao vivo (SetRate)
    private ulong _lastAcceptedUs;

    // Estatísticas (lidas pela thread de log; imprecisão de corrida é aceitável).
    private long _captured, _skipped, _encoded, _bytes, _latencySumUs, _latencyMaxUs, _keyframes;

    public string Description { get; }

    public StreamPipeline(GpuDevice gpu, ICaptureSource capture, EncoderSettings settings,
        System.Net.IPEndPoint target, int streamCount, string? dumpPath, ushort firstStreamId = 0, uint firstFrameId = 0)
    {
        _capture = capture;
        _converter = new Nv12Converter(gpu.Device, gpu.Context, capture.Width, capture.Height, settings.Width, settings.Height);
        _encoder = new MfH264Encoder(gpu.Device, settings, this);
        _minFrameIntervalUs = MinFrameIntervalUs(settings.Fps);

        _packetizers = new VideoPacketizer[streamCount];
        _senders = new UdpVideoSender[streamCount];
        for (int i = 0; i < streamCount; i++)
        {
            _packetizers[i] = new VideoPacketizer((ushort)(firstStreamId + i), firstFrameId);
            _senders[i] = new UdpVideoSender(new System.Net.IPEndPoint(target.Address, target.Port + i));
        }
        if (dumpPath is not null) _dump = File.Create(dumpPath);

        Description = $"{capture.Name} {capture.Width}x{capture.Height} → {_encoder.Name} " +
                      $"{settings.Width}x{settings.Height}@{settings.Fps} {settings.BitrateKbps} kbps → " +
                      $"udp://{target.Address}:{target.Port}..{target.Port + streamCount - 1}";
    }

    public void Start() => _capture.Start(this);

    /// <summary>Próximo frameId do (primeiro) stream: um pipeline que substitui este segue daqui. Ler depois do Dispose.</summary>
    public uint NextFrameId => _packetizers[0].NextFrameId;

    /// <summary>
    /// 2F: fps ao vivo (limitador da captura). O bitrate cai na mesma proporção sozinho: o encoder reserva os bits por
    /// quadro da configuração a 60 fps (ver StreamRateLimits.BaseKbpsAt60).
    /// </summary>
    public void SetFps(int fps) => Interlocked.Exchange(ref _minFrameIntervalUs, MinFrameIntervalUs(fps));

    private static long MinFrameIntervalUs(int fps) => 1_000_000L / fps - 2_000; // tolerância para jitter do compositor

    /// <summary>Reconexão: o decoder do headset precisa de um IDR para voltar a mostrar imagem.</summary>
    public void RequestKeyframe() => _encoder.RequestKeyframe();

    public void OnFrame(Vortice.Direct3D11.ID3D11Texture2D bgraTexture, int width, int height, ulong captureTimeUs)
    {
        if (_lastAcceptedUs != 0 && (long)(captureTimeUs - _lastAcceptedUs) < Interlocked.Read(ref _minFrameIntervalUs))
        {
            _skipped++;
            return;
        }
        _lastAcceptedUs = captureTimeUs;
        _captured++;
        int slot = _converter.Convert(bgraTexture);
        _encoder.Submit(_converter.GetOutput(slot), captureTimeUs);
    }

    public void OnEncodedFrame(ReadOnlySpan<byte> accessUnit, bool keyframe, ulong captureTimeUs)
    {
        for (int i = 0; i < _packetizers.Length; i++)
            _packetizers[i].Packetize(accessUnit, keyframe, captureTimeUs, _senders[i]);

        _dump?.Write(accessUnit);

        long latency = (long)(HostClock.NowUs() - captureTimeUs);
        _encoded++;
        _bytes += accessUnit.Length;
        _latencySumUs += latency;
        if (latency > _latencyMaxUs) _latencyMaxUs = latency;
        if (keyframe) _keyframes++;
    }

    /// <summary>Linha de estatística do último intervalo e zera os contadores.</summary>
    public string TakeStats(double seconds)
    {
        long encoded = Interlocked.Exchange(ref _encoded, 0);
        long captured = Interlocked.Exchange(ref _captured, 0);
        long skipped = Interlocked.Exchange(ref _skipped, 0);
        long bytes = Interlocked.Exchange(ref _bytes, 0);
        long latSum = Interlocked.Exchange(ref _latencySumUs, 0);
        long latMax = Interlocked.Exchange(ref _latencyMaxUs, 0);
        long keys = Interlocked.Exchange(ref _keyframes, 0);
        long replaced = Interlocked.Exchange(ref _encoder.FramesReplaced, 0);
        long sendErrors = 0;
        foreach (var s in _senders) sendErrors += Interlocked.Exchange(ref s.SendErrors, 0);

        double avgMs = encoded > 0 ? latSum / 1000.0 / encoded : 0;
        return $"cap {captured / seconds,5:F1} fps (skip {skipped}) | enc {encoded / seconds,5:F1} fps " +
               $"(repl {replaced}, idr {keys}) | {bytes * 8 / seconds / 1_000_000,6:F2} Mbps/stream | " +
               $"cap→enc avg {avgMs,5:F1} ms max {latMax / 1000.0,5:F1} ms" +
               (sendErrors > 0 ? $" | send errors {sendErrors}" : "");
    }

    public void Dispose()
    {
        _capture.Dispose();
        _encoder.Dispose();
        _converter.Dispose();
        foreach (var s in _senders) s.Dispose();
        _dump?.Dispose();
    }
}
