using System;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Cursor
{
    /// <summary>
    /// Pixel de uma tela ↔ coordenadas locais (metros a partir do centro; x ao longo do arco na tela curva) e a pose do
    /// cursor desenhado no headset (spec 3B §4): a quad fica <see cref="LiftM"/> à frente da tela, de frente para quem
    /// olha, com o hotspot no pixel. Tamanho = pixels da forma × metros por pixel da tela. Puro, sem alocação.
    /// </summary>
    public static class CursorPlacement
    {
        public const float LiftM = 0.002f;

        public static float MetersPerPixel(VirtualScreen screen) => screen.WidthMeters / screen.Resolution.Width;

        public static void PixelToLocal(VirtualScreen screen, float px, float py, out float localX, out float localY)
        {
            float mpp = MetersPerPixel(screen);
            localX = px * mpp - screen.WidthMeters * 0.5f;
            localY = screen.HeightMeters * 0.5f - py * mpp;
        }

        public static void LocalToPixel(VirtualScreen screen, float localX, float localY, out int px, out int py)
        {
            float mpp = MetersPerPixel(screen);
            px = Clamp((int)Math.Floor((localX + screen.WidthMeters * 0.5f) / mpp), 0, screen.Resolution.Width - 1);
            py = Clamp((int)Math.Floor((screen.HeightMeters * 0.5f - localY) / mpp), 0, screen.Resolution.Height - 1);
        }

        /// <summary>Pose do ponto local da tela (plana ou curva), virada para quem olha, erguida <paramref name="lift"/>.</summary>
        public static Pose LocalPose(VirtualScreen screen, LayoutSurface surface, float localX, float localY, float lift)
        {
            var pose = surface.ToPose(screen.Placement);
            var curve = CurvedGeometry.Of(screen, surface);
            var rotation = curve.IsCurved
                ? pose.Rotation * Quat.AngleAxis((float)(localX / curve.RadiusM * Angles.RadToDeg), Vec3.Up)
                : pose.Rotation;
            var point = CurvedGeometry.SurfacePoint(pose, curve.IsCurved ? curve.RadiusM : 0f, localX, localY);
            // +Z local aponta para longe de quem olha (o centro do cilindro fica atrás de −Z): erguer = ir para −Z.
            return new Pose(point - (rotation * Vec3.Forward) * lift, rotation);
        }

        /// <summary>
        /// Pose do centro da quad do cursor e o tamanho dela, com o hotspot (em pixels do bitmap; × <paramref name="scale"/>
        /// pixels da tela) no pixel (<paramref name="px"/>, <paramref name="py"/>).
        /// </summary>
        public static bool OnScreen(Workspace ws, ScreenId id, float px, float py, int shapeWidth, int shapeHeight, int hotX, int hotY,
            int scale, out Pose pose, out float widthM, out float heightM)
        {
            pose = default;
            widthM = heightM = 0f;
            if (!ws.TryGetScreen(id, out var screen)) return false;
            float mpp = MetersPerPixel(screen);
            widthM = shapeWidth * scale * mpp;
            heightM = shapeHeight * scale * mpp;
            PixelToLocal(screen, px, py, out float lx, out float ly);
            float centerX = lx + (shapeWidth * 0.5f - hotX) * scale * mpp;
            float centerY = ly - (shapeHeight * 0.5f - hotY) * scale * mpp;
            pose = LocalPose(screen, ws.Surface, centerX, centerY, LiftM);
            return true;
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
    }
}
