using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;
using static ScreenPortal.Tests.SpatialMathTests;

namespace ScreenPortal.Tests
{
    public class LayoutCommandTests
    {
        private static readonly Resolution Hd = Resolution.Create(1920, 1080);

        private static WorkspaceStore Store(params Placement[] placements)
        {
            var store = new WorkspaceStore();
            foreach (var p in placements) store.Dispatch(new CreateScreen(Hd, p, 1f));
            return store;
        }

        private static Vec3 WorldOf(Workspace ws, int index) => ws.Surface.ToPose(ws.Screens[index].Placement).Position;

        [Test]
        public void SetLayoutMode_KeepsEveryScreenWhereItIs_EvenLeavingATiltedSphere_AndUndoRestoresExactly()
        {
            var store = Store(Placement.OnSurface(-40f, 10f, 1.5f), Placement.OnSurface(35f, -20f, 2f));
            store.Dispatch(new SetLayoutMode(LayoutMode.Sphere));
            store.Dispatch(new SetSurfaceTilt(40f));
            var before = store.State;
            var p0 = WorldOf(before, 0);
            var p1 = WorldOf(before, 1);

            Assert.That(store.Dispatch(new SetLayoutMode(LayoutMode.Cylinder)).Accepted, Is.True);
            AssertNear(WorldOf(store.State, 0), p0, 0.001f);
            AssertNear(WorldOf(store.State, 1), p1, 0.001f);

            store.Undo();
            Assert.That(store.State, Is.SameAs(before));
        }

        [Test]
        public void SetLayoutMode_ToTheSameMode_IsRejected() =>
            Assert.That(Store().Dispatch(new SetLayoutMode(LayoutMode.Cylinder)).Accepted, Is.False);

        [Test]
        public void RecenterLayout_KeepsPlacements_AndMovesAllScreensTogether()
        {
            var store = Store(Placement.OnSurface(-30f, 0f, 1.3f), Placement.OnSurface(30f, 5f, 1.3f));
            var placements = new[] { store.State.Screens[0].Placement, store.State.Screens[1].Placement };
            float gapBefore = (WorldOf(store.State, 1) - WorldOf(store.State, 0)).Length;

            store.Dispatch(new RecenterLayout(new Vec3(1f, 1.4f, 2f), 90f));

            Assert.That(store.State.Screens[0].Placement, Is.EqualTo(placements[0]));
            Assert.That(store.State.Screens[1].Placement, Is.EqualTo(placements[1]));
            Assert.That((WorldOf(store.State, 1) - WorldOf(store.State, 0)).Length, Is.EqualTo(gapBefore).Within(0.001f));
            var front = store.State.Surface.ToPose(Placement.OnSurface(0f, 0f, 1f)).Position;
            AssertNear(front, new Vec3(2f, 1.4f, 2f), 0.001f);
        }

        [Test]
        public void LooseScreens_KeepTheirWorldPose_OnModeRecenterAndTilt()
        {
            var loosePose = new Pose(new Vec3(0.5f, 1.6f, 0.9f), Quat.AngleAxis(20f, Vec3.Up));
            var store = Store(Placement.Default);
            var id = store.State.Screens[0].Id;
            store.Dispatch(new SetScreenLoose(id, loosePose));

            store.Dispatch(new SetLayoutMode(LayoutMode.Sphere));
            store.Dispatch(new SetSurfaceTilt(30f));
            store.Dispatch(new RecenterLayout(new Vec3(3f, 1f, 0f), 45f));

            Assert.That(store.State.Screens[0].Placement.IsLoose, Is.True);
            Assert.That(store.State.Surface.ToPose(store.State.Screens[0].Placement), Is.EqualTo(loosePose));
        }

        [Test]
        public void ReturnScreenToSurface_PicksTheNearestPoint_AndRejectsScreensAlreadyOnIt()
        {
            var store = Store(Placement.Default);
            var id = store.State.Screens[0].Id;
            var target = Placement.OnSurface(20f, 5f, 1.5f);
            var world = store.State.Surface.ToPose(target).Position;
            Assert.That(store.Dispatch(new ReturnScreenToSurface(id)).Accepted, Is.False, "já está na superfície");

            store.Dispatch(new SetScreenLoose(id, Pose.At(world)));
            Assert.That(store.Dispatch(new ReturnScreenToSurface(id)).Accepted, Is.True);
            var back = store.State.Screens[0].Placement;
            Assert.That(back.IsLoose, Is.False);
            Assert.That(back.AzimuthDeg, Is.EqualTo(20f).Within(0.01f));
            Assert.That(back.ElevationDeg, Is.EqualTo(5f).Within(0.01f));
            Assert.That(back.DistanceM, Is.EqualTo(1.5f).Within(0.001f));
        }

        [Test]
        public void SetScreenDistance_SphereUsesDistance_CylinderKeepsHeight_LooseIsRejected()
        {
            var store = Store(Placement.OnSurface(0f, 30f, 1.2f));
            var id = store.State.Screens[0].Id;
            float height = CylinderMath.Height(store.State.Screens[0].Placement);

            store.Dispatch(new SetScreenDistance(id, 2f));
            Assert.That(CylinderMath.HorizontalRadius(store.State.Screens[0].Placement), Is.EqualTo(2f).Within(0.001f));
            Assert.That(CylinderMath.Height(store.State.Screens[0].Placement), Is.EqualTo(height).Within(0.001f));

            store.Dispatch(new SetLayoutMode(LayoutMode.Sphere));
            store.Dispatch(new SetScreenDistance(id, 3f));
            Assert.That(store.State.Screens[0].Placement.DistanceM, Is.EqualTo(3f).Within(0.001f));

            store.Dispatch(new SetScreenLoose(id, Pose.At(new Vec3(0f, 1f, 1f))));
            Assert.That(store.Dispatch(new SetScreenDistance(id, 1f)).Accepted, Is.False);
        }

        [Test]
        public void AlignSurfaceToPosture_UsesHeadPositionYawAndPitch_ClampedTo0To60()
        {
            var store = Store();
            var head = new Pose(new Vec3(0.1f, 0.9f, 0.2f), Quat.AngleAxis(30f, Vec3.Up) * Quat.AngleAxis(-35f, Vec3.Right));
            store.Dispatch(new AlignSurfaceToPosture(head));
            var s = store.State.Surface;
            AssertNear(s.Center, head.Position);
            Assert.That(s.HeadingDeg, Is.EqualTo(30f).Within(0.01f));
            Assert.That(s.TiltDeg, Is.EqualTo(35f).Within(0.01f));

            store.Dispatch(new AlignSurfaceToPosture(new Pose(head.Position, Quat.AngleAxis(-80f, Vec3.Right))));
            Assert.That(store.State.Surface.TiltDeg, Is.EqualTo(60f));
            store.Dispatch(new AlignSurfaceToPosture(new Pose(head.Position, Quat.AngleAxis(20f, Vec3.Right))));
            Assert.That(store.State.Surface.TiltDeg, Is.EqualTo(0f));
        }

        [Test]
        public void SetSnapOptions_IsStoredAndUndoable()
        {
            var store = Store();
            var grid = SnapOptions.Default.WithAngularGrid(true);
            store.Dispatch(new SetSnapOptions(grid));
            Assert.That(store.State.Surface.Snap, Is.EqualTo(grid));
            store.Undo();
            Assert.That(store.State.Surface.Snap, Is.EqualTo(SnapOptions.Default));
        }
    }
}
