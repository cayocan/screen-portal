using System.Net;
using System.Net.Sockets;
using ScreenPortal.Host.Domain.Video;

namespace ScreenPortal.Host.Transport;

/// <summary>Recebe pedidos de keyframe (KFRQ) dos headsets em UDP 9101.</summary>
public sealed class KeyframeRequestListener : IDisposable
{
    public const int DefaultPort = 9101;

    private readonly Socket _socket;
    private readonly Action<IPAddress, ushort> _onRequest;
    private volatile bool _stop;

    public int Port { get; }

    public KeyframeRequestListener(int port, Action<IPAddress, ushort> onRequest)
    {
        _onRequest = onRequest;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        Port = ((IPEndPoint)_socket.LocalEndPoint!).Port;
        new Thread(Loop) { IsBackground = true, Name = "kfrq" }.Start();
    }

    private void Loop()
    {
        var buffer = new byte[64];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        while (!_stop)
        {
            int n;
            try { n = _socket.ReceiveFrom(buffer, ref remote); }
            catch (SocketException) { if (_stop) break; continue; }
            catch (ObjectDisposedException) { break; }
            if (KeyframeRequest.TryRead(buffer.AsSpan(0, n), out var request))
                _onRequest(((IPEndPoint)remote).Address.MapToIPv4(), request.StreamId);
        }
    }

    public void Dispose()
    {
        _stop = true;
        _socket.Dispose();
    }
}
