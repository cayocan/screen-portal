using System;
using System.Collections.Generic;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Manipulation
{
    public enum ManipulationState
    {
        Idle,
        Hover,
        Moving,
        Resizing,
        /// <summary>Arrastando a alça de curvatura (spec 2E §3).</summary>
        Curving,
    }

    /// <summary>
    /// State Machine da manipulação (brief §10.10, spec 2B §4, 2E §3): Idle → Hover(moldura/alças) → Moving | Resizing |
    /// Curving → Commit | Cancel. Pré-visualização pura a cada quadro; **um** Command ao soltar. Um ponteiro por vez:
    /// o outro só modifica (sem encaixe, solto) ou cancela. Ponteiro 0 = direita, 1 = esquerda. Sem alocação por quadro.
    /// </summary>
    public sealed class ScreenManipulationController
    {
        /// <summary>30 cm ao longo da normal da tela = 100 % (spec 2E §3): para longe curva, para perto achata.</summary>
        public const float CurvatureDragM = 0.3f;

        private readonly WorkspaceStore _store;
        private readonly ICommandBus _bus;
        private readonly List<AngularRect> _others = new List<AngularRect>();
        private bool _prevSelectRight;
        private bool _prevSelectLeft;

        private ScreenId _screen;
        private Placement _startPlacement;
        private Pose _startPose;
        private float _startWidth;
        private float _aspect;
        private float _startRadius;
        private float _radiusOffset;
        private float _offsetAzimuth;
        private float _offsetVertical;
        private float _resizeStartDistance;
        private bool _loose;
        private float _looseDistance;
        private Vec3 _looseOffset;
        private float _startCurvature;
        private float _startCurveRadius;
        private Vec3 _curveOrigin;
        private Vec3 _curveNormal;

        /// <summary>Modo edição (3B): o conteúdo da tela também pega e move (o clique não vai ao Windows nesse modo).</summary>
        public bool GrabContent { get; set; }

        public ManipulationState State { get; private set; }
        public ScreenHit HoverHit { get; private set; }
        public int HoverPointer { get; private set; } = -1;
        public int ActivePointer { get; private set; } = -1;
        public ManipulationPreview Preview { get; } = new ManipulationPreview();
        public bool IsDragging => State == ManipulationState.Moving || State == ManipulationState.Resizing || State == ManipulationState.Curving;

        public ScreenManipulationController(WorkspaceStore store, ICommandBus bus)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        public void Update(in ManipulationInput right, in ManipulationInput left)
        {
            if (IsDragging)
            {
                if (ActivePointer == 0) UpdateDrag(in right, in left);
                else UpdateDrag(in left, in right);
            }
            else
            {
                UpdateHover(in right, in left);
            }
            _prevSelectRight = right.SelectHeld;
            _prevSelectLeft = left.SelectHeld;
        }

        private void UpdateHover(in ManipulationInput right, in ManipulationInput left)
        {
            State = ManipulationState.Idle;
            HoverHit = default;
            HoverPointer = -1;
            var ws = _store.State;
            if (TryHover(0, in right, _prevSelectRight, ws)) return;
            TryHover(1, in left, _prevSelectLeft, ws);
        }

        /// <returns>true se começou um arraste.</returns>
        private bool TryHover(int pointer, in ManipulationInput input, bool previousSelect, Workspace ws)
        {
            if (!input.HasRay) return false;
            var hit = ScreenHitTest.Pick(ws, input.Origin, input.Direction);
            if (hit.Region != HitRegion.Frame && hit.Region != HitRegion.Handle && hit.Region != HitRegion.CurvatureHandle
                && !(GrabContent && hit.Region == HitRegion.Content)) return false;
            if (HoverPointer < 0)
            {
                HoverHit = hit;
                HoverPointer = pointer;
                State = ManipulationState.Hover;
            }
            if (!input.SelectHeld || previousSelect) return false;
            Begin(pointer, in input, hit, ws);
            return true;
        }

        private void Begin(int pointer, in ManipulationInput input, ScreenHit hit, Workspace ws)
        {
            if (!ws.TryGetScreen(hit.Screen, out var screen)) return;
            var surface = ws.Surface;
            _screen = screen.Id;
            ActivePointer = pointer;
            HoverHit = hit;
            HoverPointer = pointer;
            _startPlacement = screen.Placement;
            _startWidth = screen.WidthMeters;
            _aspect = screen.Resolution.AspectRatio;
            _startPose = surface.ToPose(screen.Placement);
            _startCurvature = screen.Curvature;
            _startCurveRadius = CurvedGeometry.Of(screen, surface).RadiusM;
            Preview.Begin(screen.Id, _startPose, screen.Placement, screen.WidthMeters, screen.HeightMeters, screen.Curvature);
            if (ws.FocusedScreenId != screen.Id) _bus.Send(new SetFocus(screen.Id));

            if (hit.Region == HitRegion.Handle)
            {
                State = ManipulationState.Resizing;
                // coordenadas locais do hit: comprimento do arco na tela curva (na plana é a distância ao centro)
                _resizeStartDistance = Math.Max(0.01f, (float)Math.Sqrt(hit.LocalX * hit.LocalX + hit.LocalY * hit.LocalY));
                return;
            }
            if (hit.Region == HitRegion.CurvatureHandle)
            {
                State = ManipulationState.Curving;
                _curveOrigin = input.Origin;
                _curveNormal = _startPose.Rotation * Vec3.Forward;
                return;
            }

            State = ManipulationState.Moving;
            CollectOthers(ws, screen.Id);
            _loose = false;
            if (screen.Placement.IsLoose) EnterLoose(in input);
            else AnchorSurfaceGrab(surface, in input, screen.Placement);
        }

        private void UpdateDrag(in ManipulationInput active, in ManipulationInput other)
        {
            if (active.CancelPressed || other.CancelPressed)
            {
                Reset();
                return;
            }
            if (!active.SelectHeld)
            {
                Commit();
                return;
            }
            if (!active.HasRay) return;
            if (State == ManipulationState.Resizing)
            {
                UpdateResize(in active);
                return;
            }
            if (State == ManipulationState.Curving)
            {
                UpdateCurving(in active);
                return;
            }

            bool loose = _startPlacement.IsLoose || active.GripHeld || other.GripHeld;
            if (loose)
            {
                if (!_loose) EnterLoose(in active);
                UpdateLoose(in active);
                return;
            }

            var surface = _store.State.Surface;
            if (_loose)
            {
                // grip solto no meio do arraste: volta a andar sobre a superfície a partir de onde está
                _loose = false;
                AnchorSurfaceGrab(surface, in active, surface.FromWorldPosition(Preview.Pose.Position));
            }

            _radiusOffset += active.DistanceDeltaM;
            float radius = Placement.ClampDistance(_startRadius + _radiusOffset);
            if (!SurfaceRay.TryIntersect(surface, radius, active.Origin, active.Direction, out float az, out float vertical)) return;
            az += _offsetAzimuth;
            vertical += _offsetVertical;
            var candidate = surface.Mode == LayoutMode.Cylinder
                ? CylinderMath.FromAzimuthHeight(az, vertical, radius)
                : Placement.OnSurface(az, vertical, radius);

            Preview.ClearGuides();
            if (!other.SelectHeld) candidate = Snap(surface, candidate, radius);
            Preview.SetPlacement(candidate, surface.ToPose(candidate));
        }

        private void AnchorSurfaceGrab(LayoutSurface surface, in ManipulationInput input, Placement placement)
        {
            _startRadius = surface.Mode == LayoutMode.Cylinder ? CylinderMath.HorizontalRadius(placement) : placement.DistanceM;
            _radiusOffset = 0f;
            if (SurfaceRay.TryIntersect(surface, _startRadius, input.Origin, input.Direction, out float az, out float vertical))
            {
                _offsetAzimuth = placement.AzimuthDeg - az;
                _offsetVertical = Vertical(surface, placement) - vertical;
            }
            else
            {
                _offsetAzimuth = 0f;
                _offsetVertical = 0f;
            }
        }

        private static float Vertical(LayoutSurface surface, Placement p) =>
            surface.Mode == LayoutMode.Cylinder ? CylinderMath.Height(p) : p.ElevationDeg;

        private Placement Snap(LayoutSurface surface, Placement candidate, float radius)
        {
            var rect = ScreenAngles.Of(candidate, Preview.WidthM, Preview.HeightM, surface);
            var result = SurfaceSnapper.Snap(rect, _others, surface.Snap);
            if (!result.SnappedAzimuth && !result.SnappedElevation) return candidate;
            Preview.SetGuides(result.SnappedAzimuth, result.GuideAzimuthDeg, result.SnappedElevation, result.GuideElevationDeg);
            return surface.Mode == LayoutMode.Cylinder
                ? CylinderMath.FromAzimuthElevationAtRadius(result.AzimuthDeg, result.ElevationDeg, radius)
                : Placement.OnSurface(result.AzimuthDeg, result.ElevationDeg, radius);
        }

        private void EnterLoose(in ManipulationInput input)
        {
            var center = Preview.Pose.Position;
            var dir = input.Direction.Normalized;
            _looseDistance = Math.Max(0.1f, Vec3.Dot(center - input.Origin, dir));
            _looseOffset = center - (input.Origin + dir * _looseDistance);
            _loose = true;
        }

        private void UpdateLoose(in ManipulationInput input)
        {
            var dir = input.Direction.Normalized;
            var position = input.Origin + dir * _looseDistance + _looseOffset;
            var toScreen = position - input.Origin;
            var flat = new Vec3(toScreen.X, 0f, toScreen.Z);
            var rotation = flat.Length > 1e-3f ? Quat.LookRotation(flat, Vec3.Up) : Preview.Pose.Rotation;
            Preview.ClearGuides();
            Preview.SetLoose(new Pose(position, rotation));
        }

        private void UpdateResize(in ManipulationInput input)
        {
            if (!ScreenHitTest.RaycastScreen(input.Origin, input.Direction, _startPose, _startCurveRadius, out _, out float lx, out float ly)) return;
            float distance = (float)Math.Sqrt(lx * lx + ly * ly);
            float width = ScreenLimits.ClampWidth(_startWidth * distance / _resizeStartDistance);
            Preview.SetSize(width, width / _aspect);
        }

        /// <summary>Quanto o ponteiro andou ao longo da normal da tela desde o gatilho (spec 2E §3). Só prévia.</summary>
        private void UpdateCurving(in ManipulationInput input)
        {
            float pushed = Vec3.Dot(input.Origin - _curveOrigin, _curveNormal);
            Preview.SetCurvature(ScreenLimits.Clamp01(_startCurvature + pushed / CurvatureDragM));
        }

        private void Commit()
        {
            var id = _screen;
            bool resizing = State == ManipulationState.Resizing;
            bool curving = State == ManipulationState.Curving;
            bool loose = Preview.IsLoose;
            var placement = Preview.Placement;
            var pose = Preview.Pose;
            float width = Preview.WidthM;
            float curvature = Preview.Curvature;
            Reset();

            if (curving)
            {
                if (Math.Abs(curvature - _startCurvature) > 1e-4f) _bus.Send(new SetScreenCurvature(id, curvature));
                return;
            }

            if (resizing)
            {
                if (Math.Abs(width - _startWidth) > 1e-4f) _bus.Send(new ResizeScreen(id, width));
                return;
            }
            if (loose)
            {
                bool unchanged = _startPlacement.IsLoose && pose.Equals(_startPlacement.LoosePose);
                if (!unchanged) _bus.Send(new SetScreenLoose(id, pose));
                return;
            }
            if (!placement.Equals(_startPlacement))
                _bus.Send(new MoveScreenOnSurface(id, placement.AzimuthDeg, placement.ElevationDeg, placement.DistanceM));
        }

        private void Reset()
        {
            State = ManipulationState.Idle;
            ActivePointer = -1;
            _loose = false;
            Preview.End();
        }

        private void CollectOthers(Workspace ws, ScreenId moving)
        {
            _others.Clear();
            var screens = ws.Screens;
            for (int i = 0; i < screens.Count; i++)
            {
                if (screens[i].Id == moving) continue;
                if (ScreenAngles.TryOf(screens[i], ws.Surface, out var rect)) _others.Add(rect);
            }
        }
    }
}
