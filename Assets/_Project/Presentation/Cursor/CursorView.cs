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
    /// Desenha o cursor do PC no headset (spec 3B §4): uma quad no eye buffer, por cima da tela, na posição mais recente
    /// do <see cref="CursorSync"/>. Uma textura por forma, criada quando a forma chega (não no quadro). Sem alocação por quadro.
    /// </summary>
    public sealed class CursorView : IDisposable
    {
        private const string ShaderName = "ScreenPortal/CursorSprite"; // Resources/CursorSprite.shader
        private static readonly int ZTestId = Shader.PropertyToID("_ZTest");

        private readonly WorkspaceStore _store;
        private readonly CursorSync _sync;
        private readonly Dictionary<uint, Texture2D> _textures = new Dictionary<uint, Texture2D>();
        private readonly GameObject _quad;
        private readonly Material _material;
        private uint _shownShape;

        public CursorView(Transform parent, WorkspaceStore store, CursorSync sync)
        {
            _store = store;
            _sync = sync;
            _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quad.name = "Cursor";
            _quad.GetComponent<Collider>().enabled = false;
            _quad.transform.SetParent(parent, false);
            var shader = Shader.Find(ShaderName);
            if (shader == null) Debug.LogError($"[CursorView] shader {ShaderName} não encontrado: o cursor não aparece.");
            else
            {
                _material = new Material(shader) { name = "Cursor" };
                _material.SetFloat(ZTestId, (float)UnityEngine.Rendering.CompareFunction.LessEqual);
                _quad.GetComponent<MeshRenderer>().sharedMaterial = _material;
            }
            _quad.SetActive(false);
            _sync.ShapeAdded += OnShapeAdded;
        }

        private void OnShapeAdded(CursorShapeMessage shape)
        {
            if (shape.Width == 0 || shape.Height == 0 || shape.Bgra.Length < shape.Width * shape.Height * 4) return;
            // BGRA de cima para baixo → linhas de baixo para cima (Unity)
            int stride = shape.Width * 4;
            var flipped = new byte[stride * shape.Height];
            for (int y = 0; y < shape.Height; y++)
                Buffer.BlockCopy(shape.Bgra, y * stride, flipped, (shape.Height - 1 - y) * stride, stride);
            var texture = new Texture2D(shape.Width, shape.Height, TextureFormat.BGRA32, false)
            {
                name = "Cursor " + shape.ShapeId.ToString("X8"), filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            };
            texture.LoadRawTextureData(flipped);
            texture.Apply(false, true);
            _textures[shape.ShapeId] = texture;
        }

        public void Tick()
        {
            var s = _sync.Current;
            if (s.Mode != MouseMode.Screen || _material == null || !_textures.TryGetValue(s.ShapeId, out var texture)
                || !_sync.TryGetShape(s.ShapeId, out var shape)
                || !CursorPlacement.OnScreen(_store.State, new ScreenId((int)s.ScreenKey), s.X, s.Y, shape.Width, shape.Height,
                    shape.HotX, shape.HotY, shape.Scale, out var pose, out float w, out float h))
            {
                if (_quad.activeSelf) _quad.SetActive(false);
                return;
            }
            if (_shownShape != s.ShapeId)
            {
                _material.mainTexture = texture;
                _shownShape = s.ShapeId;
            }
            var t = _quad.transform;
            t.SetLocalPositionAndRotation(pose.Position.ToUnity(), pose.Rotation.ToUnity()); // mesmo espaço das telas
            t.localScale = new Vector3(w, h, 1f);
            if (!_quad.activeSelf) _quad.SetActive(true);
        }

        public void Dispose()
        {
            _sync.ShapeAdded -= OnShapeAdded;
            foreach (var texture in _textures.Values) Release(texture);
            Release(_material);
            Release(_quad);
        }

        private static void Release(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
