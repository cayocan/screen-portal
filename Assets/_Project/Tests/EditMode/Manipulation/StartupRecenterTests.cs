using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class StartupRecenterTests
    {
        private sealed class MovableHead : IHeadPoseSource
        {
            public Pose HeadPose { get; set; }
        }

        private static (WorkspaceStore store, MovableHead head, StartupRecenter recenter) Rig()
        {
            var store = new WorkspaceStore();
            var head = new MovableHead { HeadPose = Pose.Identity }; // antes do tracking: na origem
            return (store, head, new StartupRecenter(new CommandBus(store, null), head));
        }

        [Test]
        public void WaitsForTracking_ThenCentersTheSurfaceOnTheHead_Once()
        {
            var (store, head, recenter) = Rig();
            recenter.Tick();
            Assert.That(store.State.Surface.Center, Is.EqualTo(LayoutSurface.DefaultCenter), "sem tracking: não mexe");

            head.HeadPose = new Pose(new Vec3(0.4f, 1.1f, -0.3f), Quat.AngleAxis(70f, Vec3.Up));
            recenter.Tick();
            Assert.That(store.State.Surface.Center, Is.EqualTo(new Vec3(0.4f, 1.1f, -0.3f)));
            Assert.That(store.State.Surface.HeadingDeg, Is.EqualTo(70f).Within(0.01f));
            Assert.That(recenter.Done, Is.True);

            head.HeadPose = new Pose(new Vec3(2f, 1.5f, 2f), Quat.Identity);
            recenter.Tick();
            Assert.That(store.State.Surface.Center, Is.EqualTo(new Vec3(0.4f, 1.1f, -0.3f)), "só na abertura");
        }

        [Test]
        public void StartupRecenter_IsNotUndoable()
        {
            var (store, head, recenter) = Rig();
            head.HeadPose = new Pose(new Vec3(0f, 1.3f, 0f), Quat.AngleAxis(30f, Vec3.Up));
            recenter.Tick();
            Assert.That(store.CanUndo, Is.False, "o primeiro undo do usuário não pode descentralizar");
        }
    }
}
