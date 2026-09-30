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

        public Quat Inverse => new Quat(-X, -Y, -Z, W);

        /// <summary>Produto de Hamilton, como o Quaternion da Unity: (a * b) aplica b e depois a.</summary>
        public static Quat operator *(Quat a, Quat b) => new Quat(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        public static Vec3 operator *(Quat q, Vec3 v)
        {
            var u = new Vec3(q.X, q.Y, q.Z);
            var t = Vec3.Cross(u, v) * 2f;
            return v + t * q.W + Vec3.Cross(u, t);
        }

        /// <summary>Como Quaternion.AngleAxis: yaw positivo gira +Z para +X; pitch positivo gira +Z para baixo.</summary>
        public static Quat AngleAxis(float degrees, Vec3 axis)
        {
            var n = axis.Normalized;
            double half = degrees * Math.PI / 360.0;
            float s = (float)Math.Sin(half);
            return new Quat(n.X * s, n.Y * s, n.Z * s, (float)Math.Cos(half));
        }

        /// <summary>Como Quaternion.LookRotation: +Z local = forward, +Y local o mais próximo possível de up.</summary>
        public static Quat LookRotation(Vec3 forward, Vec3 up)
        {
            var f = forward.Normalized;
            var r = Vec3.Cross(up, f).Normalized;
            if (r.Length < 0.5f) r = Vec3.Cross(Vec3.Forward, f).Normalized; // forward paralelo a up
            if (r.Length < 0.5f) r = Vec3.Right;
            var u = Vec3.Cross(f, r);
            float m00 = r.X, m01 = u.X, m02 = f.X;
            float m10 = r.Y, m11 = u.Y, m12 = f.Y;
            float m20 = r.Z, m21 = u.Z, m22 = f.Z;
            float trace = m00 + m11 + m22;
            if (trace > 0f)
            {
                float s = (float)Math.Sqrt(trace + 1f) * 2f;
                return new Quat((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s);
            }
            if (m00 > m11 && m00 > m22)
            {
                float s = (float)Math.Sqrt(1f + m00 - m11 - m22) * 2f;
                return new Quat(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
            }
            if (m11 > m22)
            {
                float s = (float)Math.Sqrt(1f + m11 - m00 - m22) * 2f;
                return new Quat((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s);
            }
            float s2 = (float)Math.Sqrt(1f + m22 - m00 - m11) * 2f;
            return new Quat((m02 + m20) / s2, (m12 + m21) / s2, 0.25f * s2, (m10 - m01) / s2);
        }

        public bool Equals(Quat other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;
        public override bool Equals(object obj) => obj is Quat other && Equals(other);
        public override int GetHashCode() => (X, Y, Z, W).GetHashCode();
        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###}, {W:0.###})";
    }
}
