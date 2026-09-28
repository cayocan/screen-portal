using System;

namespace ScreenPortal.Domain.Spatial
{
    /// <summary>Posição + rotação no espaço de tracking (origem no chão, brief: FloorLevel).</summary>
    public readonly struct Pose : IEquatable<Pose>
    {
        public static readonly Pose Identity = new Pose(Vec3.Zero, Quat.Identity);

        public readonly Vec3 Position;
        public readonly Quat Rotation;

        public Pose(Vec3 position, Quat rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public static Pose At(Vec3 position) => new Pose(position, Quat.Identity);

        public bool Equals(Pose other) => Position.Equals(other.Position) && Rotation.Equals(other.Rotation);
        public override bool Equals(object obj) => obj is Pose other && Equals(other);
        public override int GetHashCode() => (Position, Rotation).GetHashCode();
        public override string ToString() => $"Pose{Position}{Rotation}";
    }
}
