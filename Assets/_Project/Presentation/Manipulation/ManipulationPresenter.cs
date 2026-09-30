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
    /// Desenha o que o controlador decide (nunca muda estado): raios, moldura + alças no hover/arraste (seguindo a curva
    /// da tela, spec 2E §4), alça e rótulo de curvatura, guias do encaixe e a pré-visualização da tela. Ao terminar sem
    /// Command (cancelar), devolve a view ao estado confirmado.
    /// Sem alocação por quadro (arrays e materiais criados no construtor).
    /// </summary>
    public sealed class ManipulationPresenter : IDisposable
    {
        private const float FrameOutsetM = 0.015f;
        private const float LineWidthM = 0.004f;
        private const float HandleSizeM = 0.04f;
        private const float RayLengthM = 3f;
        private const int GuideArcPoints = 12;
        private const int FrameEdgePoints = 25;            // pontos por borda horizontal (a moldura acompanha o arco)
        private const float CurveHandleHeightM = 0.012f;
        private const float CurveLabelAboveM = 0.05f;
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
        private readonly Transform _curveHandle;
        private readonly TextMesh _curveLabel;
        private readonly Vector3[] _frameLine = new Vector3[2 * FrameEdgePoints];
        private int _labelPercent = -1;
        private int _labelRadius = -1;
        private bool _labelLimited;
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
            _frame = Line("Frame", unlit, 2 * FrameEdgePoints, true, DragColor);
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

            var curveHandle = GameObject.CreatePrimitive(PrimitiveType.Quad);
            curveHandle.name = "CurvatureHandle";
            curveHandle.GetComponent<Collider>().enabled = false;
            if (handleMaterial != null) curveHandle.GetComponent<MeshRenderer>().sharedMaterial = handleMaterial;
            curveHandle.transform.SetParent(_root.transform, false);
            curveHandle.SetActive(false);
            _curveHandle = curveHandle.transform;

            var labelGo = new GameObject("CurvatureLabel");
            labelGo.transform.SetParent(_root.transform, false);
            _curveLabel = labelGo.AddComponent<TextMesh>();
            _curveLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelGo.GetComponent<MeshRenderer>().sharedMaterial = StereoText.MaterialFor(_curveLabel.font);
            _curveLabel.fontSize = 96;
            _curveLabel.characterSize = 0.0025f;
            _curveLabel.anchor = TextAnchor.LowerCenter;
            _curveLabel.alignment = TextAlignment.Center;
            _curveLabel.color = Color.white;
            labelGo.SetActive(false);
        }

        public void Tick()
        {
            DrawRay(_rayRight, _router.LastRight);
            DrawRay(_rayLeft, _router.LastLeft);

            var ws = _store.State;
            var preview = _controller.Preview;
            if (preview.Active)
            {
                var curve = CurvedGeometry.Of(preview.Curvature, preview.Placement, preview.WidthM, ws.Surface);
                if (_views.TryGetView(preview.Screen, out var view)) view.ApplyTransform(preview.Pose, preview.WidthM, preview.HeightM, curve.RadiusM);
                _previewing = preview.Screen;
                bool curving = _controller.State == ManipulationState.Curving;
                DrawFrame(preview.Pose, preview.WidthM, preview.HeightM, curve.RadiusM, DragColor,
                    _controller.State == ManipulationState.Resizing, curving);
                if (curving) ShowCurveLabel(preview.Pose, curve.RadiusM, preview.HeightM, curve);
                else _curveLabel.gameObject.SetActive(false);
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
            {
                var pose = ws.Surface.ToPose(screen.Placement);
                var curve = CurvedGeometry.Of(screen, ws.Surface);
                bool onCurveHandle = hover.Region == HitRegion.CurvatureHandle;
                DrawFrame(pose, screen.WidthMeters, screen.HeightMeters, curve.RadiusM, HoverColor, hover.Region == HitRegion.Handle, onCurveHandle);
                if (onCurveHandle) ShowCurveLabel(pose, curve.RadiusM, screen.HeightMeters, curve);
                else _curveLabel.gameObject.SetActive(false);
            }
            else
            {
                HideFrame();
            }
        }

        private void RestoreConfirmed(Workspace ws, ScreenId id)
        {
            if (ws.TryGetScreen(id, out var screen) && _views.TryGetView(id, out var view))
                view.Apply(screen, ws.Surface.ToPose(screen.Placement), CurvedGeometry.Of(screen, ws.Surface).RadiusM);
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

        private void DrawFrame(Pose pose, float width, float height, float radius, Color color, bool emphasizeHandles, bool emphasizeCurveHandle)
        {
            float x = width * 0.5f + FrameOutsetM;
            float y = height * 0.5f + FrameOutsetM;
            // loop: borda de cima da esquerda para a direita, borda de baixo da direita para a esquerda
            for (int i = 0; i < FrameEdgePoints; i++)
            {
                float lx = -x + 2f * x * i / (FrameEdgePoints - 1);
                _frameLine[i] = CurvedGeometry.SurfacePoint(pose, radius, lx, y).ToUnity();
                _frameLine[2 * FrameEdgePoints - 1 - i] = CurvedGeometry.SurfacePoint(pose, radius, lx, -y).ToUnity();
            }
            _frame.SetPositions(_frameLine);
            if (_frame.sharedMaterial != null) _frame.sharedMaterial.color = color;
            _frame.enabled = true;

            float size = HandleSizeM * (emphasizeHandles ? 1.5f : 1f);
            for (int i = 0; i < 4; i++)
            {
                float lx = (i < 2 ? -1f : 1f) * width * 0.5f;
                float ly = (i == 1 || i == 2 ? 1f : -1f) * height * 0.5f;
                PlaceOnSurface(_handles[i], pose, radius, lx, ly, size, size);
            }

            float scale = emphasizeCurveHandle ? 1.5f : 1f;
            PlaceOnSurface(_curveHandle, pose, radius, 0f, y, ScreenHitTest.CurvatureHandleWidthM * scale, CurveHandleHeightM * scale);
        }

        /// <summary>Quad tangente à tela no ponto local (lx, ly), 2 mm para o lado do usuário.</summary>
        private static void PlaceOnSurface(Transform t, Pose pose, float radius, float lx, float ly, float sx, float sy)
        {
            float yaw = radius > 0f ? lx / radius * Mathf.Rad2Deg : 0f;
            var rotation = pose.Rotation.ToUnity() * Quaternion.Euler(0f, yaw, 0f);
            var position = CurvedGeometry.SurfacePoint(pose, radius, lx, ly).ToUnity() + rotation * new Vector3(0f, 0f, -0.002f);
            t.SetPositionAndRotation(position, rotation);
            t.localScale = new Vector3(sx, sy, 1f);
            t.gameObject.SetActive(true);
        }

        private void ShowCurveLabel(Pose pose, float radius, float height, CurvedGeometry curve)
        {
            if (curve.Percent != _labelPercent || curve.RoundedRadiusMm != _labelRadius || curve.ArcLimited != _labelLimited)
            {
                _labelPercent = curve.Percent;
                _labelRadius = curve.RoundedRadiusMm;
                _labelLimited = curve.ArcLimited;
                _curveLabel.text = curve.Label(); // aloca só quando o valor muda
            }
            PlaceOnSurface(_curveLabel.transform, pose, radius, 0f, height * 0.5f + CurveLabelAboveM, 1f, 1f);
        }

        private void HideFrame()
        {
            _frame.enabled = false;
            for (int i = 0; i < 4; i++) _handles[i].gameObject.SetActive(false);
            _curveHandle.gameObject.SetActive(false);
            _curveLabel.gameObject.SetActive(false);
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
