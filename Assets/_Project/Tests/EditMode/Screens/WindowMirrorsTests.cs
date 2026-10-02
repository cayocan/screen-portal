using System.Linq;
using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Manipulation;
using ScreenPortal.App.Menu;
using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class WindowMirrorsTests
    {
        private ConnectionRig _c;
        private WindowMirrors _mirrors;

        [SetUp]
        public void SetUp()
        {
            _c = new ConnectionRig();
            _mirrors = new WindowMirrors(_c.Machine, _c.Store, _c.Bus, new FixedHeadPose(Pose.At(_c.Store.State.Surface.Center)));
            _c.GoToStreaming(ConnectionRig.Welcome);
        }

        private void Receive(ControlMessage message)
        {
            _c.Channel.Push(ChannelEventKind.Message, message);
            _c.Machine.Tick();
        }

        private VirtualScreen Mirror => _c.Store.State.Screens.Single(s => s.Source.Kind == ScreenSourceKind.Window);

        [Test]
        public void DetachWindow_CreatesAMirrorScreen_OfTheWindowSize_InFrontOfTheView()
        {
            Receive(new DetachWindowMessage(0x8B05BC, 1096, 824, "Sem título - Bloco de Notas"));
            Assert.That(Mirror.Source, Is.EqualTo(ScreenSource.Window(0x8B05BC)));
            Assert.That(Mirror.Resolution, Is.EqualTo(Resolution.Create(1096, 824)));
            Assert.That(Mirror.Label, Is.EqualTo("Sem título - Bloco de Notas"));
            Assert.That(Mirror.Placement.IsLoose, Is.False);
        }

        [Test]
        public void TheSameWindowTwice_IsMirroredOnce()
        {
            Receive(new DetachWindowMessage(7, 800, 600, "a"));
            Receive(new DetachWindowMessage(7, 800, 600, "a"));
            Assert.That(_c.Store.State.Screens.Count(s => s.Source.Kind == ScreenSourceKind.Window), Is.EqualTo(1));
        }

        [Test]
        public void WindowClosedOnThePc_RemovesTheMirror_OtherFailuresDoNot()
        {
            Receive(new DetachWindowMessage(7, 800, 600, "a"));
            var id = Mirror.Id;
            Receive(new ScreenFailedMessage((uint)id.Value, "A janela foi fechada."));
            Assert.That(_c.Store.State.Contains(id), Is.False);
        }

        [Test]
        public void Reattach_RemovesTheMirror_AndUndoBringsItBack()
        {
            Receive(new DetachWindowMessage(7, 800, 600, "a"));
            var id = Mirror.Id;
            var hotkeys = new HotkeyDispatcher(_c.Store, _c.Bus, new FixedHeadPose(Pose.Identity), null, null, null, null, null, _mirrors);
            hotkeys.Handle(new HotkeyMessage(HotkeyAction.ReattachWindow, (uint)id.Value));
            Assert.That(_c.Store.State.Contains(id), Is.False);
            _c.Bus.Undo();
            Assert.That(_c.Store.State.Contains(id), Is.True, "desfazer espelha de novo");
        }

        [Test]
        public void Menu_ShowsReattach_ForAMirror()
        {
            Receive(new DetachWindowMessage(7, 800, 600, "a"));
            var menu = new WristMenu(_c.Store, _c.Bus, new FixedHeadPose(Pose.Identity), new FixedHostCatalog(true, new MonitorDescriptor[0]));
            int index = menu.IndexOf(MenuItemId.RemoveScreen);
            Assert.That(menu.Label(index), Is.EqualTo(WristMenu.ReattachLabel));
        }
    }
}
