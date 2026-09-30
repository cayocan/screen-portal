using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Manipulation;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class WristMenuTests
    {
        private static readonly Pose Head = new Pose(new Vec3(0f, 1.3f, 0f), Quat.AngleAxis(30f, Vec3.Up));

        private static readonly MonitorDescriptor[] Physical =
        {
            new MonitorDescriptor(1, 2560, 1080, @"\\.\DISPLAY1", true),
            new MonitorDescriptor(2, 1920, 1080, @"\\.\DISPLAY2", false),
        };

        private sealed class Rig
        {
            public readonly WorkspaceStore Store = new WorkspaceStore();
            public readonly CommandBus Bus;
            public readonly FixedHostCatalog Host = new FixedHostCatalog(true, Physical);
            public readonly WristMenu Menu;

            public Rig(bool connected = true, bool showTestScreens = true)
            {
                Host.IsConnected = connected;
                Bus = new CommandBus(Store, null);
                Menu = new WristMenu(Store, Bus, new FixedHeadPose(Head), Host, showTestScreens);
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
            public bool Visible { get; set; } = true;
            public Pose Pose => Pose.At(new Vec3(0f, 1f, 0.4f));
            public int LastHover = -2;
            public void Render(WristMenu menu, int hoverIndex) => LastHover = hoverIndex;
        }

        [Test]
        public void AddTestScreen_CreatesNumberedTestScreens_AndUndoBecomesAvailable()
        {
            var r = new Rig();
            Assert.That(r.Menu.IsEnabled(r.Menu.IndexOf(MenuItemId.Undo)), Is.False);
            r.Menu.Invoke(MenuItemId.AddTestScreen);
            r.Menu.Invoke(MenuItemId.AddTestScreen);
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(2));
            Assert.That(r.Store.State.Screens[0].Label, Is.EqualTo("Teste 1"));
            Assert.That(r.Menu.IsEnabled(r.Menu.IndexOf(MenuItemId.Undo)), Is.True);
            Assert.That(r.Menu.StatusLine, Is.EqualTo("Cilindro · 2 telas"));
        }

        [Test]
        public void TestScreenItem_OnlyInDevBuilds()
        {
            Assert.That(new Rig(showTestScreens: false).Menu.IndexOf(MenuItemId.AddTestScreen), Is.EqualTo(-1));
            Assert.That(new Rig(showTestScreens: true).Menu.IndexOf(MenuItemId.AddTestScreen), Is.GreaterThanOrEqualTo(0));
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
            Assert.That(r.Menu.IsEnabled(r.Menu.IndexOf(MenuItemId.RemoveScreen)), Is.False);
            r.Menu.Invoke(MenuItemId.AddTestScreen);
            Assert.That(r.Menu.IsEnabled(r.Menu.IndexOf(MenuItemId.RemoveScreen)), Is.True);
            Assert.That(r.Menu.IsEnabled(r.Menu.IndexOf(MenuItemId.ReturnToSurface)), Is.False, "tela na superfície");
            r.Menu.Invoke(MenuItemId.RemoveScreen);
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(0));
        }

        [Test]
        public void Offline_CreateItemsAreDisabled_AndStatusAsksToConnect()
        {
            var r = new Rig(connected: false);
            Assert.That(r.Menu.IsEnabled(r.Menu.IndexOf(MenuItemId.AddVirtual)), Is.False);
            Assert.That(r.Menu.IsEnabled(r.Menu.IndexOf(MenuItemId.AddMirror)), Is.False);
            Assert.That(r.Menu.StatusLine, Is.EqualTo("Cilindro · 0 telas · Conecte ao PC"));
        }

        [Test]
        public void AddVirtual_AspectPage_HasOrientationSixFramesAndBack()
        {
            var r = new Rig();
            r.Menu.Invoke(MenuItemId.AddVirtual);
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Aspects));
            Assert.That(r.Menu.ItemCount, Is.EqualTo(8));
            Assert.That(r.Menu.Label(0), Is.EqualTo("Orientação: Paisagem"));
            Assert.That(r.Menu.FrameAspect(0), Is.EqualTo(0f));
            Assert.That(r.Menu.Label(1), Is.EqualTo("16:9"));
            Assert.That(r.Menu.FrameAspect(1), Is.EqualTo(16f / 9f).Within(1e-5f));
            Assert.That(r.Menu.Label(7), Is.EqualTo(WristMenu.BackLabel));

            r.Menu.Invoke(0); // Paisagem → Retrato
            Assert.That(r.Menu.Orientation, Is.EqualTo(Orientation.Portrait));
            Assert.That(r.Menu.Label(1), Is.EqualTo("9:16"));
            Assert.That(r.Menu.FrameAspect(1), Is.EqualTo(9f / 16f).Within(1e-5f));

            r.Menu.Invoke(7); // voltar
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Main));
        }

        [Test]
        public void PickingAspectThenResolution_CreatesAVirtualScreen_AndReturnsToMain()
        {
            var r = new Rig();
            r.Menu.Invoke(MenuItemId.AddVirtual);
            r.Menu.Invoke(1); // 16:9
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Resolutions));
            Assert.That(r.Menu.ItemCount, Is.EqualTo(4));
            Assert.That(r.Menu.Label(1), Is.EqualTo("2560×1440"));

            r.Menu.Invoke(3); // voltar → proporções
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Aspects));
            r.Menu.Invoke(1);
            r.Menu.Invoke(1); // 2560×1440

            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Main));
            var screen = r.Store.State.Screens[0];
            Assert.That(screen.Source, Is.EqualTo(ScreenSource.Virtual));
            Assert.That(screen.Resolution, Is.EqualTo(Resolution.Create(2560, 1440)));
            Assert.That(screen.WidthMeters, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(screen.Label, Is.EqualTo("2560×1440"));
        }

        [Test]
        public void MirrorPage_ListsPhysicalMonitors_AndCreatesAMirror()
        {
            var r = new Rig();
            r.Menu.Invoke(MenuItemId.AddMirror);
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Mirrors));
            Assert.That(r.Menu.ItemCount, Is.EqualTo(3));
            Assert.That(r.Menu.Label(1), Is.EqualTo("DISPLAY2 · 1920×1080"));

            r.Menu.Invoke(1);
            var screen = r.Store.State.Screens[0];
            Assert.That(screen.Source, Is.EqualTo(ScreenSource.Mirror(2)));
            Assert.That(screen.Resolution, Is.EqualTo(Resolution.Create(1920, 1080)));
            Assert.That(screen.Label, Is.EqualTo("DISPLAY2"));
        }

        [Test]
        public void CreatedScreen_AppearsWhereTheHeadIsLooking()
        {
            var r = new Rig();
            r.Menu.Invoke(MenuItemId.AddMirror);
            r.Menu.Invoke(1);
            var expected = SurfaceSlots.Facing(r.Store.State.Surface, Head);
            Assert.That(r.Store.State.Screens[0].Placement.AzimuthDeg, Is.EqualTo(expected.AzimuthDeg).Within(0.01f));
            Assert.That(expected.AzimuthDeg, Is.EqualTo(30f).Within(0.01f));
        }

        [Test]
        public void FifthVideoScreen_ShowsTheWarning_TestScreensDoNotCount_HidingClearsIt()
        {
            var r = new Rig();
            r.Menu.Invoke(MenuItemId.AddTestScreen);
            for (int i = 0; i < 4; i++)
            {
                r.Menu.Invoke(MenuItemId.AddVirtual);
                r.Menu.Invoke(1);
                r.Menu.Invoke(0);
            }
            Assert.That(r.Menu.StatusLine, Does.Not.Contain("Mais de 4"), "4 telas com vídeo + 1 de teste");

            r.Menu.Invoke(MenuItemId.AddVirtual);
            r.Menu.Invoke(1);
            r.Menu.Invoke(0);
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(6), "cria mesmo assim");
            Assert.That(r.Menu.StatusLine, Is.EqualTo(WristMenu.TooManyNotice));

            r.Menu.OnHidden();
            Assert.That(r.Menu.StatusLine, Is.EqualTo("Cilindro · 6 telas"));
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Main));
        }

        [Test]
        public void MenuLayout_ButtonAtFindsEachButtonCenter()
        {
            int n = new Rig().Menu.ItemCount;
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

            int index = r.Menu.IndexOf(MenuItemId.AddTestScreen);
            MenuLayout.ButtonCenter(index, r.Menu.ItemCount, out float x, out float y);
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
        public void Router_PanelHidden_ResetsTheMenuPage()
        {
            var r = new Rig();
            var panel = new FakePanel();
            var router = new InteractionRouter(NullManipulationInput.Instance, NullManipulationInput.Instance,
                new ScreenManipulationController(r.Store, r.Bus), r.Menu, panel, r.Bus);
            r.Menu.Invoke(MenuItemId.AddVirtual);
            router.Tick();
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Aspects));
            panel.Visible = false;
            router.Tick();
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Main));
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

        private static void AddVirtual2560(Rig r)
        {
            r.Menu.Invoke(MenuItemId.AddVirtual);
            r.Menu.Invoke(1); // 16:9
            r.Menu.Invoke(1); // 2560×1440
        }

        [Test]
        public void Recreate_DisabledWithoutPc_WithoutFocus_OrWhenTheFocusIsNotAVirtualMonitor()
        {
            var r = new Rig();
            int item = r.Menu.IndexOf(MenuItemId.Recreate);
            Assert.That(r.Menu.Label(item), Is.EqualTo(WristMenu.RecreateLabel));
            Assert.That(r.Menu.IsEnabled(item), Is.False, "sem tela em foco");

            r.Menu.Invoke(MenuItemId.AddTestScreen);
            Assert.That(r.Menu.IsEnabled(item), Is.False, "tela de teste");
            r.Menu.Invoke(MenuItemId.AddMirror);
            r.Menu.Invoke(1);
            Assert.That(r.Menu.IsEnabled(item), Is.False, "espelho");

            AddVirtual2560(r);
            Assert.That(r.Menu.IsEnabled(item), Is.True);
            r.Host.IsConnected = false;
            Assert.That(r.Menu.IsEnabled(item), Is.False, "sem PC");
        }

        [Test]
        public void Recreate_OpensTheAspects_WithTheTitle_AndMarksTheCurrentResolution()
        {
            var r = new Rig();
            AddVirtual2560(r);
            r.Menu.Invoke(MenuItemId.Recreate);
            Assert.That(r.Menu.IsRecreating, Is.True);
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Aspects));
            Assert.That(r.Menu.Orientation, Is.EqualTo(Orientation.Landscape));
            Assert.That(r.Menu.StatusLine, Is.EqualTo("Recriar: 2560×1440"));

            r.Menu.Invoke(1); // 16:9
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Resolutions));
            Assert.That(r.Menu.Label(0), Is.EqualTo("1920×1080"));
            Assert.That(r.Menu.IsEnabled(0), Is.True);
            Assert.That(r.Menu.Label(1), Is.EqualTo("2560×1440 · atual"));
            Assert.That(r.Menu.IsEnabled(1), Is.False);

            var before = r.Store.State;
            r.Menu.Invoke(1); // apagado: nada acontece
            Assert.That(r.Store.State, Is.SameAs(before));
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Resolutions));
        }

        [Test]
        public void Recreate_StartsInTheOrientationOfTheFocusedScreen()
        {
            var r = new Rig();
            r.Menu.Invoke(MenuItemId.AddVirtual);
            r.Menu.Invoke(0); // Paisagem → Retrato
            r.Menu.Invoke(1); // 9:16
            r.Menu.Invoke(1); // 1440×2560
            Assert.That(r.Store.State.Screens[0].Resolution, Is.EqualTo(Resolution.Create(1440, 2560)));

            r.Menu.Invoke(MenuItemId.AddVirtual);
            r.Menu.Invoke(0); // Retrato → Paisagem
            r.Menu.Invoke(r.Menu.ItemCount - 1); // voltar
            Assert.That(r.Menu.Orientation, Is.EqualTo(Orientation.Landscape));

            r.Menu.Invoke(MenuItemId.Recreate);
            Assert.That(r.Menu.Orientation, Is.EqualTo(Orientation.Portrait));
            r.Menu.Invoke(1); // 9:16
            Assert.That(r.Menu.Label(1), Is.EqualTo("1440×2560 · atual"));
        }

        [Test]
        public void PickingAResolution_SendsRecreateScreen_ReturnsToMain_AndUndoRestores()
        {
            var r = new Rig();
            AddVirtual2560(r);
            var id = r.Store.State.FocusedScreenId;
            float width = r.Store.State.Screens[0].WidthMeters;

            r.Menu.Invoke(MenuItemId.Recreate);
            r.Menu.Invoke(0); // Paisagem → Retrato
            r.Menu.Invoke(1); // 9:16
            r.Menu.Invoke(1); // 1440×2560

            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Main));
            Assert.That(r.Menu.IsRecreating, Is.False);
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(1));
            var screen = r.Store.State.Screens[0];
            Assert.That(screen.Id, Is.EqualTo(id));
            Assert.That(screen.Resolution, Is.EqualTo(Resolution.Create(1440, 2560)));
            Assert.That(screen.WidthMeters, Is.EqualTo(width).Within(1e-5f), "mesma largura; a altura segue a proporção");
            Assert.That(r.Menu.StatusLine, Is.EqualTo("Cilindro · 1 tela"));

            r.Menu.Invoke(MenuItemId.Undo);
            Assert.That(r.Store.State.Screens[0].Resolution, Is.EqualTo(Resolution.Create(2560, 1440)));
        }

        [Test]
        public void Back_LeavesRecreateMode_WithoutSendingAnything()
        {
            var r = new Rig();
            AddVirtual2560(r);
            var before = r.Store.State;

            r.Menu.Invoke(MenuItemId.Recreate);
            r.Menu.Invoke(1); // 16:9
            r.Menu.Invoke(r.Menu.ItemCount - 1); // voltar → proporções, ainda recriando
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Aspects));
            Assert.That(r.Menu.IsRecreating, Is.True);
            r.Menu.Invoke(r.Menu.ItemCount - 1); // voltar → principal
            Assert.That(r.Menu.Page, Is.EqualTo(MenuPage.Main));
            Assert.That(r.Menu.IsRecreating, Is.False);
            Assert.That(r.Store.State, Is.SameAs(before));
            Assert.That(r.Menu.StatusLine, Is.EqualTo("Cilindro · 1 tela"));

            r.Menu.Invoke(MenuItemId.AddVirtual); // "+ Monitor virtual" não marca nada
            r.Menu.Invoke(1);
            Assert.That(r.Menu.Label(1), Is.EqualTo("2560×1440"));
            Assert.That(r.Menu.IsEnabled(1), Is.True);

            r.Menu.OnHidden();
            r.Menu.Invoke(MenuItemId.Recreate);
            r.Menu.OnHidden(); // esconder o painel também sai do modo
            Assert.That(r.Menu.IsRecreating, Is.False);
            Assert.That(r.Store.State, Is.SameAs(before));
        }

        [Test]
        public void Recreate_DoesNotShowTheTooManyScreensWarning()
        {
            var r = new Rig();
            for (int i = 0; i < 5; i++) AddVirtual2560(r);
            Assert.That(r.Menu.StatusLine, Is.EqualTo(WristMenu.TooManyNotice));
            r.Menu.OnHidden();

            r.Menu.Invoke(MenuItemId.Recreate);
            r.Menu.Invoke(1);
            r.Menu.Invoke(0); // 1920×1080
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(5));
            Assert.That(r.Menu.StatusLine, Is.EqualTo("Cilindro · 5 telas"));
        }
    }
}
