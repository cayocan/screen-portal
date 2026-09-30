using System;
using NUnit.Framework;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class SpatialMathTests
    {
        internal static void AssertNear(Vec3 actual, Vec3 expected, float tolerance = 1e-4f, string message = "")
        {
            Assert.That((actual - expected).Length, Is.LessThanOrEqualTo(tolerance), $"{message} esperado {expected}, veio {actual}");
        }

        internal static float AngleDeg(Vec3 a, Vec3 b)
        {
            double cos = Vec3.Dot(a.Normalized, b.Normalized);
            return (float)(Math.Acos(Math.Max(-1.0, Math.Min(1.0, cos))) * 180.0 / Math.PI);
        }

        [Test]
        public void AngleAxis_YawPositive_TurnsForwardToTheRight() =>
            AssertNear(Quat.AngleAxis(90f, Vec3.Up) * Vec3.Forward, Vec3.Right);

        [Test]
        public void AngleAxis_PitchPositive_TurnsForwardDown() =>
            AssertNear(Quat.AngleAxis(90f, Vec3.Right) * Vec3.Forward, new Vec3(0f, -1f, 0f));

        [Test]
        public void Multiply_ComposesRotations()
        {
            var q = Quat.AngleAxis(30f, Vec3.Up) * Quat.AngleAxis(60f, Vec3.Up);
            AssertNear(q * Vec3.Forward, Vec3.Right);
        }

        [Test]
        public void Inverse_UndoesRotation()
        {
            var q = Quat.AngleAxis(37f, new Vec3(1f, 2f, 3f));
            var v = new Vec3(0.3f, -1.2f, 2f);
            AssertNear(q.Inverse * (q * v), v);
        }

        [Test]
        public void LookRotation_PointsForwardAndKeepsUp()
        {
            var dir = new Vec3(1f, 0.5f, 2f).Normalized;
            var q = Quat.LookRotation(dir, Vec3.Up);
            AssertNear(q * Vec3.Forward, dir);
            Assert.That(Vec3.Dot(q * Vec3.Right, Vec3.Up), Is.EqualTo(0f).Within(1e-4f), "sem roll");
            var yaw = Quat.LookRotation(Vec3.Right, Vec3.Up);
            var expected = Quat.AngleAxis(90f, Vec3.Up);
            Assert.That(Math.Abs(yaw.Y), Is.EqualTo(Math.Abs(expected.Y)).Within(1e-4f));
            Assert.That(Math.Abs(yaw.W), Is.EqualTo(Math.Abs(expected.W)).Within(1e-4f));
        }

        [Test]
        public void Vec3_CrossFollowsUnityConvention() =>
            AssertNear(Vec3.Cross(Vec3.Up, Vec3.Forward), Vec3.Right);
    }
}
