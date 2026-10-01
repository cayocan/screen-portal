using System;
using ScreenPortal.App;
using ScreenPortal.App.Manipulation;
using UnityEngine;

namespace ScreenPortal.Presentation.Manipulation
{
    /// <summary>
    /// Desenha o gizmo do modo edição (setas X/Y, anéis X/Y/Z, cubo de escala) na tela em foco; durante o arraste segue a
    /// prévia. Cores como na Unity (X vermelho, Y verde, Z azul); a parte sob o ponteiro ou arrastada fica amarela.
    /// Linhas pré-alocadas; sem alocação por quadro.
    /// </summary>
    public sealed class GizmoView : IDisposable
    {
        private const int RingPoints = 64;
        private const float LineWidth = 0.004f; // × tamanho do gizmo / 0,156 m (tamanho a 1,3 m)
        private static readonly Color RedX = new Color(0.95f, 0.25f, 0.25f);
        private static readonly Color GreenY = new Color(0.35f, 0.9f, 0.3f);
        private static readonly Color BlueZ = new Color(0.3f, 0.55f, 1f);
        private static readonly Color Neutral = new Color(0.95f, 0.95f, 0.95f);
        private static readonly Color Hot = new Color(1f, 0.85f, 0.1f);

        private readonly ScreenManipulationController _controller;
        private readonly WorkspaceStore _store;
        private readonly GameObject _root;
        private readonly LineRenderer _arrowX, _arrowY, _ringX, _ringY, _ringZ, _cube;
        private readonly Vector3[] _ring = new Vector3[RingPoints];
        private readonly Vector3[] _two = new Vector3[2];
        private readonly Vector3[] _square = new Vector3[4];

        public GizmoView(Transform parent, Material unlit, ScreenManipulationController controller, WorkspaceStore store)
        {
            _controller = controller;
            _store = store;
            _root = new GameObject("Gizmo");
            _root.transform.SetParent(parent, false);
            _arrowX = Line("ArrowX", unlit, 2, false);
            _arrowY = Line("ArrowY", unlit, 2, false);
            _ringX = Line("RingX", unlit, RingPoints, true);
            _ringY = Line("RingY", unlit, RingPoints, true);
            _ringZ = Line("RingZ", unlit, RingPoints, true);
            _cube = Line("Scale", unlit, 4, true);
            _root.SetActive(false);
        }

        public void Tick()
        {
            var ws = _store.State;
            if (!_controller.GizmoEnabled || !ws.TryGetScreen(ws.FocusedScreenId, out var screen))
            {
                if (_root.activeSelf) _root.SetActive(false);
                return;
            }
            var preview = _controller.Preview;
            var frame = preview.Active && preview.Screen == screen.Id
                ? ScreenGizmo.Frame(preview.Pose, ws.Surface)
                : ScreenGizmo.Frame(screen, ws.Surface);
            var part = _controller.ActiveGizmo != GizmoPart.None ? _controller.ActiveGizmo : _controller.HoverGizmo;
            float width = LineWidth * frame.Size / 0.156f;
            var o = frame.Origin.ToUnity();
            var right = frame.Right.ToUnity();
            var up = frame.Up.ToUnity();
            var back = frame.Back.ToUnity();

            Segment(_arrowX, o, o + right * frame.Size, width * 1.5f, part == GizmoPart.MoveX ? Hot : RedX);
            Segment(_arrowY, o, o + up * frame.Size, width * 1.5f, part == GizmoPart.MoveY ? Hot : GreenY);
            float r = ScreenGizmo.RingRadius * frame.Size;
            Ring(_ringX, o, up, back, r, width, part == GizmoPart.RotateX ? Hot : RedX);
            Ring(_ringY, o, right, back, r, width, part == GizmoPart.RotateY ? Hot : GreenY);
            Ring(_ringZ, o, right, up, r, width, part == GizmoPart.RotateZ ? Hot : BlueZ);
            float c = ScreenGizmo.ScaleCube * frame.Size * 0.7f;
            _square[0] = o + (-right - up) * c;
            _square[1] = o + (right - up) * c;
            _square[2] = o + (right + up) * c;
            _square[3] = o + (-right + up) * c;
            _cube.SetPositions(_square);
            Style(_cube, width * 2f, part == GizmoPart.Scale ? Hot : Neutral);
            if (!_root.activeSelf) _root.SetActive(true);
        }

        private void Segment(LineRenderer line, Vector3 a, Vector3 b, float width, Color color)
        {
            _two[0] = a;
            _two[1] = b;
            line.SetPositions(_two);
            Style(line, width, color);
        }

        /// <summary>Círculo no plano de (u, v) — perpendicular ao eixo do anel.</summary>
        private void Ring(LineRenderer line, Vector3 center, Vector3 u, Vector3 v, float radius, float width, Color color)
        {
            for (int i = 0; i < RingPoints; i++)
            {
                float a = i * (2f * Mathf.PI / RingPoints);
                _ring[i] = center + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius;
            }
            line.SetPositions(_ring);
            Style(line, width, color);
        }

        private static void Style(LineRenderer line, float width, Color color)
        {
            line.widthMultiplier = width;
            if (line.sharedMaterial != null && line.sharedMaterial.color != color) line.sharedMaterial.color = color;
            line.enabled = true;
        }

        private LineRenderer Line(string name, Material unlit, int points, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = loop;
            line.positionCount = points;
            if (unlit != null) line.sharedMaterial = new Material(unlit);
            return line;
        }

        public void Dispose()
        {
            if (_root == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
            else UnityEngine.Object.DestroyImmediate(_root);
        }
    }
}
