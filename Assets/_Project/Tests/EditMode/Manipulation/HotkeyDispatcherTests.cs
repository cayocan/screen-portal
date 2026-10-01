using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Manipulation;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class HotkeyDispatcherTests
    {
        private WorkspaceStore _store;
        private CommandBus _bus;
        private HotkeyDispatcher _hotkeys;
        private ScreenId _screen;

        [SetUp]
        public void SetUp()
        {
            _store = new WorkspaceStore();
            _bus = new CommandBus(_store, new ICommandObserver[0]);
            _hotkeys = new HotkeyDispatcher(_store, _bus, new FixedHeadPose(Pose.At(new Vec3(0.5f, 1.6f, 0f))));
            _bus.Send(new CreateScreen(Resolution.Create(2560, 1440), Placement.OnSurface(0f, 0f, 1.3f), 1f, 0.2f, "",
                ScreenSource.Virtual));
            _screen = _store.State.Screens[0].Id;
        }

        private VirtualScreen Screen => _store.State.Screens[0];
        private void Press(HotkeyAction action) => _hotkeys.Handle(new HotkeyMessage(action, (uint)_screen.Value));

        [Test]
        public void Arrows_MoveTheScreenFiveDegrees()
        {
            Press(HotkeyAction.MoveRight);
            Press(HotkeyAction.MoveUp);
            Assert.That(Screen.Placement.AzimuthDeg, Is.EqualTo(5f).Within(1e-3f));
            Assert.That(Screen.Placement.ElevationDeg, Is.EqualTo(5f).Within(1e-3f));
            Press(HotkeyAction.MoveLeft);
            Press(HotkeyAction.MoveLeft);
            Assert.That(Screen.Placement.AzimuthDeg, Is.EqualTo(-5f).Within(1e-3f));
        }

        [Test]
        public void SizeChangesTheWidthOnly_NeverTheResolution()
        {
            Press(HotkeyAction.Bigger);
            Assert.That(Screen.WidthMeters, Is.EqualTo(1.1f).Within(1e-4f));
            Press(HotkeyAction.Smaller);
            Press(HotkeyAction.Smaller);
            Assert.That(Screen.WidthMeters, Is.EqualTo(1f / 1.1f).Within(1e-4f));
            Assert.That(Screen.Resolution, Is.EqualTo(Resolution.Create(2560, 1440)));
        }

        [Test]
        public void CloserAndFarther_ChangeTheDistance()
        {
            float before = CylinderMath.HorizontalRadius(Screen.Placement);
            Press(HotkeyAction.Closer);
            Assert.That(CylinderMath.HorizontalRadius(Screen.Placement), Is.EqualTo(before - 0.1f).Within(1e-3f));
            Press(HotkeyAction.Farther);
            Press(HotkeyAction.Farther);
            Assert.That(CylinderMath.HorizontalRadius(Screen.Placement), Is.EqualTo(before + 0.1f).Within(1e-3f));
        }

        [Test]
        public void Curvature_TogglesAndSteps()
        {
            Press(HotkeyAction.CurvatureMore);
            Assert.That(Screen.Curvature, Is.EqualTo(0.3f).Within(1e-4f));
            Press(HotkeyAction.CurvatureToggle);
            Assert.That(Screen.Curvature, Is.EqualTo(0f));
            Press(HotkeyAction.CurvatureLess); // já plana: nada muda, nada para desfazer
            Assert.That(Screen.Curvature, Is.EqualTo(0f));
            _bus.Undo();
            Assert.That(Screen.Curvature, Is.EqualTo(0.3f).Within(1e-4f), "o passo nulo não entrou no desfazer");
        }

        [Test]
        public void FrameRate_Cycles()
        {
            Press(HotkeyAction.CycleFrameRate);
            Assert.That(Screen.FrameRateMode, Is.EqualTo(FrameRateMode.Video));
            Press(HotkeyAction.CycleFrameRate);
            Assert.That(Screen.FrameRateMode, Is.EqualTo(FrameRateMode.Eco));
            Press(HotkeyAction.CycleFrameRate);
            Assert.That(Screen.FrameRateMode, Is.EqualTo(FrameRateMode.Fixed(30)));
            Press(HotkeyAction.CycleFrameRate);
            Assert.That(Screen.FrameRateMode, Is.EqualTo(FrameRateMode.Auto));
        }

        [Test]
        public void UndoRedo_GoThroughTheBus()
        {
            Press(HotkeyAction.MoveRight);
            Press(HotkeyAction.Undo);
            Assert.That(Screen.Placement.AzimuthDeg, Is.EqualTo(0f).Within(1e-3f));
            Press(HotkeyAction.Redo);
            Assert.That(Screen.Placement.AzimuthDeg, Is.EqualTo(5f).Within(1e-3f));
        }

        [Test]
        public void NoScreenUnderTheCursor_OnlyGlobalActionsRun()
        {
            _hotkeys.Handle(new HotkeyMessage(HotkeyAction.MoveRight, 0));
            _hotkeys.Handle(new HotkeyMessage(HotkeyAction.Bigger, 999));
            Assert.That(Screen.Placement.AzimuthDeg, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(Screen.WidthMeters, Is.EqualTo(1f).Within(1e-4f));

            _hotkeys.Handle(new HotkeyMessage(HotkeyAction.ToggleGrid, 0));
            Assert.That(_store.State.Surface.Snap.AngularGrid, Is.True);
            _hotkeys.Handle(new HotkeyMessage(HotkeyAction.ToggleLayoutMode, 0));
            Assert.That(_store.State.Surface.Mode, Is.EqualTo(LayoutMode.Sphere));
            _hotkeys.Handle(new HotkeyMessage(HotkeyAction.Recenter, 0));
            Assert.That(_store.State.Surface.Center.X, Is.EqualTo(0.5f).Within(1e-3f));
        }

        [Test]
        public void LooseScreen_IsNotPulledBackByTheArrows()
        {
            _bus.Send(new SetScreenLoose(_screen, Pose.At(new Vec3(0f, 1f, 1f))));
            Press(HotkeyAction.MoveRight);
            Press(HotkeyAction.Closer);
            Assert.That(Screen.Placement.IsLoose, Is.True);
        }
    }
}
