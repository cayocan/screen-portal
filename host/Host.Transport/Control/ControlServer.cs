using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using ScreenPortal.App.Connection;
using ScreenPortal.Infrastructure.Protocol;

namespace ScreenPortal.Host.Transport.Control;

/// <summary>Servidor TCP do canal de controle (porta 9200): uma thread e uma HeadsetSession por headset.</summary>
public sealed class ControlServer(int port, Func<IControlConnection, HeadsetSession> sessionFactory, Action<string> log) : IDisposable
{
    private readonly ConcurrentDictionary<TcpConnection, byte> _connections = new();
    private TcpListener? _listener;
    private volatile bool _stopping;

    public int Port { get; private set; } = port;
    public int ConnectionCount => _connections.Count;

    public void Start()
    {
        _listener = new TcpListener(IPAddress.Any, Port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        new Thread(AcceptLoop) { IsBackground = true, Name = "control-accept" }.Start();
        log($"controle: ouvindo tcp/{Port}");
    }

    private void AcceptLoop()
    {
        while (!_stopping)
        {
            TcpClient client;
            try { client = _listener!.AcceptTcpClient(); }
            catch (SocketException) { break; }
            catch (ObjectDisposedException) { break; }
            new Thread(() => Serve(client)) { IsBackground = true, Name = "control-session" }.Start();
        }
    }

    private void Serve(TcpClient client)
    {
        client.NoDelay = true;
        var connection = new TcpConnection(client);
        _connections[connection] = 0;
        var session = sessionFactory(connection);
        log($"controle: headset conectado ({connection.RemoteAddress})");
        var reader = new FrameReader();
        var buffer = new byte[8192];
        try
        {
            var stream = client.GetStream();
            while (!_stopping)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;
                reader.Append(buffer, 0, read);
                while (reader.TryRead(out var payload))
                {
                    var decoded = ControlMessageCodec.Decode(payload);
                    if (decoded.Status == DecodeStatus.Ok) session.OnMessage(decoded.Message);
                    else if (decoded.Status == DecodeStatus.IncompatibleVersion)
                    {
                        log($"controle: versão incompatível do headset ({decoded.Version})");
                        connection.Send(new ByeMessage(ByeReason.IncompatibleVersion));
                        connection.Close();
                    }
                }
                if (reader.IsCorrupt) break;
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
        finally
        {
            _connections.TryRemove(connection, out _);
            connection.Close();
            session.OnClosed();
            log($"controle: headset desconectado ({connection.RemoteAddress})");
        }
    }

    /// <summary>Derruba todas as conexões (testes de reconexão).</summary>
    public void DropAllConnections()
    {
        foreach (var c in _connections.Keys) c.Close();
    }

    public void BroadcastBye(ByeReason reason)
    {
        foreach (var c in _connections.Keys) c.Send(new ByeMessage(reason));
    }

    public void Dispose()
    {
        _stopping = true;
        BroadcastBye(ByeReason.Shutdown);
        DropAllConnections();
        _listener?.Stop();
    }

    private sealed class TcpConnection(TcpClient client) : IControlConnection
    {
        private readonly object _lock = new();
        public string RemoteAddress { get; } = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.MapToIPv4().ToString();

        public void Send(ControlMessage message)
        {
            var frame = FrameCodec.Frame(ControlMessageCodec.Encode(message));
            lock (_lock)
            {
                try { client.GetStream().Write(frame, 0, frame.Length); }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }
        }

        public void Close()
        {
            lock (_lock) client.Dispose();
        }
    }
}
