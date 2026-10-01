using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Manipulation;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class ScreenGizmoTests
    {
        private WorkspaceStore _store;
        private CommandBus _bus;
        private ScreenManipulationController _controller;
        private Vec3 _eye;

        [SetUp]
        public void SetUp()
        {
            _store = new WorkspaceStore();
            _bus = new CommandBus(_store, new ICommandObserver[0]);
            _bus.Send(new CreateScreen(Resolution.Create(2560, 1440), Placement.OnSurface(0f, 0f, 1.3f), 1.28f, 0f, "", ScreenSource.Virtual));
            _bus.Send(new SetFocus(_store.State.Screens[0].Id));
            _controller = new ScreenManipulationController(_store, _bus) { GizmoEnabled = true };
            _eye = _store.State.Surface.Center;
        }

        private VirtualScreen Screen => _store.State.Screens[0];
        private GizmoFrame Frame => ScreenGizmo.Frame(Screen, _store.State.Surface);

        private ManipulationInput At(Vec3 target, bool select, bool noSnap = false) =>
            new ManipulationInput { HasRay = true, Origin = _eye, Direction = (target - _eye).Normalized, SelectHeld = select, NoSnap = noSnap };

        private void Step(Vec3 target, bool select, bool noSnap = false)
        {
            var input = At(target, select, noSnap);
            var none = default(ManipulationInput);
            _controller.Update(in input, in none);
        }

        [Test]
        public void Pick_FindsEachPart()
        {
            var f = Frame;
            Assert.That(ScreenGizmo.Pick(in f, _eye, (f.Origin - _eye).Normalized), Is.EqualTo(GizmoPart.Scale));
            Assert.That(ScreenGizmo.Pick(in f, _eye, (f.Origin + f.Right * (0.6f * f.Size) - _eye).Normalized), Is.EqualTo(GizmoPart.MoveX));
            Assert.That(ScreenGizmo.Pick(in f, _eye, (f.Origin + f.Up * (0.6f * f.Size) - _eye).Normalized), Is.EqualTo(GizmoPart.MoveY));
            var ringPoint = f.Origin + (f.Right * -0.7071f + f.Up * 0.7071f) * (ScreenGizmo.RingRadius * f.Size);
            Assert.That(ScreenGizmo.Pick(in f, _eye, (ringPoint - _eye).Normalized), Is.EqualTo(GizmoPart.RotateZ));
            Assert.That(ScreenGizmo.Pick(in f, _eye, (f.Origin + f.Right * (3f * f.Size) - _eye).Normalized), Is.EqualTo(GizmoPart.None));
        }

        [Test]
        public void HoveringTheGizmo_DoesNotGrabTheScreenBehind()
        {
            var f = Frame;
            Step(f.Origin + f.Right * (0.6f * f.Size), false);
            Assert.That(_controller.HoverGizmo, Is.EqualTo(GizmoPart.MoveX));
            Assert.That(_controller.HoverHit.Region, Is.EqualTo(HitRegion.None));
        }

        [Test]
        public void MoveArrow_SlidesAlongTheSurface()
        {
            var f = Frame;
            var grab = f.Origin + f.Right * (0.6f * f.Size);
            Step(grab, false);
            Step(grab, true);
            Assert.That(_controller.ActiveGizmo, Is.EqualTo(GizmoPart.MoveX));
            Step(grab + f.Right * 0.2f, true);
            Step(grab + f.Right * 0.2f, false);
            Assert.That(Screen.Placement.IsLoose, Is.False, "continua na superfície");
            Assert.That(Screen.Placement.AzimuthDeg, Is.GreaterThan(5f));
            Assert.That(Screen.Placement.ElevationDeg, Is.EqualTo(0f).Within(0.5f), "seta X não mexe na altura");
        }

        [Test]
        public void RotateRing_FreesTheScreen_InSnappedSteps()
        {
            var f = Frame;
            float r = ScreenGizmo.RingRadius * f.Size;
            var start = Ring(f, r, 45f);
            var end = Ring(f, r, 60f); // +15° em volta do eixo para quem olha
            Step(start, false);
            Step(start, true);
            Assert.That(_controller.ActiveGizmo, Is.EqualTo(GizmoPart.RotateZ));
            Step(end, true);
            Step(end, false);
            Assert.That(Screen.Placement.IsLoose, Is.True, "girar solta da superfície");
            var up = Screen.Placement.LoosePose.Rotation * Vec3.Up;
            float roll = (float)(System.Math.Atan2(-up.X, up.Y) * 180.0 / System.Math.PI);
            Assert.That(System.Math.Abs(roll), Is.EqualTo(15f).Within(0.01f), "múltiplo de 5°");
            _bus.Undo();
            Assert.That(Screen.Placement.IsLoose, Is.False, "desfazer volta à superfície");
        }

        [Test]
        public void RotateRing_WithShift_IsNotSnapped()
        {
            var f = Frame;
            float r = ScreenGizmo.RingRadius * f.Size;
            var start = Ring(f, r, 45f);
            var end = Ring(f, r, 57f); // +12°
            Step(start, false);
            Step(start, true);
            Step(end, true, noSnap: true);
            Step(end, false, noSnap: true);
            var up = Screen.Placement.LoosePose.Rotation * Vec3.Up;
            float roll = (float)(System.Math.Atan2(-up.X, up.Y) * 180.0 / System.Math.PI);
            Assert.That(System.Math.Abs(roll), Is.EqualTo(12f).Within(0.3f));
        }

        /// <summary>Ponto do anel Z no ângulo dado (0° = direita, sentido anti-horário para quem olha).</summary>
        private static Vec3 Ring(GizmoFrame f, float r, float degrees)
        {
            double a = degrees * System.Math.PI / 180.0;
            return f.Origin + (f.Right * (float)System.Math.Cos(a) + f.Up * (float)System.Math.Sin(a)) * r;
        }

        [Test]
        public void ScaleCube_GrowsTheWidth_NeverTheResolution()
        {
            var f = Frame;
            Step(f.Origin, false);
            Step(f.Origin, true);
            Assert.That(_controller.ActiveGizmo, Is.EqualTo(GizmoPart.Scale));
            var diag = (f.Right + f.Up).Normalized;
            Step(f.Origin + diag * (0.5f * f.Size), true);
            Step(f.Origin + diag * (0.5f * f.Size), false);
            Assert.That(Screen.WidthMeters, Is.GreaterThan(1.28f * 1.5f));
            Assert.That(Screen.Resolution, Is.EqualTo(Resolution.Create(2560, 1440)));
        }

        [Test]
        public void WithoutEditMode_ThereIsNoGizmo()
        {
            _controller.GizmoEnabled = false;
            var f = Frame;
            Step(f.Origin + f.Right * (0.6f * f.Size), false);
            Assert.That(_controller.HoverGizmo, Is.EqualTo(GizmoPart.None));
        }
    }
}
