using System;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Manipulation
{
    /// <summary>
    /// Onde o raio cruza a superfície com um raio dado: esfera de raio r (Esfera) ou cilindro vertical de raio r
    /// (Cilindro). Usa a interseção mais distante (o usuário está dentro). vertical = elevação (°) na Esfera,
    /// altura (m) relativa ao centro no Cilindro.
    /// </summary>
    public static class SurfaceRay
    {
        public static bool TryIntersect(LayoutSurface surface, float radius, Vec3 origin, Vec3 direction, out float azimuthDeg, out float vertical)
        {
            azimuthDeg = 0f;
            vertical = 0f;
            var inverse = surface.FrameRotation.Inverse;
            var o = inverse * (origin - surface.Center);
            var d = (inverse * direction).Normalized;
            Vec3 p;
            if (surface.Mode == LayoutMode.Sphere)
            {
                float b = Vec3.Dot(o, d);
                float c = Vec3.Dot(o, o) - radius * radius;
                float disc = b * b - c;
                if (disc < 0f) return false;
                float t = -b + (float)Math.Sqrt(disc);
                if (t <= 0f) return false;
                p = o + d * t;
                vertical = (float)(Math.Asin(Math.Max(-1.0, Math.Min(1.0, p.Y / radius))) * Angles.RadToDeg);
            }
            else
            {
                float a = d.X * d.X + d.Z * d.Z;
                if (a < 1e-8f) return false;
                float b = o.X * d.X + o.Z * d.Z;
                float c = o.X * o.X + o.Z * o.Z - radius * radius;
                float disc = b * b - a * c;
                if (disc < 0f) return false;
                float t = (-b + (float)Math.Sqrt(disc)) / a;
                if (t <= 0f) return false;
                p = o + d * t;
                vertical = p.Y;
            }
            azimuthDeg = (float)(Math.Atan2(p.X, p.Z) * Angles.RadToDeg);
            return true;
        }
    }
}
