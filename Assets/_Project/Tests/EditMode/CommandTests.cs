using NUnit.Framework;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class CommandTests
    {
        private static readonly Resolution Ultrawide = Resolution.Create(2560, 1080);
        private static readonly Pose Front = Pose.At(new Vec3(0f, 1.2f, 1.3f));

        private static Workspace WithOneScreen(out ScreenId id)
        {
            Assert.That(new CreateScreen(Ultrawide, Front, 1.4f).Apply(Workspace.Empty, out var ws).Accepted, Is.True);
            id = ws.FocusedScreenId;
            return ws;
        }

        private static VirtualScreen Get(Workspace ws, ScreenId id)
        {
            Assert.That(ws.TryGetScreen(id, out var s), Is.True);
            return s;
        }

        [Test]
        public void CreateScreen_AddsAndFocusesNewScreen()
        {
            var ws = WithOneScreen(out var id);
            Assert.That(id, Is.EqualTo(new ScreenId(1)));
            var s = Get(ws, id);
            Assert.That(s.Resolution, Is.EqualTo(Ultrawide));
            Assert.That(s.Pose, Is.EqualTo(Front));
            Assert.That(s.WidthMeters, Is.EqualTo(1.4f));
        }

        [Test]
        public void CreateScreen_WithInvalidResolution_IsRejectedAndStateUnchanged()
        {
            var result = new CreateScreen(default(Resolution), Front, 1f).Apply(Workspace.Empty, out var next);
            Assert.That(result.Accepted, Is.False);
            Assert.That(next, Is.SameAs(Workspace.Empty));
        }

        [TestCase(0.01f)]
        [TestCase(0.5f)]
        [TestCase(3f)]
        [TestCase(100f)]
        public void Invariant_ResizeNeverChangesResolutionOrOrientation(float width)
        {
            var ws = WithOneScreen(out var id);
            new ResizeScreen(id, width).Apply(ws, out var next);
            var s = Get(next, id);
            Assert.That(s.Resolution, Is.EqualTo(Ultrawide));
            Assert.That(s.Orientation, Is.EqualTo(Orientation.Landscape));
            Assert.That(s.WidthMeters, Is.EqualTo(ScreenLimits.ClampWidth(width)));
        }

        [TestCase(0f)]
        [TestCase(0.3f)]
        [TestCase(1f)]
        [TestCase(5f)]
        public void Invariant_CurvatureNeverChangesResolutionOrientationOrWidth(float curvature)
        {
            var ws = WithOneScreen(out var id);
            new SetScreenCurvature(id, curvature).Apply(ws, out var next);
            var s = Get(next, id);
            Assert.That(s.Resolution, Is.EqualTo(Ultrawide));
            Assert.That(s.Orientation, Is.EqualTo(Orientation.Landscape));
            Assert.That(s.WidthMeters, Is.EqualTo(1.4f));
            Assert.That(s.Curvature, Is.EqualTo(ScreenLimits.Clamp01(curvature)));
        }

        [Test]
        public void ToggleCurvature_FlatToLastUsedAndBack()
        {
            var ws = WithOneScreen(out var id);
            new ToggleScreenCurvature(id).Apply(ws, out ws);
            Assert.That(Get(ws, id).Curvature, Is.EqualTo(ScreenLimits.DefaultCurvature), "primeira vez: Média");

            new SetScreenCurvature(id, 0.3f).Apply(ws, out ws);
            new ToggleScreenCurvature(id).Apply(ws, out ws);
            Assert.That(Get(ws, id).Curvature, Is.EqualTo(0f));
            new ToggleScreenCurvature(id).Apply(ws, out ws);
            Assert.That(Get(ws, id).Curvature, Is.EqualTo(0.3f), "volta à última curvatura usada");
        }

        [Test]
        public void MoveScreen_ChangesOnlyPose()
        {
            var ws = WithOneScreen(out var id);
            var target = Pose.At(new Vec3(-1f, 1.5f, 1f));
            new MoveScreen(id, target).Apply(ws, out var next);
            var s = Get(next, id);
            Assert.That(s.Pose, Is.EqualTo(target));
            Assert.That(s.WidthMeters, Is.EqualTo(1.4f));
            Assert.That(s.Resolution, Is.EqualTo(Ultrawide));
        }

        [Test]
        public void CommandsOnUnknownScreen_AreRejected()
        {
            var unknown = new ScreenId(42);
            ICommand[] commands =
            {
                new RemoveScreen(unknown), new MoveScreen(unknown, Front), new ResizeScreen(unknown, 1f),
                new SetScreenCurvature(unknown, 0.5f), new ToggleScreenCurvature(unknown),
                new SetFrameRateMode(unknown, FrameRateMode.Video), new RecreateScreen(unknown, Ultrawide),
                new SetFocus(unknown),
            };
            foreach (var command in commands)
            {
                var result = command.Apply(Workspace.Empty, out var next);
                Assert.That(result.Accepted, Is.False, command.GetType().Name);
                Assert.That(result.Error, Does.Contain("42"), command.GetType().Name);
                Assert.That(next, Is.SameAs(Workspace.Empty), command.GetType().Name);
            }
        }

        [Test]
        public void RecreateScreen_ChangesResolutionKeepsPoseAndPhysicalWidth()
        {
            var ws = WithOneScreen(out var id);
            var portrait = Resolution.Create(1440, 2560);
            new RecreateScreen(id, portrait).Apply(ws, out var next);
            var s = Get(next, id);
            Assert.That(s.Resolution, Is.EqualTo(portrait));
            Assert.That(s.Orientation, Is.EqualTo(Orientation.Portrait));
            Assert.That(s.Pose, Is.EqualTo(Front));
            Assert.That(s.WidthMeters, Is.EqualTo(1.4f));
        }

        [Test]
        public void SetFrameRateMode_IsStoredPerScreen()
        {
            var ws = WithOneScreen(out var id);
            new SetFrameRateMode(id, FrameRateMode.Fixed(30)).Apply(ws, out var next);
            Assert.That(Get(next, id).FrameRateMode, Is.EqualTo(FrameRateMode.Fixed(30)));
        }

        [Test]
        public void RemoveScreen_RemovesAndClearsFocus()
        {
            var ws = WithOneScreen(out var id);
            new RemoveScreen(id).Apply(ws, out var next);
            Assert.That(next.Contains(id), Is.False);
            Assert.That(next.FocusedScreenId.IsNone, Is.True);
        }

        [Test]
        public void SetFocus_IsNotUndoable_OthersAre()
        {
            Assert.That(new SetFocus(ScreenId.None).IsUndoable, Is.False);
            Assert.That(new MoveScreen(new ScreenId(1), Front).IsUndoable, Is.True);
            Assert.That(new CreateScreen(Ultrawide, Front, 1f).IsUndoable, Is.True);
        }
    }
}
