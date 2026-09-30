using System;
using System.Collections.Generic;

namespace ScreenPortal.Domain.Layout
{
    public readonly struct SnapResult
    {
        public readonly float AzimuthDeg;
        public readonly float ElevationDeg;
        public readonly bool SnappedAzimuth;
        public readonly bool SnappedElevation;
        /// <summary>Linha-guia (azimute da borda/centro alinhado) — só vale se <see cref="SnappedAzimuth"/>.</summary>
        public readonly float GuideAzimuthDeg;
        public readonly float GuideElevationDeg;

        public SnapResult(float azimuthDeg, float elevationDeg, bool snappedAzimuth, bool snappedElevation, float guideAzimuthDeg, float guideElevationDeg)
        {
            AzimuthDeg = azimuthDeg;
            ElevationDeg = elevationDeg;
            SnappedAzimuth = snappedAzimuth;
            SnappedElevation = snappedElevation;
            GuideAzimuthDeg = guideAzimuthDeg;
            GuideElevationDeg = guideElevationDeg;
        }
    }

    /// <summary>
    /// Encaixe em ângulos (spec superfície §5): bordas, centros e topos das vizinhas, e grade angular. Só age dentro
    /// do limiar — continuar arrastando desgruda sozinho, porque o encaixe é recalculado a partir da posição bruta.
    /// Sem alocação (chamado a cada quadro do arraste).
    /// </summary>
    public static class SurfaceSnapper
    {
        public const float ThresholdDeg = 1.5f;

        public static SnapResult Snap(AngularRect moving, IReadOnlyList<AngularRect> others, SnapOptions options)
        {
            var az = new Axis(moving.AzimuthDeg);
            var el = new Axis(moving.ElevationDeg);
            float mw = moving.HalfWidthDeg;
            float mh = moving.HalfHeightDeg;

            if (options.NeighborSnap && others != null)
            {
                for (int i = 0; i < others.Count; i++)
                {
                    var o = others[i];
                    float ow = o.HalfWidthDeg;
                    float oh = o.HalfHeightDeg;
                    az.Try(o.AzimuthDeg + ow + mw, o.AzimuthDeg + ow); // minha esquerda na direita dela
                    az.Try(o.AzimuthDeg - ow - mw, o.AzimuthDeg - ow); // minha direita na esquerda dela
                    az.Try(o.AzimuthDeg, o.AzimuthDeg);                // centros
                    az.Try(o.AzimuthDeg - ow + mw, o.AzimuthDeg - ow); // esquerdas alinhadas
                    az.Try(o.AzimuthDeg + ow - mw, o.AzimuthDeg + ow); // direitas alinhadas
                    el.Try(o.ElevationDeg, o.ElevationDeg);                // centros
                    el.Try(o.ElevationDeg + oh - mh, o.ElevationDeg + oh); // topos alinhados
                    el.Try(o.ElevationDeg - oh + mh, o.ElevationDeg - oh); // bases alinhadas
                    el.Try(o.ElevationDeg + oh + mh, o.ElevationDeg + oh); // em cima dela
                    el.Try(o.ElevationDeg - oh - mh, o.ElevationDeg - oh); // embaixo dela
                }
            }

            if (options.AngularGrid)
            {
                float step = options.GridStepDeg;
                float gridAz = (float)Math.Round(moving.AzimuthDeg / step) * step;
                float gridEl = (float)Math.Round(moving.ElevationDeg / step) * step;
                az.Try(gridAz, gridAz);
                el.Try(gridEl, gridEl);
            }

            return new SnapResult(az.Value, el.Value, az.Snapped, el.Snapped, az.Guide, el.Guide);
        }

        private struct Axis
        {
            private readonly float _raw;
            private float _bestDelta;
            public float Value;
            public float Guide;
            public bool Snapped;

            public Axis(float raw)
            {
                _raw = raw;
                _bestDelta = ThresholdDeg;
                Value = raw;
                Guide = 0f;
                Snapped = false;
            }

            public void Try(float target, float guide)
            {
                float delta = Math.Abs(target - _raw);
                if (delta > _bestDelta) return;
                _bestDelta = delta;
                Value = target;
                Guide = guide;
                Snapped = true;
            }
        }
    }
}
