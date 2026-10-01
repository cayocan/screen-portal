using System;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Manipulation
{
    /// <summary>
    /// Atalhos globais do PC (spec 3A §5, mouse-first): cada <see cref="HotkeyMessage"/> vira o Command da ação na tela
    /// sob o cursor do mouse. Tela 0 (cursor fora das telas do Quest) → só as ações globais. Passos fixos; os limites são
    /// os dos próprios Commands (e um passo que não muda nada não vira entrada no desfazer).
    /// </summary>
    public sealed class HotkeyDispatcher : IDisposable
    {
        public const float MoveStepDeg = 5f;
        public const float SizeFactor = 1.1f;
        public const float DistanceStepM = 0.1f;
        public const float CurvatureStep = 0.1f;

        private readonly WorkspaceStore _store;
        private readonly ICommandBus _bus;
        private readonly IHeadPoseSource _head;
        private readonly ConnectionStateMachine _connection;
        private readonly EditMode _edit;
        private readonly ScreenManipulationController _manipulation;
        private readonly ScreenPortal.App.Cursor.WorldCursor _world;

        public HotkeyDispatcher(WorkspaceStore store, ICommandBus bus, IHeadPoseSource head, ConnectionStateMachine connection = null,
            EditMode edit = null, ScreenManipulationController manipulation = null, ScreenPortal.App.Cursor.WorldCursor world = null)
        {
            _world = world;
            _edit = edit;
            _manipulation = manipulation;
            _store = store;
            _bus = bus;
            _head = head;
            _connection = connection;
            if (_connection != null) _connection.ScreenMessage += OnMessage;
        }

        private void OnMessage(ControlMessage message)
        {
            if (message is HotkeyMessage hotkey) Handle(hotkey);
        }

        public void Handle(HotkeyMessage hotkey)
        {
            var ws = _store.State;
            switch (hotkey.Action)
            {
                case HotkeyAction.ToggleEditMode: _edit?.Toggle(); return;
                case HotkeyAction.FindCursor: _world?.CenterOnView(); return;
                case HotkeyAction.Escape:
                    if (_manipulation != null && _manipulation.IsDragging) _edit?.RequestCancel(); // Esc cancela o arraste
                    else _edit?.Set(false); // parado: sai do modo edição
                    return;
                case HotkeyAction.Undo: _bus.Undo(); return;
                case HotkeyAction.Redo: _bus.Redo(); return;
                case HotkeyAction.Recenter:
                    var head = _head.HeadPose;
                    _bus.Send(new RecenterLayout(head.Position, LayoutSurface.HeadingOf(head.Rotation)));
                    return;
                case HotkeyAction.ToggleLayoutMode:
                    _bus.Send(new SetLayoutMode(ws.Surface.Mode == LayoutMode.Cylinder ? LayoutMode.Sphere : LayoutMode.Cylinder));
                    return;
                case HotkeyAction.AlignPosture: _bus.Send(new AlignSurfaceToPosture(_head.HeadPose)); return;
                case HotkeyAction.ToggleGrid:
                    _bus.Send(new SetSnapOptions(ws.Surface.Snap.WithAngularGrid(!ws.Surface.Snap.AngularGrid)));
                    return;
            }

            if (hotkey.ScreenKey == 0 || hotkey.ScreenKey > int.MaxValue
                || !ws.TryGetScreen(new ScreenId((int)hotkey.ScreenKey), out var screen)) return;
            var id = screen.Id;
            switch (hotkey.Action)
            {
                case HotkeyAction.MoveLeft: Move(screen, -MoveStepDeg, 0f); break;
                case HotkeyAction.MoveRight: Move(screen, MoveStepDeg, 0f); break;
                case HotkeyAction.MoveUp: Move(screen, 0f, MoveStepDeg); break;
                case HotkeyAction.MoveDown: Move(screen, 0f, -MoveStepDeg); break;
                case HotkeyAction.Bigger: Resize(screen, screen.WidthMeters * SizeFactor); break;
                case HotkeyAction.Smaller: Resize(screen, screen.WidthMeters / SizeFactor); break;
                case HotkeyAction.Closer: Distance(screen, ws, -DistanceStepM); break;
                case HotkeyAction.Farther: Distance(screen, ws, DistanceStepM); break;
                case HotkeyAction.CurvatureToggle: _bus.Send(new ToggleScreenCurvature(id)); break;
                case HotkeyAction.CurvatureLess: Curve(screen, -CurvatureStep); break;
                case HotkeyAction.CurvatureMore: Curve(screen, CurvatureStep); break;
                case HotkeyAction.CycleFrameRate:
                    if (screen.Source.HasVideo) _bus.Send(new SetFrameRateMode(id, NextFrameRate(screen.FrameRateMode)));
                    break;
            }
        }

        /// <summary>Auto → Vídeo → Eco → Fixo 30 → Auto (o mesmo que o menu oferece, na ordem mais útil para o teclado).</summary>
        public static FrameRateMode NextFrameRate(FrameRateMode mode)
        {
            switch (mode.Kind)
            {
                case FrameRateKind.Auto: return FrameRateMode.Video;
                case FrameRateKind.Video: return FrameRateMode.Eco;
                case FrameRateKind.Eco: return FrameRateMode.Fixed(30);
                default: return FrameRateMode.Auto;
            }
        }

        private void Move(VirtualScreen screen, float dAz, float dEl)
        {
            var p = screen.Placement;
            if (p.IsLoose) return; // tela solta é livre no espaço: o teclado só mexe na superfície
            _bus.Send(new MoveScreenOnSurface(screen.Id, p.AzimuthDeg + dAz, p.ElevationDeg + dEl));
        }

        private void Resize(VirtualScreen screen, float width)
        {
            if (Math.Abs(ScreenLimits.ClampWidth(width) - screen.WidthMeters) < 1e-4f) return;
            _bus.Send(new ResizeScreen(screen.Id, width));
        }

        private void Distance(VirtualScreen screen, Workspace ws, float delta)
        {
            var p = screen.Placement;
            if (p.IsLoose) return;
            float current = ws.Surface.Mode == LayoutMode.Sphere ? p.DistanceM : CylinderMath.HorizontalRadius(p);
            float next = Placement.ClampDistance(current + delta);
            if (Math.Abs(next - current) < 1e-4f) return;
            _bus.Send(new SetScreenDistance(screen.Id, next));
        }

        private void Curve(VirtualScreen screen, float delta)
        {
            float next = ScreenLimits.Clamp01(screen.Curvature + delta);
            if (Math.Abs(next - screen.Curvature) < 1e-4f) return;
            _bus.Send(new SetScreenCurvature(screen.Id, next));
        }

        public void Dispose()
        {
            if (_connection != null) _connection.ScreenMessage -= OnMessage;
        }
    }
}
