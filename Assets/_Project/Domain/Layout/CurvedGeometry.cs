using System;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Domain.Layout
{
    /// <summary>
    /// Geometria da tela curva (brief §10.2, spec 2E §3): cilindro de eixo vertical (Y local da tela), curva só na
    /// horizontal. R = d ÷ c', com c' limitada para o arco não passar de 120°. A tela passa pela pose (centro do arco) e o
    /// eixo fica R atrás dela, do lado do usuário. Struct: sem alocação (só <see cref="Label"/> aloca).
    /// </summary>
    public readonly struct CurvedGeometry
    {
        public const float MaxArcDeg = 120f;
        private const float MinDistanceM = 0.01f;
        private const float MinWidthM = 0.001f;

        /// <summary>Pedida (0..1), como está na tela.</summary>
        public readonly float Curvature;
        /// <summary>c' = min(c, 120° × d ÷ largura).</summary>
        public readonly float EffectiveCurvature;
        public readonly float DistanceM;
        public readonly float WidthM;
        /// <summary>0 = plana.</summary>
        public readonly float RadiusM;
        public readonly bool ArcLimited;

        private CurvedGeometry(float curvature, float effective, float distanceM, float widthM, float radiusM, bool arcLimited)
        {
            Curvature = curvature;
            EffectiveCurvature = effective;
            DistanceM = distanceM;
            WidthM = widthM;
            RadiusM = radiusM;
            ArcLimited = arcLimited;
        }

        public bool IsCurved => RadiusM > 0f;
        public float ArcRad => IsCurved ? WidthM / RadiusM : 0f;
        public float ArcDeg => (float)(ArcRad * Angles.RadToDeg);
        public int Percent => (int)Math.Round(EffectiveCurvature * 100.0, MidpointRounding.AwayFromZero);
        /// <summary>R em mm arredondado a 100 (rótulo "≈ NNNNR"); 0 = plana.</summary>
        public int RoundedRadiusMm => IsCurved ? (int)Math.Round(RadiusM * 10.0, MidpointRounding.AwayFromZero) * 100 : 0;

        public static CurvedGeometry Of(float curvature, float distanceM, float widthM)
        {
            float c = ScreenLimits.Clamp01(curvature);
            float d = float.IsNaN(distanceM) ? MinDistanceM : Math.Max(MinDistanceM, distanceM);
            float w = float.IsNaN(widthM) ? MinWidthM : Math.Max(MinWidthM, widthM);
            float max = (float)(MaxArcDeg * Angles.DegToRad) * d / w;
            bool limited = c > max;
            float effective = limited ? max : c;
            return new CurvedGeometry(c, effective, d, w, effective > 0f ? d / effective : 0f, limited);
        }

        /// <summary>d = <see cref="LayoutSurface.CurvatureDistance"/> (raio horizontal no Cilindro, distância na Esfera; solta: do centro).</summary>
        public static CurvedGeometry Of(float curvature, Placement placement, float widthM, LayoutSurface surface) =>
            Of(curvature, surface.CurvatureDistance(placement), widthM);

        public static CurvedGeometry Of(VirtualScreen screen, LayoutSurface surface) =>
            Of(screen.Curvature, screen.Placement, screen.WidthMeters, surface);

        /// <summary>Centro do cilindro = posição da tela − frente da tela × R, mesma rotação (transform da layer cilíndrica).</summary>
        public static Pose CylinderCenter(Pose screenPose, float radiusM) =>
            new Pose(screenPose.Position - (screenPose.Rotation * Vec3.Forward) * radiusM, screenPose.Rotation);

        /// <summary>Ponto da tela nas coordenadas locais (x ao longo do arco, y na altura, metros a partir do centro).</summary>
        public static Vec3 SurfacePoint(Pose screenPose, float radiusM, float localX, float localY)
        {
            if (radiusM <= 0f) return screenPose.Position + screenPose.Rotation * new Vec3(localX, localY, 0f);
            double theta = localX / radiusM;
            var local = new Vec3((float)(radiusM * Math.Sin(theta)), localY, (float)(radiusM * (Math.Cos(theta) - 1.0)));
            return screenPose.Position + screenPose.Rotation * local;
        }

        /// <summary>"Média · 60 % · ≈ 1300R" (+ " · arco limitado"). Aloca: guardar em cache pelo valor.</summary>
        public string Label()
        {
            if (!IsCurved) return CurvaturePresets.Name(CurvaturePresets.Flat);
            int preset = CurvaturePresets.IndexOf(EffectiveCurvature);
            string text = (preset > 0 ? CurvaturePresets.Name(preset) + " · " : string.Empty) + Percent + " % · ≈ " + RoundedRadiusMm + "R";
            return ArcLimited ? text + " · arco limitado" : text;
        }

        public override string ToString() => IsCurved ? $"curva c'={EffectiveCurvature:0.###} R={RadiusM:0.###} m arco={ArcDeg:0.#}°" : "plana";
    }
}
