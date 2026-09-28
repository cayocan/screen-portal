using System;

namespace ScreenPortal.Domain.Spatial
{
    /// <summary>Vetor 3D do domínio (metros, convenção da Unity: +Y para cima, +Z para frente). Sem UnityEngine.</summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);

        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(Vec3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is Vec3 other && Equals(other);
        public override int GetHashCode() => (X, Y, Z).GetHashCode();
        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }
}
