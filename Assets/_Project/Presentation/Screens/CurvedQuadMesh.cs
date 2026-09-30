using System;
using UnityEngine;

namespace ScreenPortal.Presentation.Screens
{
    /// <summary>
    /// Malha de um arco de cilindro (spec 2E §4) para o furo, o quad de estado, a tela de teste e a moldura: eixo vertical
    /// R atrás do centro, face visível para −Z (como o Quad da Unity). ~1 segmento a cada 3° (mínimo 2); R = 0 → um quad.
    /// Só regenera quando largura, altura ou raio mudam, sem alocar (buffers do tamanho máximo, criados uma vez).
    /// </summary>
    public sealed class CurvedQuadMesh : IDisposable
    {
        public const float DegreesPerSegment = 3f;
        public const int MaxSegments = 64;

        private readonly Vector3[] _vertices = new Vector3[(MaxSegments + 1) * 2];
        private readonly Vector2[] _uv = new Vector2[(MaxSegments + 1) * 2];
        private readonly int[] _triangles = new int[MaxSegments * 6];
        private float _width = -1f;
        private float _height = -1f;
        private float _radius = -1f;

        public Mesh Mesh { get; }
        public int Segments { get; private set; }

        public CurvedQuadMesh(string name)
        {
            Mesh = new Mesh { name = name };
            Mesh.MarkDynamic();
        }

        public static int SegmentsFor(float widthM, float radiusM)
        {
            if (radiusM <= 0f) return 1;
            float arcDeg = widthM / radiusM * Mathf.Rad2Deg;
            return Mathf.Clamp(Mathf.CeilToInt(arcDeg / DegreesPerSegment), 2, MaxSegments);
        }

        /// <returns>true se regenerou.</returns>
        public bool Set(float widthM, float heightM, float radiusM)
        {
            if (widthM == _width && heightM == _height && radiusM == _radius) return false;
            _width = widthM;
            _height = heightM;
            _radius = radiusM;
            int n = SegmentsFor(widthM, radiusM);
            Segments = n;
            float hh = heightM * 0.5f;
            for (int i = 0; i <= n; i++)
            {
                float u = (float)i / n;
                float x = (u - 0.5f) * widthM;
                float px = x;
                float pz = 0f;
                if (radiusM > 0f)
                {
                    float theta = x / radiusM;
                    px = radiusM * Mathf.Sin(theta);
                    pz = radiusM * (Mathf.Cos(theta) - 1f);
                }
                _vertices[2 * i] = new Vector3(px, -hh, pz);
                _vertices[2 * i + 1] = new Vector3(px, hh, pz);
                _uv[2 * i] = new Vector2(u, 0f);
                _uv[2 * i + 1] = new Vector2(u, 1f);
            }
            for (int i = 0; i < n; i++)
            {
                int bl = 2 * i;
                int k = i * 6;
                _triangles[k] = bl;         // mesma ordem do Quad da Unity: (bl, tr, br), (tr, bl, tl)
                _triangles[k + 1] = bl + 3;
                _triangles[k + 2] = bl + 2;
                _triangles[k + 3] = bl + 3;
                _triangles[k + 4] = bl;
                _triangles[k + 5] = bl + 1;
            }
            int vertexCount = (n + 1) * 2;
            Mesh.Clear();
            Mesh.SetVertices(_vertices, 0, vertexCount);
            Mesh.SetUVs(0, _uv, 0, vertexCount);
            Mesh.SetTriangles(_triangles, 0, n * 6, 0);
            return true;
        }

        public void Dispose()
        {
            if (Mesh == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(Mesh);
            else UnityEngine.Object.DestroyImmediate(Mesh);
        }
    }
}
