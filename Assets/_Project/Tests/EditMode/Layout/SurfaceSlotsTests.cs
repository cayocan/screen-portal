using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Screens;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class SurfaceSlotsTests
    {
        private static readonly Resolution Hd = Resolution.Create(1920, 1080);

        [Test]
        public void FirstScreen_GoesToTheDefaultPlacement()
        {
            new CreateScreen(Hd, null, 1f).Apply(Workspace.Empty, out var ws);
            Assert.That(ws.Screens[0].Placement, Is.EqualTo(Placement.Default));
        }

        [Test]
        public void NextScreens_GoToTheRight_WithoutOverlap()
        {
            var ws = Workspace.Empty;
            for (int i = 0; i < 3; i++) new CreateScreen(Hd, null, 1f).Apply(ws, out ws);
            for (int i = 1; i < 3; i++)
            {
                Assert.That(ScreenAngles.TryOf(ws.Screens[i - 1], ws.Surface, out var a), Is.True);
                Assert.That(ScreenAngles.TryOf(ws.Screens[i], ws.Surface, out var b), Is.True);
                Assert.That(b.AzimuthDeg, Is.GreaterThan(a.AzimuthDeg));
                Assert.That(b.AzimuthDeg - b.HalfWidthDeg, Is.GreaterThanOrEqualTo(a.AzimuthDeg + a.HalfWidthDeg + SurfaceSlots.GapDeg - 0.01f));
            }
        }

        [Test]
        public void Facing_IsThePointOfTheSurfaceTheHeadLooksAt_AtTheDefaultDistance()
        {
            var head = new Pose(LayoutSurface.DefaultCenter, Quat.AngleAxis(40f, Vec3.Up));
            var p = SurfaceSlots.Facing(LayoutSurface.Default, head);
            Assert.That(p.IsLoose, Is.False);
            Assert.That(p.AzimuthDeg, Is.EqualTo(40f).Within(0.01f));
            Assert.That(p.ElevationDeg, Is.EqualTo(0f).Within(0.01f));
            Assert.That(p.DistanceM, Is.EqualTo(Placement.Default.DistanceM).Within(0.001f));
        }

        [Test]
        public void Facing_FollowsTheSurfaceHeading()
        {
            var surface = LayoutSurface.Default.WithCenter(LayoutSurface.DefaultCenter, 90f);
            var head = new Pose(LayoutSurface.DefaultCenter, Quat.AngleAxis(100f, Vec3.Up));
            Assert.That(SurfaceSlots.Facing(surface, head).AzimuthDeg, Is.EqualTo(10f).Within(0.01f));
        }

        [Test]
        public void CreateNear_FreeSpot_GoesExactlyThere()
        {
            var near = Placement.OnSurface(-70f, 10f, 1.3f);
            new CreateScreen(Hd, null, 1f, near: near).Apply(Workspace.Empty, out var ws);
            Assert.That(ws.Screens[0].Placement, Is.EqualTo(near));
        }

        [Test]
        public void CreateNear_OccupiedSpot_GoesToTheClosestFreeSide()
        {
            // Uma tela no ponto olhado e outra logo à direita dela: o lado livre mais perto é a esquerda.
            var ws = Workspace.Empty;
            new CreateScreen(Hd, Placement.OnSurface(0f, 0f, 1.3f), 1f).Apply(ws, out ws);
            new CreateScreen(Hd, Placement.OnSurface(45f, 0f, 1.3f), 1f).Apply(ws, out ws);
            new CreateScreen(Hd, null, 1f, near: Placement.OnSurface(5f, 0f, 1.3f)).Apply(ws, out ws);

            Assert.That(ScreenAngles.TryOf(ws.Screens[0], ws.Surface, out var occupied), Is.True);
            Assert.That(ScreenAngles.TryOf(ws.Screens[2], ws.Surface, out var created), Is.True);
            Assert.That(created.AzimuthDeg, Is.LessThan(occupied.AzimuthDeg));
            Assert.That(created.AzimuthDeg + created.HalfWidthDeg, Is.LessThanOrEqualTo(occupied.AzimuthDeg - occupied.HalfWidthDeg - SurfaceSlots.GapDeg + 0.01f));
            Assert.That(ws.Screens[2].Placement.ElevationDeg, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void SurfaceChange_ReappliesEveryView()
        {
            var store = new WorkspaceStore();
            store.Dispatch(new CreateScreen(Hd, Placement.Default, 1f));
            var applied = 0;
            var factory = new CountingFactory(() => applied++);
            using (var sync = new ScreenViewSynchronizer(store, factory))
            {
                sync.Start();
                Assert.That(applied, Is.EqualTo(1));
                store.Dispatch(new RecenterLayout(new Vec3(0f, 1.5f, 0f), 90f));
                Assert.That(applied, Is.EqualTo(2), "superfície nova = pose nova, mesmo com a tela intacta");
            }
        }

        private sealed class CountingFactory : IScreenViewFactory
        {
            private readonly System.Action _onApply;
            public CountingFactory(System.Action onApply) => _onApply = onApply;
            public IScreenView Create(VirtualScreen screen) => new View(_onApply);

            private sealed class View : IScreenView
            {
                private readonly System.Action _onApply;
                public View(System.Action onApply) => _onApply = onApply;
                public void Apply(VirtualScreen screen, Pose pose, float radiusM) => _onApply();
                public void ApplyTransform(Pose pose, float widthMeters, float heightMeters, float radiusM) { }
                public void Dispose() { }
            }
        }
    }
}
