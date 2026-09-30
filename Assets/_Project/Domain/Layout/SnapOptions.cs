using System;

namespace ScreenPortal.Domain.Layout
{
    /// <summary>Encaixe (spec superfície §3): nas vizinhas (ligado) e grade angular (desligada, passo 5°).</summary>
    public readonly struct SnapOptions : IEquatable<SnapOptions>
    {
        public const float MinGridStepDeg = 1f;
        public const float MaxGridStepDeg = 30f;
        public static readonly SnapOptions Default = new SnapOptions(true, false, 5f);

        public readonly bool NeighborSnap;
        public readonly bool AngularGrid;
        public readonly float GridStepDeg;

        public SnapOptions(bool neighborSnap, bool angularGrid, float gridStepDeg)
        {
            NeighborSnap = neighborSnap;
            AngularGrid = angularGrid;
            GridStepDeg = Placement.Clamp(gridStepDeg, MinGridStepDeg, MaxGridStepDeg);
        }

        public SnapOptions WithAngularGrid(bool on) => new SnapOptions(NeighborSnap, on, GridStepDeg);

        public bool Equals(SnapOptions other) =>
            NeighborSnap == other.NeighborSnap && AngularGrid == other.AngularGrid && GridStepDeg == other.GridStepDeg;
        public override bool Equals(object obj) => obj is SnapOptions other && Equals(other);
        public override int GetHashCode() => (NeighborSnap, AngularGrid, GridStepDeg).GetHashCode();
    }
}
