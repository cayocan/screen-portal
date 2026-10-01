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
        [TestCase(0.2f, 0.29f, HitRegion.Frame)]
        [TestCase(0f, -0.29f, HitRegion.Frame)]
        [TestCase(0f, 0.29f, HitRegion.CurvatureHandle)]
        [TestCase(0.04f, 0.295f, HitRegion.CurvatureHandle)]
        [TestCase(0.5f, 0.28f, HitRegion.Handle)]
        [TestCase(-0.49f, -0.27f, HitRegion.Handle)]
        [TestCase(0.6f, 0f, HitRegion.None)]
        public void Pick_ClassifiesContentFrameAndHandles(float x, float y, HitRegion expected)
        {
            var hit = ScreenHitTest.Pick(OneScreen(), Eye, To(x, y));
            Assert.That(hit.Region, Is.EqualTo(expected));
            if (expected != HitRegion.None) Assert.That(hit.Distance, Is.GreaterThan(1.29f));
        }

        private static Workspace OneCurvedScreen(float curvature)
        {
            var ws = OneScreen();
            new SetScreenCurvature(ws.Screens[0].Id, curvature).Apply(ws, out ws);
            return ws;
        }

        private static Vec3 ToCurved(Workspace ws, float x, float y)
        {
            var screen = ws.Screens[0];
            var pose = ws.Surface.ToPose(screen.Placement);
            var r = CurvedGeometry.Of(screen, ws.Surface).RadiusM;
            return (CurvedGeometry.SurfacePoint(pose, r, x, y) - Eye).Normalized;
        }

        [TestCase(0f, 0f, HitRegion.Content)]
        [TestCase(0.45f, 0.2f, HitRegion.Content)]
        [TestCase(0.515f, 0f, HitRegion.Frame)]
        [TestCase(-0.515f, 0.1f, HitRegion.Frame)]
        [TestCase(0f, 0.29f, HitRegion.CurvatureHandle)]
        [TestCase(0.5f, 0.28f, HitRegion.Handle)]
        [TestCase(-0.49f, -0.27f, HitRegion.Handle)]
        [TestCase(0.6f, 0f, HitRegion.None)]
        public void CurvedScreen_SameLocalCoordinates_SameRegions(float x, float y, HitRegion expected)
        {
            var ws = OneCurvedScreen(0.6f);
            var hit = ScreenHitTest.Pick(ws, Eye, ToCurved(ws, x, y));
            Assert.That(hit.Region, Is.EqualTo(expected));
            if (expected == HitRegion.None) return;
            Assert.That(hit.LocalX, Is.EqualTo(x).Within(1e-3f), "x ao longo do arco");
            Assert.That(hit.LocalY, Is.EqualTo(y).Within(1e-3f));
        }

        [TestCase(22f, HitRegion.Content)] // arco 0,499 m
        [TestCase(23f, HitRegion.Frame)]   // 0,522 m
        [TestCase(24f, HitRegion.None)]    // 0,545 m
        public void Envolvente_FromTheAxis_TheRayAngleIsTheArc(float yawDeg, HitRegion expected)
        {
            // c = 1 a 1,3 m: o eixo do cilindro fica no olho; o ângulo do raio × R = x no arco
            var ws = OneCurvedScreen(1f);
            var hit = ScreenHitTest.Pick(ws, Eye, Quat.AngleAxis(yawDeg, Vec3.Up) * Vec3.Forward);
            Assert.That(hit.Region, Is.EqualTo(expected));
            if (expected != HitRegion.None)
                Assert.That(hit.LocalX, Is.EqualTo(yawDeg * System.Math.PI / 180.0 * 1.3).Within(1e-3));
        }

        [Test]
        public void CurvedScreen_EdgesComeCloser_HitIsOnTheArcNotOnThePlane()
        {
            var ws = OneCurvedScreen(1f);
            var hit = ScreenHitTest.Pick(ws, Eye, ToCurved(ws, 0.45f, 0f));
            Assert.That(hit.Distance, Is.EqualTo(1.3f).Within(1e-3f), "do eixo, todo ponto do arco está a R");
            var flat = ScreenHitTest.Pick(OneScreen(), Eye, (new Vec3(0.45f, 1.2f, 1.3f) - Eye).Normalized);
            Assert.That(flat.Distance, Is.GreaterThan(1.37f));
        }

        [Test]
        public void CurvedScreen_RayPassingBeside_HitsNothing()
        {
            var ws = OneCurvedScreen(0.6f);
            Assert.That(ScreenHitTest.Pick(ws, Eye, Quat.AngleAxis(50f, Vec3.Up) * Vec3.Forward).Region, Is.EqualTo(HitRegion.None));
            Assert.That(ScreenHitTest.Pick(ws, Eye, new Vec3(0f, 0f, -1f)).Region, Is.EqualTo(HitRegion.None));
            Assert.That(ScreenHitTest.Pick(ws, Eye, Vec3.Up).Region, Is.EqualTo(HitRegion.None));
        }

        [Test]
        public void CurvedScreen_FromBehindTheArc_HitsTheNearSide()
        {
            // origem fora do cilindro, atrás da tela, olhando para o usuário: acerta a face de trás (a mais próxima)
            var ws = OneCurvedScreen(0.6f);
            var hit = ScreenHitTest.Pick(ws, new Vec3(0f, 1.2f, 2.5f), new Vec3(0f, 0f, -1f));
            Assert.That(hit.Region, Is.EqualTo(HitRegion.Content));
            Assert.That(hit.Distance, Is.EqualTo(1.2f).Within(1e-3f));
        }

        [Test]
        public void RaycastScreen_Flat_EqualsThePlane()
        {
            var pose = new Pose(new Vec3(0f, 1.2f, 1.3f), Quat.Identity);
            var dir = (new Vec3(0.3f, 1.3f, 1.3f) - Eye).Normalized;
            Assert.That(ScreenHitTest.RaycastScreen(Eye, dir, pose, 0f, out float t, out float lx, out float ly), Is.True);
            Assert.That(ScreenHitTest.RaycastPlane(Eye, dir, pose, out float tp, out float px, out float py), Is.True);
            Assert.That(t, Is.EqualTo(tp));
            Assert.That(lx, Is.EqualTo(px));
            Assert.That(ly, Is.EqualTo(py));
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
