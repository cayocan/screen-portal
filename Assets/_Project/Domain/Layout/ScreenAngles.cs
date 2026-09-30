using System;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Layout
{
    public static class ScreenAngles
    {
        public static AngularRect Of(Placement placement, float widthM, float heightM, LayoutSurface surface)
        {
            float horizontal = Math.Max(0.01f, surface.CurvatureDistance(placement));
            float distance = Math.Max(0.01f, placement.DistanceM);
            float halfWidth = (float)(Math.Atan(widthM * 0.5f / horizontal) * Angles.RadToDeg);
            float halfHeight = (float)(Math.Atan(heightM * 0.5f / distance) * Angles.RadToDeg);
            return new AngularRect(placement.AzimuthDeg, placement.ElevationDeg, halfWidth, halfHeight);
        }

        /// <summary>false para telas soltas (não participam do encaixe nem ocupam espaço na superfície).</summary>
        public static bool TryOf(VirtualScreen screen, LayoutSurface surface, out AngularRect rect)
        {
            if (screen.Placement.IsLoose)
            {
                rect = default;
                return false;
            }
            rect = Of(screen.Placement, screen.WidthMeters, screen.HeightMeters, surface);
            return true;
        }
    }
}
