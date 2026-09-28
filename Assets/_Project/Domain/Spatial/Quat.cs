using System;

namespace ScreenPortal.Domain.Spatial
{
    /// <summary>Rotação (quaternion unitário, mesma convenção da Unity). Sem UnityEngine.</summary>
    public readonly struct Quat : IEquatable<Quat>
    {
        public static readonly Quat Identity = new Quat(0f, 0f, 0f, 1f);

        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public readonly float W;

        public Quat(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public bool Equals(Quat other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;
        public override bool Equals(object obj) => obj is Quat other && Equals(other);
        public override int GetHashCode() => (X, Y, Z, W).GetHashCode();
        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###}, {W:0.###})";
    }
}
