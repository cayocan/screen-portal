using System;
using ScreenPortal.App;
using ScreenPortal.App.Manipulation;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Presentation.Screens;
using UnityEngine;
using Object = UnityEngine.Object;
using Pose = ScreenPortal.Domain.Spatial.Pose;

namespace ScreenPortal.Presentation.Manipulation
{
    /// <summary>
    /// Desenha o que o controlador decide (nunca muda estado): raios, moldura + alças no hover/arraste, guias do
    /// encaixe e a pré-visualização da tela. Ao terminar sem Command (cancelar), devolve a view ao estado confirmado.
    /// Sem alocação por quadro (arrays e materiais criados no construtor).
    /// </summary>
    public sealed class ManipulationPresenter : IDisposable
    {
        private const float FrameOutsetM = 0.015f;
        private const float LineWidthM = 0.004f;
        private const float HandleSizeM = 0.04f;
        private const float RayLengthM = 3f;
        private const int GuideArcPoints = 12;
        private static readonly Color HoverColor = new Color(1f, 1f, 1f);
        private static readonly Color DragColor = new Color(0.25f, 0.8f, 1f);
        private static readonly Color GuideColor = new Color(1f, 0.8f, 0.2f);
        private static readonly Color RayColor = new Color(0.9f, 0.9f, 0.9f);

        private readonly ScreenManipulationController _controller;
        private readonly WorkspaceStore _store;
        private readonly OverlayScreenViewFactory _views;
        private readonly InteractionRouter _router;
        private GameObject _root;
        private readonly LineRenderer _frame;
        private readonly LineRenderer _guideAzimuth;
        private readonly LineRenderer _guideElevation;
        private readonly LineRenderer _rayRight;
        private readonly LineRenderer _rayLeft;
        private readonly Transform[] _handles = new Transform[4];
        private readonly Vector3[] _corners = new Vector3[4];
        private readonly Vector3[] _two = new Vector3[2];
        private readonly Vector3[] _arc = new Vector3[GuideArcPoints];
        private ScreenId _previewing;

        public ManipulationPresenter(Transform parent, Material unlit, ScreenManipulationController controller,
            WorkspaceStore store, OverlayScreenViewFactory views, InteractionRouter router)
        {
            _controller = controller;
            _store = store;
            _views = views;
            _router = router;
            _root = new GameObject("Manipulation");
            _root.transform.SetParent(parent, false);
            _frame = Line("Frame", unlit, 4, true, DragColor);
            _guideAzimuth = Line("GuideAzimuth", unlit, 2, false, GuideColor);
            _guideElevation = Line("GuideElevation", unlit, GuideArcPoints, false, GuideColor);
            _rayRight = Line("RayRight", unlit, 2, false, RayColor);
            _rayLeft = Line("RayLeft", unlit, 2, false, RayColor);
            var handleMaterial = unlit != null ? new Material(unlit) { color = DragColor } : null;
            for (int i = 0; i < 4; i++)
            {
                var handle = GameObject.CreatePrimitive(PrimitiveType.Quad);
                handle.name = "Handle" + i;
                handle.GetComponent<Collider>().enabled = false;
                if (handleMaterial != null) handle.GetComponent<MeshRenderer>().sharedMaterial = handleMaterial;
                handle.transform.SetParent(_root.transform, false);
                handle.SetActive(false);
                _handles[i] = handle.transform;
            }
        }

        public void Tick()
        {
            DrawRay(_rayRight, _router.LastRight);
            DrawRay(_rayLeft, _router.LastLeft);

            var ws = _store.State;
            var preview = _controller.Preview;
            if (preview.Active)
            {
                if (_views.TryGetView(preview.Screen, out var view)) view.ApplyTransform(preview.Pose, preview.WidthM, preview.HeightM);
                _previewing = preview.Screen;
                DrawFrame(preview.Pose, preview.WidthM, preview.HeightM, DragColor, _controller.State == ManipulationState.Resizing);
                DrawGuides(preview, ws.Surface);
                return;
            }

            if (!_previewing.IsNone)
            {
                RestoreConfirmed(ws, _previewing);
                _previewing = ScreenId.None;
            }
            _guideAzimuth.enabled = false;
            _guideElevation.enabled = false;

            var hover = _controller.HoverHit;
            if (_controller.State == ManipulationState.Hover && ws.TryGetScreen(hover.Screen, out var screen))
                DrawFrame(ws.Surface.ToPose(screen.Placement), screen.WidthMeters, screen.HeightMeters, HoverColor, hover.Region == HitRegion.Handle);
            else
                HideFrame();
        }

