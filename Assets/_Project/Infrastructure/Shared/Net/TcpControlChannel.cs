using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using ScreenPortal.App.Connection;
using ScreenPortal.Infrastructure.Protocol;

namespace ScreenPortal.Infrastructure.Net
{
    /// <summary>
    /// Cliente TCP do canal de controle. Uma thread por conexão lê frames e enfileira eventos; <see cref="Poll"/>
    /// os entrega na thread principal. Cada Connect/Close troca a "geração": eventos de conexões antigas são descartados.
    /// Não é caminho quente (poucas mensagens por segundo): alocação por mensagem é aceitável.
    /// </summary>
    public sealed class TcpControlChannel : IControlChannel, IDisposable
    {
        private readonly ConcurrentQueue<ChannelEvent> _events = new ConcurrentQueue<ChannelEvent>();
        private readonly object _lock = new object();
        private readonly int _connectTimeoutMs;
        private TcpClient _client;
        private NetworkStream _stream;
        private int _generation;

        private struct ChannelEvent
        {
            public int Generation;
            public ChannelEventKind Kind;
            public ControlMessage Message;
        }

        public TcpControlChannel(int connectTimeoutMs) => _connectTimeoutMs = connectTimeoutMs;

        public void Connect(HostEndpoint host)
        {
            int generation;
            lock (_lock)
            {
                CloseLocked();
                generation = _generation;
            }
            var thread = new Thread(() => Run(host, generation)) { IsBackground = true, Name = "sp-control" };
            thread.Start();
        }

        private void Run(HostEndpoint host, int generation)
        {
            var client = new TcpClient { NoDelay = true };
            try
            {
                var connect = client.ConnectAsync(host.Address, host.Port);
                if (!connect.Wait(_connectTimeoutMs) || !client.Connected) throw new TimeoutException();
            }
            catch (Exception)
            {
                client.Dispose();
                Post(generation, ChannelEventKind.ConnectFailed, null);
                return;
            }

            NetworkStream stream;
            lock (_lock)
            {
                if (generation != _generation)
                {
                    client.Dispose();
                    return;
                }
                _client = client;
                _stream = stream = client.GetStream();
            }
            Post(generation, ChannelEventKind.Connected, null);

            var reader = new FrameReader();
            var buffer = new byte[8192];
            try
            {
                while (true)
                {
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;
                    reader.Append(buffer, 0, read);
                    while (reader.TryRead(out var payload))
                    {
                        var decoded = ControlMessageCodec.Decode(payload);
                        if (decoded.Status == DecodeStatus.Ok)
                            Post(generation, ChannelEventKind.Message, decoded.Message);
                        else if (decoded.Status == DecodeStatus.IncompatibleVersion)
                            Post(generation, ChannelEventKind.Message, new ByeMessage(ByeReason.IncompatibleVersion));
                    }
                    if (reader.IsCorrupt) break;
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }

            Post(generation, ChannelEventKind.Disconnected, null);
        }

        private void Post(int generation, ChannelEventKind kind, ControlMessage message) =>
            _events.Enqueue(new ChannelEvent { Generation = generation, Kind = kind, Message = message });

        public ChannelEventKind Poll(out ControlMessage message)
        {
            while (_events.TryDequeue(out var e))
            {
                if (e.Generation != Volatile.Read(ref _generation)) continue;
                message = e.Message;
                return e.Kind;
            }
            message = null;
            return ChannelEventKind.None;
        }

        public void Send(ControlMessage message)
        {
            var frame = FrameCodec.Frame(ControlMessageCodec.Encode(message));
            lock (_lock)
            {
                if (_stream == null) return;
                try { _stream.Write(frame, 0, frame.Length); }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            }
        }

        public void Close()
        {
            lock (_lock) CloseLocked();
        }

        private void CloseLocked()
        {
            Interlocked.Increment(ref _generation);
            _stream = null;
            var client = _client;
            _client = null;
            client?.Dispose();
        }

        public void Dispose() => Close();
    }
}
