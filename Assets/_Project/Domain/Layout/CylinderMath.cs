using System;

namespace ScreenPortal.Domain.Layout
{
    /// <summary>No Cilindro a tela sobe reta (mantém o raio horizontal) e o afastar mexe no raio (mantém a altura).</summary>
    public static class CylinderMath
    {
        public static float HorizontalRadius(Placement p) => p.DistanceM * (float)Math.Cos(p.ElevationDeg * Angles.DegToRad);
        public static float Height(Placement p) => p.DistanceM * (float)Math.Sin(p.ElevationDeg * Angles.DegToRad);

        public static Placement FromAzimuthHeight(float azimuthDeg, float height, float radius)
        {
            double el = Math.Atan2(height, radius) * Angles.RadToDeg;
            float d = (float)Math.Sqrt(radius * radius + height * height);
            return Placement.OnSurface(azimuthDeg, (float)el, d);
        }

        public static Placement FromAzimuthElevationAtRadius(float azimuthDeg, float elevationDeg, float radius)
        {
            float el = Placement.ClampElevation(elevationDeg);
            return Placement.OnSurface(azimuthDeg, el, radius / (float)Math.Cos(el * Angles.DegToRad));
        }

        public static Placement MoveVertically(Placement p, float height) => FromAzimuthHeight(p.AzimuthDeg, height, HorizontalRadius(p));
        public static Placement WithHorizontalRadius(Placement p, float radius) => FromAzimuthHeight(p.AzimuthDeg, Height(p), radius);
    }
}
