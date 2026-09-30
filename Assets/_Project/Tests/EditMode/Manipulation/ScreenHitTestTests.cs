using NUnit.Framework;
using ScreenPortal.App.Manipulation;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class ScreenHitTestTests
    {
        // tela 1 m × 0,5625 m em (0; 1,2; 1,3), sem rotação; raio saindo do centro da superfície
        private static readonly Vec3 Eye = LayoutSurface.DefaultCenter;

        private static Workspace OneScreen()
        {
            new CreateScreen(Resolution.Create(1920, 1080), Placement.OnSurface(0f, 0f, 1.3f), 1f).Apply(Workspace.Empty, out var ws);
            return ws;
        }

        private static Vec3 To(float x, float y) => (new Vec3(x, 1.2f + y, 1.3f) - Eye).Normalized;

        [TestCase(0f, 0f, HitRegion.Content)]
        [TestCase(0.45f, 0.2f, HitRegion.Content)]
        [TestCase(0.515f, 0f, HitRegion.Frame)]
        [TestCase(0f, 0.29f, HitRegion.Frame)]
        [TestCase(0.5f, 0.28f, HitRegion.Handle)]
        [TestCase(-0.49f, -0.27f, HitRegion.Handle)]
        [TestCase(0.6f, 0f, HitRegion.None)]
        public void Pick_ClassifiesContentFrameAndHandles(float x, float y, HitRegion expected)
        {
            var hit = ScreenHitTest.Pick(OneScreen(), Eye, To(x, y));
            Assert.That(hit.Region, Is.EqualTo(expected));
            if (expected != HitRegion.None) Assert.That(hit.Distance, Is.GreaterThan(1.29f));
        }

        [Test]
        public void Pick_ReturnsTheNearestScreen()
        {
            var ws = OneScreen();
            new CreateScreen(Resolution.Create(1920, 1080), Placement.OnSurface(0f, 0f, 0.8f), 1f).Apply(ws, out ws);
            var hit = ScreenHitTest.Pick(ws, Eye, To(0f, 0f));
            Assert.That(hit.Screen, Is.EqualTo(new ScreenId(2)));
        }

        [Test]
        public void RayPointingAway_HitsNothing() =>
            Assert.That(ScreenHitTest.Pick(OneScreen(), Eye, new Vec3(0f, 0f, -1f)).Region, Is.EqualTo(HitRegion.None));

        [Test]
        public void SurfaceRay_FromTheCenter_ReturnsTheRayAngles()
        {
            var sphere = LayoutSurface.Default.WithMode(LayoutMode.Sphere);
            var dir = LayoutSurface.Direction(25f, 10f);
            Assert.That(SurfaceRay.TryIntersect(sphere, 1.5f, Eye, dir, out float az, out float el), Is.True);
            Assert.That(az, Is.EqualTo(25f).Within(0.01f));
            Assert.That(el, Is.EqualTo(10f).Within(0.01f));

            Assert.That(SurfaceRay.TryIntersect(LayoutSurface.Default, 1.5f, Eye, dir, out az, out float height), Is.True);
            Assert.That(az, Is.EqualTo(25f).Within(0.01f));
            Assert.That(height, Is.EqualTo(1.5f * System.Math.Tan(10.0 * System.Math.PI / 180.0)).Within(0.001f));
        }
    }
}
