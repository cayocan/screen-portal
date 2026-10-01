using System.Net;
using System.Net.Sockets;
using Concentus;
using Concentus.Enums;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using ScreenPortal.Host.Domain.Audio;
using ScreenPortal.Host.Domain.Timing;

namespace ScreenPortal.Host.App.Audio;

/// <summary>
/// Som do PC → Quest (spec 2G §3): loopback WASAPI de uma saída → Opus 48 kHz estéreo, quadros de 10 ms, 128 kbps →
/// pacote SPAU → UDP. O loopback só entrega dados quando algo toca; em silêncio não sai pacote (o Quest toca silêncio).
/// Buffers reaproveitados: sem alocação por quadro.
/// </summary>
public sealed class PcAudioStreamer : IDisposable
{
    public const int SampleRate = 48000;
    public const int FrameSamples = 480; // 10 ms
    public const int Channels = 2;
    public const int BitrateBps = 128_000;

    private readonly WasapiLoopbackCapture _capture;
    private readonly IOpusEncoder _encoder;
    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private readonly IPEndPoint _target;
    private readonly Action<string> _log;
    private readonly float[] _frame = new float[FrameSamples * Channels];
    private readonly byte[] _opus = new byte[AudioPacketHeader.MaxPayload];
    private readonly byte[] _packet = new byte[AudioPacketHeader.Size + AudioPacketHeader.MaxPayload];
    private int _filled;
    private uint _seq;
    private long _packets;

    public string DeviceName { get; }

    public PcAudioStreamer(MMDevice device, IPEndPoint target, Action<string> log)
    {
        _target = target;
        _log = log;
        DeviceName = device.FriendlyName;
        _capture = new WasapiLoopbackCapture(device);
        var format = _capture.WaveFormat;
        if (format.SampleRate != SampleRate || format.Encoding != WaveFormatEncoding.IeeeFloat && format.BitsPerSample != 32)
            throw new NotSupportedException($"formato de {device.FriendlyName} não suportado ({format}); use 48 kHz no Windows");
        _encoder = OpusCodecFactory.CreateEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_RESTRICTED_LOWDELAY);
        _encoder.Bitrate = BitrateBps;
        _capture.DataAvailable += OnData;
    }

    public long TakePackets() => Interlocked.Exchange(ref _packets, 0);

    public void Start()
    {
        _capture.StartRecording();
        _log($"áudio: som do PC ({DeviceName}) → {_target} (Opus 48 kHz estéreo, 10 ms, {BitrateBps / 1000} kbps)");
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        int channels = _capture.WaveFormat.Channels;
        int frames = e.BytesRecorded / (4 * channels);
        var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, frames * 4 * channels));
        for (int f = 0; f < frames; f++)
        {
            float left = samples[f * channels];
            float right = channels > 1 ? samples[f * channels + 1] : left; // 5.1/7.1: os dois primeiros canais
            _frame[_filled * 2] = left;
            _frame[_filled * 2 + 1] = right;
            if (++_filled < FrameSamples) continue;
            _filled = 0;
            Send();
        }
    }

    private void Send()
    {
        int n = _encoder.Encode(_frame, FrameSamples, _opus, _opus.Length);
        if (n <= 0) return;
        int size = new AudioPacketHeader(Channels, _seq++, HostClock.NowUs()).Write(_packet, _opus.AsSpan(0, n));
        try
        {
            _socket.SendTo(_packet, 0, size, SocketFlags.None, _target);
            Interlocked.Increment(ref _packets);
        }
        catch (SocketException) { /* Wi-Fi caiu: o próximo quadro tenta de novo */ }
    }

    public void Dispose()
    {
        _capture.DataAvailable -= OnData;
        try { _capture.StopRecording(); } catch (Exception) { }
        _capture.Dispose();
        _socket.Dispose();
    }
}
