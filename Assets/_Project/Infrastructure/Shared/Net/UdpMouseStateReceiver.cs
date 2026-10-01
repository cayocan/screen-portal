using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Cursor;
using ScreenPortal.Infrastructure.Protocol;

namespace ScreenPortal.Infrastructure.Net
{
    /// <summary>
    /// <see cref="MouseState"/> por UDP (spec 3B §3): uma thread com buffer fixo; guarda só o pacote mais novo (seq).
    /// Seq muito menor que o último = host reiniciou: aceita. Leitura do quadro sem alocação.
    /// </summary>
    public sealed class UdpMouseStateReceiver : IMouseStateSource
    {
        private const uint RestartGap = 100_000;

        private readonly IClock _clock;
        private readonly object _lock = new object();
        private Socket _socket;
        private Thread _thread;
        private MouseState _latest;
        private double _latestAt;
        private bool _has;

        public UdpMouseStateReceiver(IClock clock) => _clock = clock;

        public bool Start(int port)
        {
            if (_socket != null) return true;
            try
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
                _socket = socket;
            }
            catch (SocketException)
            {
                return false;
            }
            _thread = new Thread(Run) { IsBackground = true, Name = "MouseState" };
            _thread.Start();
            return true;
        }

        private void Run()
        {
            var buffer = new byte[256];
            var socket = _socket;
            while (true)
            {
                int n;
                try { n = socket.Receive(buffer); }
                catch (SocketException) { if (_socket == null) return; continue; }
                catch (ObjectDisposedException) { return; }
                if (!MouseStateCodec.TryRead(new ReadOnlySpan<byte>(buffer, 0, n), out var state)) continue;
                double now = _clock.NowSeconds;
                lock (_lock)
                {
                    if (_has && state.Seq <= _latest.Seq && _latest.Seq - state.Seq < RestartGap) continue;
                    _latest = state;
                    _latestAt = now;
                    _has = true;
                }
            }
        }

        public bool TryGetLatest(out MouseState state, out double receivedAt)
        {
            lock (_lock)
            {
                state = _latest;
                receivedAt = _latestAt;
                return _has;
            }
        }

        public void Stop()
        {
            var socket = _socket;
            _socket = null;
            socket?.Close();
        }
    }
}
