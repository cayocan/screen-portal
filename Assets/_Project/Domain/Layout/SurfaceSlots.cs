using System;

namespace ScreenPortal.Domain.Layout
{
    /// <summary>Regra de criação (spec superfície §6): posição padrão; se ocupada, o primeiro espaço livre à direita.</summary>
    public static class SurfaceSlots
    {
        public const float GapDeg = 1f;
        private const int MaxIterations = 72;

        public static Placement FirstFree(Workspace workspace, float widthM, float heightM)
        {
            var surface = workspace.Surface;
            var start = Placement.Default;
            var rect = ScreenAngles.Of(start, widthM, heightM, surface);
            float az = start.AzimuthDeg;
            var screens = workspace.Screens;
            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                bool moved = false;
                for (int i = 0; i < screens.Count; i++)
                {
                    if (!ScreenAngles.TryOf(screens[i], surface, out var other)) continue;
                    bool overlapAz = Math.Abs(Placement.NormalizeAzimuth(az - other.AzimuthDeg)) < rect.HalfWidthDeg + other.HalfWidthDeg + GapDeg;
                    bool overlapEl = Math.Abs(start.ElevationDeg - other.ElevationDeg) < rect.HalfHeightDeg + other.HalfHeightDeg + GapDeg;
                    if (!overlapAz || !overlapEl) continue;
                    az = other.AzimuthDeg + other.HalfWidthDeg + GapDeg + rect.HalfWidthDeg + 0.001f;
                    moved = true;
                }
                if (!moved) break;
            }
            return Placement.OnSurface(az, start.ElevationDeg, start.DistanceM);
        }
    }
}
