using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Cursor;
using ScreenPortal.App.Manipulation;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class MouseMenuTests
    {
        private sealed class Source : IMouseStateSource
        {
            public MouseState State;
            public bool Start(int port) => true;
            public void Stop() { }

            public bool TryGetLatest(out MouseState state, out double receivedAt)
            {
                state = State;
                receivedAt = 0;
                return true;
            }
        }

        private ConnectionRig _c;
        private Source _source;
        private CursorSync _sync;
        private EditMode _edit;
        private MouseMenu _menu;
        private HotkeyDispatcher _hotkeys;
        private Vec3 _eye;

        [SetUp]
        public void SetUp()
        {
            _c = new ConnectionRig();
            _c.Bus.Send(new CreateScreen(Resolution.Create(2560, 1440), Placement.OnSurface(30f, 0f, 1.3f), 1.28f, 0f, "", ScreenSource.Virtual));
            _source = new Source();
            _sync = new CursorSync(_c.Machine, _source, _c.Clock);
            _edit = new EditMode(_c.Machine);
            _c.GoToStreaming(ConnectionRig.Welcome);
            _eye = _c.Store.State.Surface.Center;
            var head = new FixedHeadPose(Pose.At(_eye));
            var world = new WorldCursor(_c.Store, head, _ => { });
            _menu = new MouseMenu(_edit, head, _sync, world, _c.Store);
            _hotkeys = new HotkeyDispatcher(_c.Store, _c.Bus, head, null, _edit, null, world, _menu);
            // cursor no centro da tela (a 30° à direita)
            _source.State = new MouseState { Mode = MouseMode.Screen, ScreenKey = (uint)_c.Store.State.Screens[0].Id.Value, X = 1280, Y = 720 };
            _sync.Tick();
        }

        [Test]
        public void CtrlAltM_OpensTheMenuTowardTheCursor_AndHoldsEditMode()
        {
            _hotkeys.Handle(new HotkeyMessage(HotkeyAction.OpenMenu, 0));
            Assert.That(_menu.Open, Is.True);
            Assert.That(_edit.IsOn, Is.True, "o mouse vira o cursor 3D");
            var toMenu = _menu.Pose.Position - _eye;
            Assert.That(toMenu.Length, Is.EqualTo(MouseMenu.DistanceM).Within(1e-3f));
            WorldCursor.AnglesOf(toMenu, out float az, out _);
            Assert.That(az, Is.EqualTo(30f).Within(1f), "na direção do cursor");
        }

        [Test]
        public void EscCloses_AndGivesTheMouseBack()
        {
            _hotkeys.Handle(new HotkeyMessage(HotkeyAction.OpenMenu, 0));
            _hotkeys.Handle(new HotkeyMessage(HotkeyAction.Escape, 0));
            Assert.That(_menu.Open, Is.False);
            Assert.That(_edit.IsOn, Is.False, "Esc fechou o menu, não ligou/desligou a edição");
        }

        [Test]
        public void ClosingKeepsEditModeIfItWasOn()
        {
            _edit.Set(true);
            _hotkeys.Handle(new HotkeyMessage(HotkeyAction.OpenMenu, 0));
            _hotkeys.Handle(new HotkeyMessage(HotkeyAction.OpenMenu, 0));
            Assert.That(_menu.Open, Is.False);
            Assert.That(_edit.IsOn, Is.True);
        }
    }
}
