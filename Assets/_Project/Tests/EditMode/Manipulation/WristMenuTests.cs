using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Manipulation;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class WristMenuTests
    {
        private static readonly Pose Head = new Pose(new Vec3(0f, 1.3f, 0f), Quat.AngleAxis(30f, Vec3.Up));

        private sealed class Rig
        {
            public readonly WorkspaceStore Store = new WorkspaceStore();
            public readonly CommandBus Bus;
            public readonly WristMenu Menu;

            public Rig()
            {
                Bus = new CommandBus(Store, null);
                Menu = new WristMenu(Store, Bus, new FixedHeadPose(Head));
            }
        }

        private sealed class FakeInput : IManipulationInput
        {
            public ManipulationInput Next;
            public bool Read(out ManipulationInput input)
            {
                input = Next;
                return true;
            }
        }

        private sealed class FakePanel : IWristMenuPanel
        {
            public bool Visible => true;
            public Pose Pose => Pose.At(new Vec3(0f, 1f, 0.4f));
            public int LastHover = -2;
            public void Render(WristMenu menu, int hoverIndex) => LastHover = hoverIndex;
        }

        [Test]
        public void AddTestScreen_CreatesNumberedTestScreens_AndUndoBecomesAvailable()
        {
            var r = new Rig();
            Assert.That(r.Menu.IsEnabled(MenuItemId.Undo), Is.False);
            r.Menu.Invoke(MenuItemId.AddTestScreen);
            r.Menu.Invoke(MenuItemId.AddTestScreen);
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(2));
            Assert.That(r.Store.State.Screens[0].Label, Is.EqualTo("Teste 1"));
            Assert.That(TestScreens.IsTestScreen(r.Store.State.Screens[1]), Is.True);
            Assert.That(r.Menu.IsEnabled(MenuItemId.Undo), Is.True);
            Assert.That(r.Menu.StatusLine, Is.EqualTo("Cilindro · 2 telas"));
        }

        [Test]
        public void ToggleModeRecenterAndGrid_SendTheSurfaceCommands()
        {
            var r = new Rig();
            r.Menu.Invoke(MenuItemId.ToggleMode);
            Assert.That(r.Store.State.Surface.Mode, Is.EqualTo(LayoutMode.Sphere));
            r.Menu.Invoke(MenuItemId.Recenter);
            Assert.That(r.Store.State.Surface.Center, Is.EqualTo(Head.Position));
            Assert.That(r.Store.State.Surface.HeadingDeg, Is.EqualTo(30f).Within(0.01f));
            r.Menu.Invoke(MenuItemId.ToggleGrid);
            Assert.That(r.Store.State.Surface.Snap.AngularGrid, Is.True);
            r.Menu.Invoke(MenuItemId.Undo);
            Assert.That(r.Store.State.Surface.Snap.AngularGrid, Is.False);
        }

        [Test]
        public void ReturnAndRemove_NeedAFocusedScreen()
        {
            var r = new Rig();
            Assert.That(r.Menu.IsEnabled(MenuItemId.RemoveScreen), Is.False);
            r.Menu.Invoke(MenuItemId.AddTestScreen);
            Assert.That(r.Menu.IsEnabled(MenuItemId.RemoveScreen), Is.True);
            Assert.That(r.Menu.IsEnabled(MenuItemId.ReturnToSurface), Is.False, "tela na superfície");
            r.Menu.Invoke(MenuItemId.RemoveScreen);
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(0));
        }

        [Test]
        public void MenuLayout_ButtonAtFindsEachButtonCenter()
        {
            int n = WristMenu.Items.Length;
            for (int i = 0; i < n; i++)
            {
                MenuLayout.ButtonCenter(i, n, out float x, out float y);
                Assert.That(MenuLayout.ButtonAt(x, y, n), Is.EqualTo(i));
            }
            Assert.That(MenuLayout.ButtonAt(0f, MenuLayout.PanelHeight(n) * 0.5f - 0.002f, n), Is.EqualTo(-1), "linha de status");
        }

        [Test]
        public void Router_RaySelectOnAButton_InvokesItAndDoesNotGrabScreens()
        {
            var r = new Rig();
            var right = new FakeInput();
            var left = new FakeInput();
            var panel = new FakePanel();
            var controller = new ScreenManipulationController(r.Store, r.Bus);
            var router = new InteractionRouter(right, left, controller, r.Menu, panel, r.Bus);

            int index = System.Array.IndexOf(WristMenu.Items, MenuItemId.AddTestScreen);
            MenuLayout.ButtonCenter(index, WristMenu.Items.Length, out float x, out float y);
            var eye = new Vec3(0f, 1f, 0f);
            var dir = (panel.Pose.Position + new Vec3(x, y, 0f) - eye).Normalized;

            right.Next = new ManipulationInput { HasRay = true, Origin = eye, Direction = dir };
            router.Tick();
            Assert.That(router.MenuHover, Is.EqualTo(index));
            Assert.That(panel.LastHover, Is.EqualTo(index));
            right.Next.SelectHeld = true;
            router.Tick();
            router.Tick(); // segurar não repete
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(1));
            Assert.That(controller.IsDragging, Is.False);
        }

        [Test]
        public void Router_LeftX_Undoes_LeftY_Redoes()
        {
            var r = new Rig();
            var left = new FakeInput();
            var router = new InteractionRouter(NullManipulationInput.Instance, left,
                new ScreenManipulationController(r.Store, r.Bus), r.Menu, null, r.Bus);
            r.Menu.Invoke(MenuItemId.AddTestScreen);
            left.Next = new ManipulationInput { UndoPressed = true };
            router.Tick();
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(0));
            left.Next = new ManipulationInput { RedoPressed = true };
            router.Tick();
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(1));
        }
    }
}
