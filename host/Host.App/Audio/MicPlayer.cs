using System.Net;
using System.Net.Sockets;
using Concentus;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using ScreenPortal.Host.Domain.Audio;

namespace ScreenPortal.Host.App.Audio;

/// <summary>
/// Microfone do Quest → PC (spec 2G §3): UDP → pacote SPAU → Opus 48 kHz mono → buffer (máx. 100 ms, o excesso é
/// descartado para não acumular atraso) → WASAPI no "Alto-falantes (Steam Streaming Microphone)", que os apps veem como
/// o microfone "Steam Streaming Microphone".
/// </summary>
public sealed class MicPlayer : IDisposable
{
    public const int SampleRate = 48000;
    public const int MaxFrameSamples = 5760; // 120 ms, o maior quadro Opus
    private static readonly TimeSpan MaxBuffered = TimeSpan.FromMilliseconds(100);

    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private readonly IOpusDecoder _decoder = OpusCodecFactory.CreateDecoder(SampleRate, 1);
    private readonly BufferedWaveProvider _buffer = new(new WaveFormat(SampleRate, 16, 1))
    {
        BufferDuration = TimeSpan.FromMilliseconds(400),
        DiscardOnBufferOverflow = true,
    };
    private readonly WasapiOut _output;
    private readonly Thread _thread;
    private readonly Action<string> _log;
    private volatile bool _stop;
    private uint _lastSeq;
    private bool _hasSeq;
    private long _packets;

    public int Port { get; }

    public MicPlayer(MMDevice render, int port, Action<string> log)
    {
        _log = log;
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        _socket.ReceiveTimeout = 500;
        Port = port;
        _output = new WasapiOut(render, AudioClientShareMode.Shared, true, 20);
        _output.Init(_buffer);
        _output.Play();
        _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "MicPlayer" };
        _thread.Start();
        _log($"áudio: microfone do Quest → {render.FriendlyName} (udp/{port})");
    }

    public long TakePackets() => Interlocked.Exchange(ref _packets, 0);

    private void ReceiveLoop()
    {
        var packet = new byte[2048];
        var pcm = new short[MaxFrameSamples];
        var bytes = new byte[MaxFrameSamples * 2];
        EndPoint from = new IPEndPoint(IPAddress.Any, 0);
        while (!_stop)
        {
            int n;
            try { n = _socket.ReceiveFrom(packet, ref from); }
            catch (SocketException) { continue; } // timeout: confere _stop
            catch (ObjectDisposedException) { return; }
            if (!AudioPacketHeader.TryRead(packet.AsSpan(0, n), out var header, out var payload)) continue;
            if (_hasSeq && (int)(header.Seq - _lastSeq) <= 0) continue; // atrasado ou repetido
            _hasSeq = true;
            _lastSeq = header.Seq;
            int samples = _decoder.Decode(payload, pcm, MaxFrameSamples, false);
            if (samples <= 0) continue;
            Buffer.BlockCopy(pcm, 0, bytes, 0, samples * 2);
            if (_buffer.BufferedDuration > MaxBuffered) _buffer.ClearBuffer(); // não acumula atraso
            _buffer.AddSamples(bytes, 0, samples * 2);
            Interlocked.Increment(ref _packets);
        }
    }

    public void Dispose()
    {
        _stop = true;
        _socket.Dispose();
        _thread.Join(1000);
        _output.Stop();
        _output.Dispose();
        _log("áudio: microfone do Quest parado");
    }
}
