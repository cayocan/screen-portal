using System;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Manipulation
{
    public enum HitRegion
    {
        None,
        /// <summary>Meio da tela: reservado aos cliques no Windows (Fase 3); não manipula.</summary>
        Content,
        /// <summary>Faixa fora da borda: mover.</summary>
        Frame,
        /// <summary>Cantos: redimensionar.</summary>
        Handle,
    }

    public readonly struct ScreenHit
    {
        public readonly ScreenId Screen;
        public readonly HitRegion Region;
        public readonly float Distance;
        public readonly Vec3 Point;
        public readonly float LocalX;
        public readonly float LocalY;

        public ScreenHit(ScreenId screen, HitRegion region, float distance, Vec3 point, float localX, float localY)
        {
            Screen = screen;
            Region = region;
            Distance = distance;
            Point = point;
            LocalX = localX;
            LocalY = localY;
        }
    }

    /// <summary>Raio × tela como matemática pura (sem colliders): moldura, alças e menu testáveis sem Unity.</summary>
    public static class ScreenHitTest
    {
        public const float FrameWidthM = 0.03f;
        public const float HandleSizeM = 0.06f;

        /// <summary>Interseção com o plano da pose (normal = +Z local). lx/ly em metros a partir do centro.</summary>
        public static bool RaycastPlane(Vec3 origin, Vec3 direction, Pose pose, out float t, out float localX, out float localY)
        {
            t = 0f;
            localX = 0f;
            localY = 0f;
            var normal = pose.Rotation * Vec3.Forward;
            float denom = Vec3.Dot(direction, normal);
            if (Math.Abs(denom) < 1e-6f) return false;
            t = Vec3.Dot(pose.Position - origin, normal) / denom;
            if (t <= 0f) return false;
            var local = pose.Rotation.Inverse * (origin + direction * t - pose.Position);
            localX = local.X;
            localY = local.Y;
            return true;
        }

        /// <summary>Como <see cref="RaycastPlane"/>, mas só dentro do retângulo (painéis).</summary>
        public static bool Raycast(Vec3 origin, Vec3 direction, Pose pose, float width, float height, out float t, out float localX, out float localY) =>
            RaycastPlane(origin, direction, pose, out t, out localX, out localY)
            && Math.Abs(localX) <= width * 0.5f && Math.Abs(localY) <= height * 0.5f;

        public static HitRegion Classify(float localX, float localY, float width, float height)
        {
            float ax = Math.Abs(localX);
            float ay = Math.Abs(localY);
            float hw = width * 0.5f;
            float hh = height * 0.5f;
            if (ax > hw + FrameWidthM || ay > hh + FrameWidthM) return HitRegion.None;
            if (ax >= hw - HandleSizeM * 0.5f && ay >= hh - HandleSizeM * 0.5f) return HitRegion.Handle;
            if (ax > hw || ay > hh) return HitRegion.Frame;
            return HitRegion.Content;
        }

        /// <summary>A tela mais próxima que o raio acerta (qualquer região ≠ None). Sem alocação.</summary>
        public static ScreenHit Pick(Workspace workspace, Vec3 origin, Vec3 direction)
        {
            var best = default(ScreenHit);
            float bestT = float.MaxValue;
            var surface = workspace.Surface;
            var screens = workspace.Screens;
            for (int i = 0; i < screens.Count; i++)
            {
                var screen = screens[i];
                var pose = surface.ToPose(screen.Placement);
                if (!RaycastPlane(origin, direction, pose, out float t, out float lx, out float ly)) continue;
                var region = Classify(lx, ly, screen.WidthMeters, screen.HeightMeters);
                if (region == HitRegion.None || t >= bestT) continue;
                bestT = t;
                best = new ScreenHit(screen.Id, region, t, origin + direction * t, lx, ly);
            }
            return best;
        }
    }
}
