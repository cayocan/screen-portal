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

        private void Wheel(Vec3 target, int steps, bool coarse)
        {
            var input = At(target, true);
            input.WheelSteps = steps;
            input.Coarse = coarse;
            var none = default(ManipulationInput);
            _controller.Update(in input, in none);
        }

        private float Roll()
        {
            var up = Screen.Placement.LoosePose.Rotation * Vec3.Up;
            return (float)(System.Math.Atan2(-up.X, up.Y) * 180.0 / System.Math.PI);
        }

        [Test]
        public void WheelWhileHoldingARing_RotatesInFineSteps_AndShiftInLongSteps()
        {
            var f = Frame;
            var grab = Ring(f, ScreenGizmo.RingRadius * f.Size, 45f);
            Step(grab, false);
            Step(grab, true);
            Wheel(grab, 3, coarse: false); // sem mexer o mouse
            Wheel(grab, 1, coarse: true);
            Step(grab, false);
            Assert.That(System.Math.Abs(Roll()), Is.EqualTo(18f).Within(0.01f), "3 × 1° + 1 × 15°");
        }

        [Test]
        public void WheelWhileHoldingTheCube_ScalesByOnePercent_OrTen()
        {
            var f = Frame;
            Step(f.Origin, false);
            Step(f.Origin, true);
            Wheel(f.Origin, 2, coarse: false);
            Step(f.Origin, false);
            Assert.That(Screen.WidthMeters, Is.EqualTo(1.28f * 1.01f * 1.01f).Within(1e-4f));
            Step(f.Origin, false);
            Step(f.Origin, true);
            Wheel(f.Origin, -1, coarse: true);
            Step(f.Origin, false);
            Assert.That(Screen.WidthMeters, Is.EqualTo(1.28f * 1.01f * 1.01f / 1.1f).Within(1e-4f), "para baixo divide: subir e descer volta ao mesmo tamanho");
        }

        [Test]
        public void WheelWhileHoldingAnArrow_MovesFiveMillimetersPerStep()
        {
            var f = Frame;
            var grab = f.Origin + f.Right * (0.6f * f.Size);
            var before = _store.State.Surface.ToPose(Screen.Placement).Position;
            Step(grab, false);
            Step(grab, true);
            Wheel(grab, 4, coarse: false);
            Step(grab, false);
            var after = _store.State.Surface.ToPose(Screen.Placement).Position;
            Assert.That((after - before).Length, Is.EqualTo(0.02f).Within(0.002f), "4 × 5 mm (ao longo da superfície)");
            Assert.That(Screen.Placement.IsLoose, Is.False);
        }

        private float Radius => CylinderMath.HorizontalRadius(Screen.Placement);

        [Test]
        public void DepthHandle_IsPickedBelowTheCenter()
        {
            var f = Frame;
            var mid = f.Origin - f.Up * (0.65f * f.Size);
            Assert.That(ScreenGizmo.Pick(in f, _eye, (mid - _eye).Normalized), Is.EqualTo(GizmoPart.MoveZ));
        }

        [Test]
        public void DraggingTheDepthHandleUp_BringsTheScreenCloser_DownPushesItAway()
        {
            var f = Frame;
            var grab = f.Origin - f.Up * (0.65f * f.Size);
            float before = Radius;
            Step(grab, false);
            Step(grab, true);
            Assert.That(_controller.ActiveGizmo, Is.EqualTo(GizmoPart.MoveZ));
            Step(grab + f.Up * 0.1f, true);
            Step(grab + f.Up * 0.1f, false);
            Assert.That(Radius, Is.EqualTo(before - 0.1f).Within(0.01f), "para cima aproxima");
            Assert.That(Screen.Placement.IsLoose, Is.False, "continua na superfície");
            Assert.That(Screen.Placement.AzimuthDeg, Is.EqualTo(0f).Within(0.01f));

            f = Frame;
            grab = f.Origin - f.Up * (0.65f * f.Size);
            Step(grab, false);
            Step(grab, true);
            Step(grab - f.Up * 0.05f, true);
            Step(grab - f.Up * 0.05f, false);
            Assert.That(Radius, Is.EqualTo(before - 0.05f).Within(0.01f), "para baixo afasta");
        }

        [Test]
        public void WheelOnTheDepthHandle_UpBringsCloser()
        {
            var f = Frame;
            var grab = f.Origin - f.Up * (0.65f * f.Size);
            float before = Radius;
            Step(grab, false);
            Step(grab, true);
            Wheel(grab, 2, coarse: false);
            Wheel(grab, -1, coarse: true);
            Step(grab, false);
            Assert.That(Radius, Is.EqualTo(before - 0.01f + 0.05f).Within(1e-3f), "2 × 5 mm mais perto, 1 × 5 cm mais longe");
        }

        [Test]
        public void DepthOnALooseScreen_MovesAlongItsOwnAxis()
        {
            _bus.Send(new SetScreenLoose(Screen.Id, new Pose(new Vec3(0.3f, 1.2f, 1.0f), Quat.AngleAxis(20f, Vec3.Up))));
            var startPose = Screen.Placement.LoosePose;
            var f = Frame;
            var grab = f.Origin - f.Up * (0.65f * f.Size);
            Step(grab, false);
            Step(grab, true);
            Wheel(grab, -2, coarse: true); // 10 cm mais longe
            Step(grab, false);
            var moved = Screen.Placement.LoosePose.Position - startPose.Position;
            Assert.That(moved.Length, Is.EqualTo(0.1f).Within(1e-3f));
            Assert.That(Vec3.Dot(moved.Normalized, startPose.Rotation * Vec3.Forward), Is.GreaterThan(0.999f), "para longe de quem olha (+Z da tela)");
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
