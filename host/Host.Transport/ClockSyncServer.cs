using System.Net;
using System.Net.Sockets;
using ScreenPortal.Host.Domain.Timing;

namespace ScreenPortal.Host.Transport;

/// <summary>
/// Responde pings de sincronização de relógio (protocol/video-packet-v1.md). O headset usa o
/// offset para calcular a latência captura→decodificado a partir do captureTimeUs dos pacotes.
/// </summary>
public sealed class ClockSyncServer : IDisposable
{
    public const int DefaultPort = 9100;

    private readonly Socket _socket;
    private readonly Thread _thread;
    private volatile bool _stop;

    public long Requests;

    public ClockSyncServer(int port = DefaultPort)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        _thread = new Thread(Loop) { IsBackground = true, Name = "clock-sync", Priority = ThreadPriority.Highest };
        _thread.Start();
    }

    private void Loop()
    {
        var buffer = new byte[64];
        var remote = new SocketAddress(AddressFamily.InterNetwork);
        while (!_stop)
        {
            int n;
            try { n = _socket.ReceiveFrom(buffer, SocketFlags.None, remote); }
            catch (SocketException) { if (_stop) break; continue; }
            catch (ObjectDisposedException) { break; }

            if (!ClockSyncMessage.TryRead(buffer.AsSpan(0, n), out var req)) continue;
            new ClockSyncMessage(req.Seq, req.ClientSendUs, HostClock.NowUs()).Write(buffer);
            try { _socket.SendTo(buffer.AsSpan(0, ClockSyncMessage.Size), SocketFlags.None, remote); } catch (SocketException) { }
            Requests++;
        }
    }

    public void Dispose()
    {
        _stop = true;
        _socket.Dispose();
    }
}
