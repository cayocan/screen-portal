using System;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Screens
{
    /// <summary>
    /// Janelas espelhadas (3D, ADR 0018): <see cref="DetachWindowMessage"/> do host (Ctrl+Alt+D) cria a tela-espelho à
    /// frente da visão (o HostSync pede o stream como qualquer tela); janela fechada no PC (<see cref="ScreenFailedMessage"/>
    /// numa tela-espelho) tira a tela. Reanexar = remover a tela-espelho (o desfazer a espelha de novo).
    /// </summary>
    public sealed class WindowMirrors : IDisposable
    {
        private readonly ConnectionStateMachine _connection;
        private readonly WorkspaceStore _store;
        private readonly ICommandBus _bus;
        private readonly IHeadPoseSource _head;
        private readonly ScreenPortal.App.Cursor.WorldCursor _world;

        public WindowMirrors(ConnectionStateMachine connection, WorkspaceStore store, ICommandBus bus, IHeadPoseSource head,
            ScreenPortal.App.Cursor.WorldCursor world = null)
        {
            _world = world;
            _connection = connection;
            _store = store;
            _bus = bus;
            _head = head;
            _connection.ScreenMessage += OnMessage;
        }

        public static bool IsMirror(VirtualScreen screen) => screen.Source.Kind == ScreenSourceKind.Window;

        private void OnMessage(ControlMessage message)
        {
            switch (message)
            {
                case DetachWindowMessage detach:
                    Detach(detach);
                    break;
                case ScreenFailedMessage failed when failed.ScreenKey <= int.MaxValue
                    && _store.State.TryGetScreen(new ScreenId((int)failed.ScreenKey), out var screen) && IsMirror(screen):
                    _bus.Send(new RemoveScreen(screen.Id)); // a janela fechou no PC
                    break;
            }
        }

        private void Detach(DetachWindowMessage detach)
        {
            if (detach.Width % 8 != 0 || detach.Height % 8 != 0 || detach.Width == 0 || detach.Height == 0) return;
            foreach (var existing in _store.State.Screens)
                if (existing.Source.Equals(ScreenSource.Window(detach.WindowId))) return; // já espelhada
            var resolution = Resolution.Create((int)detach.Width, (int)detach.Height);
            var surface = _store.State.Surface;
            // arrastada para o vazio: nasce onde o cursor 3D está; pelo atalho: à frente da visão
            Placement? at = null;
            if (_world != null && _world.Active)
            {
                var p = surface.FromWorldPosition(_world.Point);
                at = Placement.OnSurface(p.AzimuthDeg, p.ElevationDeg, p.DistanceM);
            }
            var near = SurfaceSlots.Facing(surface, _head.HeadPose);
            _bus.Send(new CreateScreen(resolution, at, ScreenSizing.InitialWidthMeters(resolution), 0f, detach.Title,
                ScreenSource.Window(detach.WindowId), near));
        }

        /// <summary>Ctrl+Alt+Shift+D / menu: devolve a janela (tira o espelho).</summary>
        public bool Reattach(ScreenId id)
        {
            if (!_store.State.TryGetScreen(id, out var screen) || !IsMirror(screen)) return false;
            _bus.Send(new RemoveScreen(id));
            return true;
        }

        public void Dispose() => _connection.ScreenMessage -= OnMessage;
    }
}
