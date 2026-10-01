using System;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Layout;
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
        /// <summary>Meio do topo da moldura: curvar (spec 2E §3).</summary>
        CurvatureHandle,
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
        /// <summary>Largura da alça de curvatura, na faixa de cima da moldura (não invade o conteúdo).</summary>
        public const float CurvatureHandleWidthM = 0.1f;

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

        /// <summary>
        /// Interseção com o cilindro da tela curva (eixo = Y local, R atrás da pose). Tenta a interseção mais próxima e depois
        /// a mais distante: de dentro (o normal) vale a face interna; de fora, a face mais próxima. lx = comprimento do arco
        /// a partir do centro, ly = altura: as mesmas coordenadas da tela plana. Só aceita |lx| ≤ <paramref name="maxHalfX"/>.
        /// </summary>
        public static bool RaycastCylinder(Vec3 origin, Vec3 direction, Pose pose, float radius, float maxHalfX,
            out float t, out float localX, out float localY)
        {
            t = 0f;
            localX = 0f;
            localY = 0f;
            var inverse = pose.Rotation.Inverse;
            var o = inverse * (origin - pose.Position);
            var d = inverse * direction;
            float ox = o.X;
            float oz = o.Z + radius; // relativo ao eixo
            float a = d.X * d.X + d.Z * d.Z;
            if (a < 1e-8f) return false;
            float b = ox * d.X + oz * d.Z;
            float c = ox * ox + oz * oz - radius * radius;
            float disc = b * b - a * c;
            if (disc < 0f) return false;
            float sq = (float)Math.Sqrt(disc);
            for (int k = 0; k < 2; k++)
            {
                float root = (-b + (k == 0 ? -sq : sq)) / a;
                if (root <= 0f) continue;
                float px = ox + d.X * root;
                float pz = oz + d.Z * root;
                if (pz <= 0f) continue; // metade de trás do cilindro: nunca é tela (arco ≤ 120°)
                float x = (float)(Math.Atan2(px, pz) * radius);
                if (Math.Abs(x) > maxHalfX) continue;
                t = root;
                localX = x;
                localY = o.Y + d.Y * root;
                return true;
            }
            return false;
        }

        /// <summary>Plano (R = 0) ou cilindro, sem limite de x: arrastes que saem da tela (redimensionar).</summary>
        public static bool RaycastScreen(Vec3 origin, Vec3 direction, Pose pose, float radiusM, out float t, out float localX, out float localY) =>
            radiusM > 0f
                ? RaycastCylinder(origin, direction, pose, radiusM, float.MaxValue, out t, out localX, out localY)
                : RaycastPlane(origin, direction, pose, out t, out localX, out localY);

        public static HitRegion Classify(float localX, float localY, float width, float height)
        {
            float ax = Math.Abs(localX);
            float ay = Math.Abs(localY);
            float hw = width * 0.5f;
            float hh = height * 0.5f;
            if (ax > hw + FrameWidthM || ay > hh + FrameWidthM) return HitRegion.None;
            if (ax >= hw - HandleSizeM * 0.5f && ay >= hh - HandleSizeM * 0.5f) return HitRegion.Handle;
            if (localY > hh && ax <= CurvatureHandleWidthM * 0.5f) return HitRegion.CurvatureHandle;
            if (ax > hw || ay > hh) return HitRegion.Frame;
            return HitRegion.Content;
        }

        /// <summary>A tela mais próxima que o raio acerta (qualquer região ≠ None); tela curva pelo cilindro. Sem alocação.</summary>
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
                var curve = CurvedGeometry.Of(screen, surface);
                float t, lx, ly;
                bool hit = curve.IsCurved
                    ? RaycastCylinder(origin, direction, pose, curve.RadiusM, screen.WidthMeters * 0.5f + FrameWidthM, out t, out lx, out ly)
                    : RaycastPlane(origin, direction, pose, out t, out lx, out ly);
                if (!hit) continue;
                var region = Classify(lx, ly, screen.WidthMeters, screen.HeightMeters);
                if (region == HitRegion.None || t >= bestT) continue;
                bestT = t;
                best = new ScreenHit(screen.Id, region, t, origin + direction * t, lx, ly);
            }
            return best;
        }
    }
}
