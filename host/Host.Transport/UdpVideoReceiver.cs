using System.Net;
using System.Net.Sockets;
using ScreenPortal.Host.Domain.Timing;
using ScreenPortal.Host.Domain.Video;

namespace ScreenPortal.Host.Transport;

/// <summary>
/// Receptor de depuração no PC (modo <c>receive</c> do host): remonta frames e grava o .h264
/// para validar o stream com ffprobe/ffplay sem precisar do headset.
/// </summary>
public sealed class UdpVideoReceiver : IDisposable
{
    private readonly Socket _socket;
    private readonly FrameReassembler _reassembler = new();
    private readonly Stream? _output;

    public long Frames;
    public long Keyframes;
    public long Bytes;
    public long LatencySumUs;
    public long LatencyMaxUs;
    public FrameReassembler Reassembler => _reassembler;

    public UdpVideoReceiver(int port, Stream? output)
    {
        _output = output;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { ReceiveBufferSize = 8 * 1024 * 1024 };
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
    }

    public void Run(CancellationToken ct)
    {
        var buffer = new byte[VideoPacketHeader.MaxPacket + 64];
        _socket.ReceiveTimeout = 200;
        while (!ct.IsCancellationRequested)
        {
            int n;
            try { n = _socket.Receive(buffer); }
            catch (SocketException) { continue; }
            _reassembler.Push(buffer.AsSpan(0, n), OnFrame);
        }
    }

    private void OnFrame(ReadOnlySpan<byte> au, in VideoPacketHeader h)
    {
        // Mesmo PC → mesmo relógio QPC: latência captura→remontado sem sincronização.
        long latency = (long)(HostClock.NowUs() - h.CaptureTimeUs);
        Frames++;
        if (h.IsKeyframe) Keyframes++;
        Bytes += au.Length;
        LatencySumUs += latency;
        if (latency > LatencyMaxUs) LatencyMaxUs = latency;
        _output?.Write(au);
    }

    public void Dispose()
    {
        _socket.Dispose();
        _output?.Dispose();
    }
}
