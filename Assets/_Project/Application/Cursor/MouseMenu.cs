using System;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Manipulation;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Cursor
{
    /// <summary>
    /// Menu do app pelo mouse (3C, brief §10.10): <c>Ctrl+Alt+M</c> abre o mesmo menu do pulso flutuando na direção do
    /// cursor, a <see cref="DistanceM"/> da cabeça, virado para ela. Aberto, segura o modo edição (o mouse vira o cursor 3D
    /// e o clique escolhe o item); fecha com Esc, <c>Ctrl+Alt+M</c> ou clique fora.
    /// </summary>
    public sealed class MouseMenu
    {
        public const float DistanceM = 0.7f;

        private readonly EditMode _edit;
        private readonly IHeadPoseSource _head;
        private readonly CursorSync _sync;
        private readonly WorldCursor _world;
        private readonly WorkspaceStore _store;

        public MouseMenu(EditMode edit, IHeadPoseSource head, CursorSync sync, WorldCursor world, WorkspaceStore store)
        {
            _edit = edit;
            _head = head;
            _sync = sync;
            _world = world;
            _store = store;
        }

        public bool Open { get; private set; }
        public Pose Pose { get; private set; }

        public void Toggle()
        {
            if (Open) Close();
            else OpenAtCursor();
        }

        public void Close()
        {
            if (!Open) return;
            Open = false;
            _edit.SetMenuHold(false);
        }

        private void OpenAtCursor()
        {
            var head = _head.HeadPose;
            var toward = CursorPoint(out var point) ? point - head.Position : head.Rotation * Vec3.Forward;
            if (toward.Length < 1e-4f) toward = head.Rotation * Vec3.Forward;
            var position = head.Position + toward.Normalized * DistanceM;
            Pose = new Pose(position, Quat.LookRotation(position - head.Position, Vec3.Up));
            Open = true;
            _edit.SetMenuHold(true);
        }

        /// <summary>Onde o cursor está agora: o cursor 3D, ou o pixel na tela.</summary>
        private bool CursorPoint(out Vec3 point)
        {
            point = default;
            if (_world.Active)
            {
                point = _world.Point;
                return true;
            }
            var s = _sync.Current;
            if (s.Mode != MouseMode.Screen || !_store.State.TryGetScreen(new ScreenId((int)s.ScreenKey), out var screen)) return false;
            CursorPlacement.PixelToLocal(screen, s.X, s.Y, out float lx, out float ly);
            point = CursorPlacement.LocalPose(screen, _store.State.Surface, lx, ly, 0f).Position;
            return true;
        }
    }
}