        private void RestoreConfirmed(Workspace ws, ScreenId id)
        {
            if (ws.TryGetScreen(id, out var screen) && _views.TryGetView(id, out var view))
                view.Apply(screen, ws.Surface.ToPose(screen.Placement));
        }

        private void DrawRay(LineRenderer line, ManipulationInput input)
        {
            if (!input.HasRay)
            {
                line.enabled = false;
                return;
            }
            var hit = ScreenHitTest.Pick(_store.State, input.Origin, input.Direction);
            float length = hit.Region != HitRegion.None ? hit.Distance : RayLengthM;
            _two[0] = input.Origin.ToUnity();
            _two[1] = (input.Origin + input.Direction * length).ToUnity();
            line.SetPositions(_two);
            line.enabled = true;
        }

        private void DrawFrame(Pose pose, float width, float height, Color color, bool emphasizeHandles)
        {
            var position = pose.Position.ToUnity();
            var rotation = pose.Rotation.ToUnity();
            float x = width * 0.5f + FrameOutsetM;
            float y = height * 0.5f + FrameOutsetM;
            _corners[0] = position + rotation * new Vector3(-x, -y, 0f);
            _corners[1] = position + rotation * new Vector3(-x, y, 0f);
            _corners[2] = position + rotation * new Vector3(x, y, 0f);
            _corners[3] = position + rotation * new Vector3(x, -y, 0f);
            _frame.SetPositions(_corners);
            if (_frame.sharedMaterial != null) _frame.sharedMaterial.color = color;
            _frame.enabled = true;

            float size = HandleSizeM * (emphasizeHandles ? 1.5f : 1f);
            for (int i = 0; i < 4; i++)
            {
                float sx = i < 2 ? -1f : 1f;
                float sy = i == 1 || i == 2 ? 1f : -1f;
                _handles[i].SetPositionAndRotation(position + rotation * new Vector3(sx * width * 0.5f, sy * height * 0.5f, -0.002f), rotation);
                _handles[i].localScale = new Vector3(size, size, 1f);
                _handles[i].gameObject.SetActive(true);
            }
        }

        private void HideFrame()
        {
            _frame.enabled = false;
            for (int i = 0; i < 4; i++) _handles[i].gameObject.SetActive(false);
        }

        private void DrawGuides(ManipulationPreview preview, LayoutSurface surface)
        {
            var p = preview.Placement;
            bool onSurface = !preview.IsLoose;
            if (onSurface && preview.HasGuideAzimuth)
            {
                _two[0] = surface.ToPose(Placement.OnSurface(preview.GuideAzimuthDeg, p.ElevationDeg - 25f, p.DistanceM)).Position.ToUnity();
                _two[1] = surface.ToPose(Placement.OnSurface(preview.GuideAzimuthDeg, p.ElevationDeg + 25f, p.DistanceM)).Position.ToUnity();
                _guideAzimuth.SetPositions(_two);
                _guideAzimuth.enabled = true;
            }
            else
            {
                _guideAzimuth.enabled = false;
            }

            if (onSurface && preview.HasGuideElevation)
            {
                for (int i = 0; i < GuideArcPoints; i++)
                {
                    float az = p.AzimuthDeg - 40f + 80f * i / (GuideArcPoints - 1);
                    _arc[i] = surface.ToPose(Placement.OnSurface(az, preview.GuideElevationDeg, p.DistanceM)).Position.ToUnity();
                }
                _guideElevation.SetPositions(_arc);
                _guideElevation.enabled = true;
            }
            else
            {
                _guideElevation.enabled = false;
            }
        }

        private LineRenderer Line(string name, Material unlit, int points, bool loop, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = loop;
            line.positionCount = points;
            line.widthMultiplier = LineWidthM;
            if (unlit != null) line.sharedMaterial = new Material(unlit) { color = color };
            line.enabled = false;
            return line;
        }

        public void Dispose()
        {
            if (_root == null) return;
            if (Application.isPlaying) Object.Destroy(_root);
            else Object.DestroyImmediate(_root);
            _root = null;
        }
    }
}
