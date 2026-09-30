using System;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Domain.Layout
{
    /// <summary>
    /// Regras de criação (spec superfície §6, spec 2C §3): sem ponto preferido, a posição padrão e, se ocupada, o primeiro
    /// espaço livre à direita; com ponto preferido (onde o usuário olha), ele mesmo ou o espaço livre mais perto dele.
    /// </summary>
    public static class SurfaceSlots
    {
        public const float GapDeg = 1f;
        private const int MaxIterations = 72;

        public static Placement FirstFree(Workspace workspace, float widthM, float heightM) =>
            Push(workspace, widthM, heightM, Placement.Default, +1);

        /// <summary>O ponto preferido se estiver livre; senão o espaço livre mais perto (à esquerda ou à direita).</summary>
        public static Placement NearestFree(Workspace workspace, float widthM, float heightM, Placement preferred)
        {
            var right = Push(workspace, widthM, heightM, preferred, +1);
            var left = Push(workspace, widthM, heightM, preferred, -1);
            float dRight = Math.Abs(Placement.NormalizeAzimuth(right.AzimuthDeg - preferred.AzimuthDeg));
            float dLeft = Math.Abs(Placement.NormalizeAzimuth(left.AzimuthDeg - preferred.AzimuthDeg));
            return dLeft < dRight ? left : right;
        }

        /// <summary>O ponto da superfície para onde a cabeça olha, na distância padrão de uma tela nova.</summary>
        public static Placement Facing(LayoutSurface surface, Pose head)
        {
            float distance = Placement.Default.DistanceM;
            var gaze = surface.FromWorldPosition(head.Position + (head.Rotation * Vec3.Forward) * distance);
            return Placement.OnSurface(gaze.AzimuthDeg, gaze.ElevationDeg, distance);
        }

        private static Placement Push(Workspace workspace, float widthM, float heightM, Placement start, int direction)
        {
            var surface = workspace.Surface;
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
                    az = other.AzimuthDeg + direction * (other.HalfWidthDeg + GapDeg + rect.HalfWidthDeg + 0.001f);
                    moved = true;
                }
                if (!moved) break;
            }
            return Placement.OnSurface(az, start.ElevationDeg, start.DistanceM);
        }
    }
}
