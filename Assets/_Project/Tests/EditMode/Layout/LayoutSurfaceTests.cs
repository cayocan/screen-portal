using NUnit.Framework;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Spatial;
using static ScreenPortal.Tests.SpatialMathTests;

namespace ScreenPortal.Tests
{
    public class LayoutSurfaceTests
    {
        private static readonly Vec3 Center = new Vec3(0.2f, 1.1f, -0.3f);

        private static LayoutSurface Surface(LayoutMode mode, float heading = 25f, float tilt = 0f) =>
            new LayoutSurface(mode, Center, heading, tilt, SnapOptions.Default);

        [Test]
        public void Cylinder_ScreensStayUpright_ForAnyPlacement()
        {
            var s = Surface(LayoutMode.Cylinder, tilt: 30f); // tilt é ignorado no Cilindro
            for (float az = -150f; az <= 150f; az += 30f)
            for (float el = -60f; el <= 75f; el += 15f)
            foreach (float d in new[] { 0.5f, 1.3f, 4f })
            {
                var pose = s.ToPose(Placement.OnSurface(az, el, d));
                AssertNear(pose.Rotation * Vec3.Up, Vec3.Up, 1e-4f, "roll/pitch");
                var toScreen = pose.Position - Center;
                var flat = new Vec3(toScreen.X, 0f, toScreen.Z);
                Assert.That(AngleDeg(pose.Rotation * Vec3.Forward, flat), Is.LessThanOrEqualTo(0.01f), "olha para o eixo");
            }
        }

        [Test]
        public void Sphere_ScreenFacesTheCenter_WithAndWithoutTilt()
        {
            foreach (float tilt in new[] { 0f, 35f, 60f })
            {
                var s = Surface(LayoutMode.Sphere, tilt: tilt);
                for (float az = -150f; az <= 150f; az += 50f)
                for (float el = -60f; el <= 75f; el += 45f)
                {
                    var pose = s.ToPose(Placement.OnSurface(az, el, 1.5f));
                    Assert.That(AngleDeg(pose.Rotation * Vec3.Forward, pose.Position - Center), Is.LessThanOrEqualTo(0.01f),
                        $"tilt {tilt} az {az} el {el}");
                }
            }
        }

        [Test]
        public void CylinderAndSphere_PlaceTheScreenAtTheSamePoint_WithoutTilt()
        {
            var p = Placement.OnSurface(40f, 20f, 2f);
            AssertNear(Surface(LayoutMode.Cylinder).ToPose(p).Position, Surface(LayoutMode.Sphere).ToPose(p).Position, 1e-4f);
        }

        [Test]
        public void DefaultPlacement_IsInFrontAndSlightlyBelow()
        {
            var pose = LayoutSurface.Default.ToPose(Placement.Default);
            Assert.That(pose.Position.Z, Is.GreaterThan(1.2f));
            Assert.That(pose.Position.Y, Is.LessThan(1.2f));
            Assert.That(pose.Position.X, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void FromWorldPosition_RoundTrips()
        {
            foreach (var mode in new[] { LayoutMode.Cylinder, LayoutMode.Sphere })
            {
                var s = Surface(mode, tilt: 20f);
                var p = Placement.OnSurface(-35f, 12f, 1.7f);
                var back = s.FromWorldPosition(s.ToPose(p).Position);
                Assert.That(back.AzimuthDeg, Is.EqualTo(-35f).Within(0.01f));
                Assert.That(back.ElevationDeg, Is.EqualTo(12f).Within(0.01f));
                Assert.That(back.DistanceM, Is.EqualTo(1.7f).Within(0.001f));
            }
        }

        [Test]
        public void CurvatureDistance_IsHorizontalRadiusOnCylinder_AndDistanceOnSphere()
        {
            var p = Placement.OnSurface(0f, 60f, 2f);
            Assert.That(Surface(LayoutMode.Cylinder).CurvatureDistance(p), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(Surface(LayoutMode.Sphere).CurvatureDistance(p), Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void Limits_AreClamped()
        {
            var high = Placement.OnSurface(190f, 90f, 10f);
            Assert.That(high.ElevationDeg, Is.EqualTo(75f));
            Assert.That(high.DistanceM, Is.EqualTo(5f));
            Assert.That(high.AzimuthDeg, Is.EqualTo(-170f).Within(1e-4f));
            var low = Placement.OnSurface(0f, -80f, 0.1f);
            Assert.That(low.ElevationDeg, Is.EqualTo(-60f));
            Assert.That(low.DistanceM, Is.EqualTo(0.4f));
            Assert.That(Surface(LayoutMode.Sphere, tilt: 80f).TiltDeg, Is.EqualTo(60f));
            Assert.That(Surface(LayoutMode.Sphere, tilt: -5f).TiltDeg, Is.EqualTo(0f));
            Assert.That(new SnapOptions(true, true, 0f).GridStepDeg, Is.EqualTo(1f));
            Assert.That(new SnapOptions(true, true, 50f).GridStepDeg, Is.EqualTo(30f));
        }

        [Test]
        public void CylinderMath_MoveVertically_KeepsHorizontalRadius()
        {
            var p = Placement.OnSurface(10f, -5f, 1.3f);
            float radius = CylinderMath.HorizontalRadius(p);
            var moved = CylinderMath.MoveVertically(p, 0.6f);
            Assert.That(CylinderMath.HorizontalRadius(moved), Is.EqualTo(radius).Within(0.001f));
            Assert.That(CylinderMath.Height(moved), Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(moved.AzimuthDeg, Is.EqualTo(10f).Within(1e-4f));
        }

        [Test]
        public void CylinderMath_WithHorizontalRadius_KeepsHeight()
        {
            var p = Placement.OnSurface(0f, 20f, 1.5f);
            float height = CylinderMath.Height(p);
            var farther = CylinderMath.WithHorizontalRadius(p, 2.5f);
            Assert.That(CylinderMath.Height(farther), Is.EqualTo(height).Within(0.001f));
            Assert.That(CylinderMath.HorizontalRadius(farther), Is.EqualTo(2.5f).Within(0.001f));
        }

        [Test]
        public void HeadingAndPitchOf_ReadTheHeadRotation()
        {
            var head = Quat.AngleAxis(30f, Vec3.Up) * Quat.AngleAxis(-20f, Vec3.Right);
            Assert.That(LayoutSurface.HeadingOf(head), Is.EqualTo(30f).Within(0.01f));
            Assert.That(LayoutSurface.PitchUpOf(head), Is.EqualTo(20f).Within(0.01f));
        }
    }
}
