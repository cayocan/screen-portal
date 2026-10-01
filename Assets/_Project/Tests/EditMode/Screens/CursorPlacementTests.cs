using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Cursor;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class CursorPlacementTests
    {
        private static (Workspace ws, VirtualScreen screen) Screen(float curvature)
        {
            var store = new WorkspaceStore();
            store.Dispatch(new CreateScreen(Resolution.Create(2560, 1440), Placement.OnSurface(0f, 0f, 1.3f), 1.28f, curvature, "",
                ScreenSource.Virtual));
            return (store.State, store.State.Screens[0]);
        }

        private static float Dist(Vec3 a, Vec3 b) => (a - b).Length;

        [Test]
        public void PixelAndLocal_RoundTrip()
        {
            var (_, s) = Screen(0f);
            CursorPlacement.PixelToLocal(s, 0f, 0f, out float lx, out float ly);
            Assert.That(lx, Is.EqualTo(-0.64f).Within(1e-4f));
            Assert.That(ly, Is.EqualTo(s.HeightMeters * 0.5f).Within(1e-4f));
            CursorPlacement.PixelToLocal(s, 1280.5f, 720.5f, out lx, out ly);
            CursorPlacement.LocalToPixel(s, lx, ly, out int px, out int py);
            Assert.That((px, py), Is.EqualTo((1280, 720)));
            CursorPlacement.LocalToPixel(s, 5f, -5f, out px, out py);
            Assert.That((px, py), Is.EqualTo((2559, 1439)), "fora da tela: limitado à borda");
        }

        [Test]
        public void FlatScreen_CursorSitsOnThePixel_InFrontOfTheScreen()
        {
            var (ws, s) = Screen(0f);
            var screenPose = ws.Surface.ToPose(s.Placement);
            // forma 32×32 com hotspot no canto: o centro da quad fica 16 px à direita e abaixo do pixel
            Assert.That(CursorPlacement.OnScreen(ws, s.Id, 1280f, 720f, 32, 32, 0, 0, 1, out var pose, out float w, out float h), Is.True);
            float mpp = 1.28f / 2560f;
            Assert.That(w, Is.EqualTo(32 * mpp).Within(1e-6f));
            Assert.That(h, Is.EqualTo(32 * mpp).Within(1e-6f));
            var local = screenPose.Rotation.Inverse * (pose.Position - screenPose.Position);
            Assert.That(local.X, Is.EqualTo(16 * mpp).Within(1e-5f));
            Assert.That(local.Y, Is.EqualTo(-16 * mpp).Within(1e-5f));
            var eye = ws.Surface.Center;
            Assert.That(Dist(pose.Position, eye), Is.LessThan(Dist(screenPose.Position, eye)), "erguido para o lado de quem olha");
        }

        [Test]
        public void ScaleAndHotspot_KeepThePhysicalSize()
        {
            var (ws, s) = Screen(0f);
            CursorPlacement.OnScreen(ws, s.Id, 100f, 100f, 16, 16, 4, 4, 2, out _, out float w, out _);
            Assert.That(w, Is.EqualTo(32 * 1.28f / 2560f).Within(1e-6f));
        }

        [Test]
        public void CurvedScreen_CursorFollowsTheArc_AndFacesTheViewer()
        {
            var (ws, s) = Screen(0.6f);
            var curve = CurvedGeometry.Of(s, ws.Surface);
            var screenPose = ws.Surface.ToPose(s.Placement);
            float lx = 0.5f, ly = 0.1f;
            var pose = CursorPlacement.LocalPose(s, ws.Surface, lx, ly, 0f);
            Assert.That(Dist(pose.Position, CurvedGeometry.SurfacePoint(screenPose, curve.RadiusM, lx, ly)), Is.LessThan(1e-4f));
            // na borda curva, a quad olha para o eixo do cilindro (onde fica quem olha)
            var axis = CurvedGeometry.CylinderCenter(screenPose, curve.RadiusM).Position;
            var toAxis = axis - pose.Position;
            toAxis = new Vec3(toAxis.X, 0f, toAxis.Z) * (1f / new Vec3(toAxis.X, 0f, toAxis.Z).Length);
            var facing = -(pose.Rotation * Vec3.Forward);
            Assert.That(Vec3.Dot(facing, toAxis), Is.GreaterThan(0.999f));
        }

        [Test]
        public void UnknownScreen_IsNotPlaced()
        {
            var (ws, _) = Screen(0f);
            Assert.That(CursorPlacement.OnScreen(ws, new ScreenId(99), 0f, 0f, 32, 32, 0, 0, 1, out _, out _, out _), Is.False);
        }
    }
}
