using System;
using System.Collections.Generic;
using ScreenPortal.App.Connection;

namespace ScreenPortal.App.Cursor
{
    /// <summary>Recebe o <see cref="MouseState"/> (UDP) do host. A implementação guarda só o mais recente.</summary>
    public interface IMouseStateSource
    {
        /// <returns>false se a porta não abriu.</returns>
        bool Start(int port);
        void Stop();
        /// <summary>O pacote mais novo recebido (false = nenhum ainda). Sem alocação.</summary>
        bool TryGetLatest(out MouseState state, out double receivedAt);
    }

    public sealed class NullMouseStateSource : IMouseStateSource
    {
        public bool Start(int port) => false;
        public void Stop() { }

        public bool TryGetLatest(out MouseState state, out double receivedAt)
        {
            state = default;
            receivedAt = 0;
            return false;
        }
    }

    /// <summary>
    /// Cursor do PC no headset (spec 3B §3–4): no Welcome abre a porta e pede o <c>MouseState</c> ao host
    /// (<see cref="StartCursorMessage"/>); guarda as formas que chegam pelo controle. <see cref="Tick"/> (1× por quadro)
    /// copia o estado mais recente; sem pacote há <see cref="StaleSeconds"/>, o cursor some.
    /// </summary>
    public sealed class CursorSync : IDisposable
    {
        public const int RxPort = 9310;
        public const double StaleSeconds = 1.0;

        private readonly ConnectionStateMachine _connection;
        private readonly IMouseStateSource _source;
        private readonly IClock _clock;
        private readonly Dictionary<uint, CursorShapeMessage> _shapes = new Dictionary<uint, CursorShapeMessage>();
        private bool _listening;

        public CursorSync(ConnectionStateMachine connection, IMouseStateSource source, IClock clock)
        {
            _connection = connection;
            _source = source;
            _clock = clock;
            _connection.Welcomed += OnWelcomed;
            _connection.ScreenMessage += OnMessage;
        }

        /// <summary>Estado do quadro. <see cref="MouseMode.Outside"/> também quando não há dado recente.</summary>
        public MouseState Current { get; private set; }

        /// <summary>Forma nova chegou (a view cria a textura uma vez).</summary>
        public event Action<CursorShapeMessage> ShapeAdded;

        public bool TryGetShape(uint id, out CursorShapeMessage shape) => _shapes.TryGetValue(id, out shape);

        private void OnWelcomed(WelcomeMessage _)
        {
            if (!_listening) _listening = _source.Start(RxPort);
            if (_listening) _connection.Send(new StartCursorMessage(RxPort));
        }

        private void OnMessage(ControlMessage message)
        {
            if (!(message is CursorShapeMessage shape) || _shapes.ContainsKey(shape.ShapeId)) return;
            _shapes[shape.ShapeId] = shape;
            ShapeAdded?.Invoke(shape);
        }

        public void Tick()
        {
            if (_connection.IsLinked && _source.TryGetLatest(out var state, out double at) && _clock.NowSeconds - at <= StaleSeconds)
            {
                Current = state;
                return;
            }
            var hidden = Current;
            hidden.Mode = MouseMode.Outside;
            Current = hidden;
        }

        public void Dispose()
        {
            _connection.Welcomed -= OnWelcomed;
            _connection.ScreenMessage -= OnMessage;
            if (_listening) _source.Stop();
        }
    }
}
