using System;
using System.Collections.Generic;
using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Cursor;
using ScreenPortal.Domain.Screens;
using UnityEngine;

namespace ScreenPortal.Presentation.Cursor
{
    /// <summary>
    /// Desenha o cursor do PC no headset (spec 3B §4) como **compositor layer** (OVROverlay quad, tipo Overlay): o Quest a
    /// amostra na resolução do painel, nítida como as telas (no eye buffer ficava borrada). A forma ocupa o canto superior
    /// esquerdo de uma tela de <see cref="Canvas"/>² pixels (a layer nunca é recriada; só o conteúdo muda quando a forma
    /// muda). Mostrar/esconder pelo <c>hidden</c>. Sem alocação por quadro.
    /// </summary>
    public sealed class CursorView : IDisposable
    {
        /// <summary>Maior forma que o host manda (≤ 60 KB → ≤ 122 px de lado).</summary>
        public const int Canvas = 128;

        private readonly WorkspaceStore _store;
        private readonly CursorSync _sync;
        private readonly Dictionary<uint, byte[]> _canvases = new Dictionary<uint, byte[]>();
        private readonly GameObject _root;
        private readonly OVROverlay _overlay;
        private readonly Texture2D _texture;
        private uint _shownShape;

        public CursorView(Transform parent, WorkspaceStore store, CursorSync sync)
        {
            _store = store;
            _sync = sync;
            _root = new GameObject("Cursor");
            _root.transform.SetParent(parent, false);
            _texture = new Texture2D(Canvas, Canvas, TextureFormat.BGRA32, false)
            {
                name = "Cursor", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            };
            _texture.SetPixelData(new byte[Canvas * Canvas * 4], 0);
            _texture.Apply(false, false);
            _overlay = _root.AddComponent<OVROverlay>();
            _overlay.currentOverlayType = OVROverlay.OverlayType.Overlay; // por cima de tudo: o cursor nunca some
            _overlay.currentOverlayShape = OVROverlay.OverlayShape.Quad;
            _overlay.textures = new Texture[] { _texture, _texture };
            _overlay.isDynamic = true; // a forma muda: a layer copia a textura (128², barato)
            _overlay.isAlphaPremultiplied = false;
            _overlay.useExpensiveSuperSample = true; // forma pequena: sem serrilhado nem cintilação
            _overlay.hidden = true;
            _sync.ShapeAdded += OnShapeAdded;
        }

        private void OnShapeAdded(CursorShapeMessage shape)
        {
            if (shape.Width == 0 || shape.Height == 0 || shape.Width > Canvas || shape.Height > Canvas
                || shape.Bgra.Length < shape.Width * shape.Height * 4) return;
            // canto superior esquerdo da tela; linhas de baixo para cima (Unity)
            var canvas = new byte[Canvas * Canvas * 4];
            int stride = shape.Width * 4;
            for (int y = 0; y < shape.Height; y++)
                Buffer.BlockCopy(shape.Bgra, y * stride, canvas, (Canvas - 1 - y) * Canvas * 4, stride);
            _canvases[shape.ShapeId] = canvas;
        }

        public void Tick()
        {
            var s = _sync.Current;
            if (s.Mode != MouseMode.Screen || !_canvases.TryGetValue(s.ShapeId, out var canvas) || !_sync.TryGetShape(s.ShapeId, out var shape)
                || !CursorPlacement.OnScreen(_store.State, new ScreenId((int)s.ScreenKey), s.X, s.Y, Canvas, Canvas,
                    shape.HotX, shape.HotY, shape.Scale, out var pose, out float w, out float h))
            {
                _overlay.hidden = true;
                return;
            }
            if (_shownShape != s.ShapeId)
            {
                _texture.SetPixelData(canvas, 0);
                _texture.Apply(false, false);
                _shownShape = s.ShapeId;
            }
            var t = _root.transform;
            t.SetLocalPositionAndRotation(pose.Position.ToUnity(), pose.Rotation.ToUnity()); // mesmo espaço das telas
            t.localScale = new Vector3(w, h, 1f);
            _overlay.hidden = false;
        }

        public void Dispose()
        {
            _sync.ShapeAdded -= OnShapeAdded;
            Release(_root);
            Release(_texture);
        }

        private static void Release(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
