using System.Linq;
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
    public class EditModeTests
    {
        private sealed class Pointer : IManipulationInput
        {
            public ManipulationInput Next;
            public bool Read(out ManipulationInput input)
            {
                input = Next;
                return true;
            }
        }

        [Test]
        public void TogglingTellsTheHost_AndWelcomeStartsOff()
        {
            var c = new ConnectionRig();
            var edit = new EditMode(c.Machine);
            edit.Set(true); // antes de conectar
            c.GoToStreaming(ConnectionRig.Welcome);
            Assert.That(edit.IsOn, Is.False, "cada conexão começa sem edição");
            edit.Toggle();
            Assert.That(edit.IsOn, Is.True);
            Assert.That(c.Channel.Sent.OfType<SetEditModeMessage>().Last().On, Is.True);
            edit.Toggle();
            Assert.That(c.Channel.Sent.OfType<SetEditModeMessage>().Last().On, Is.False);
        }

        [Test]
        public void HoldingCtrlAlt_IsATemporaryEditMode()
        {
            var c = new ConnectionRig();
            var edit = new EditMode(c.Machine);
            c.GoToStreaming(ConnectionRig.Welcome);
            var held = new MouseState { Mode = MouseMode.Screen, Flags = MouseState.FlagCtrlAlt };
            edit.Tick(in held);
            Assert.That(edit.IsOn && edit.Temporary && !edit.Enabled, Is.True);
            Assert.That(c.Channel.Sent.OfType<SetEditModeMessage>().Last().On, Is.True);
            var released = new MouseState { Mode = MouseMode.Edit };
            edit.Tick(in released);
            Assert.That(edit.IsOn, Is.False);
            Assert.That(c.Channel.Sent.OfType<SetEditModeMessage>().Last().On, Is.False);
            Assert.That(c.Channel.Sent.OfType<SetEditModeMessage>().Count(), Is.EqualTo(2), "uma mensagem por mudança");
        }

        [Test]
        public void Escape_CancelsADrag_OrLeavesEditMode()
        {
            var store = new WorkspaceStore();
            var bus = new CommandBus(store, new ICommandObserver[0]);
            var manipulation = new ScreenManipulationController(store, bus);
            var edit = new EditMode(null);
            var hotkeys = new HotkeyDispatcher(store, bus, new FixedHeadPose(Pose.Identity), null, edit, manipulation);
            edit.Set(true);
            hotkeys.Handle(new HotkeyMessage(HotkeyAction.Escape, 0));
            Assert.That(edit.IsOn, Is.False, "parado: Esc sai do modo edição");
            hotkeys.Handle(new HotkeyMessage(HotkeyAction.ToggleEditMode, 0));
            Assert.That(edit.IsOn, Is.True, "Ctrl+Alt+E alterna");
        }

        private static (WorkspaceStore store, ScreenManipulationController manipulation, InteractionRouter router, Pointer right, EditMode edit) Rig()
        {
            var store = new WorkspaceStore();
            var bus = new CommandBus(store, new ICommandObserver[0]);
            bus.Send(new CreateScreen(Resolution.Create(2560, 1440), Placement.OnSurface(0f, 0f, 1.3f), 1.28f, 0f, "", ScreenSource.Virtual));
            var manipulation = new ScreenManipulationController(store, bus);
            var head = new FixedHeadPose(Pose.At(store.State.Surface.Center));
            var menu = new WristMenu(store, bus, head);
            var right = new Pointer();
            var edit = new EditMode(null);
            var router = new InteractionRouter(right, NullManipulationInput.Instance, manipulation, menu, null, bus, edit);
            var eye = store.State.Surface.Center;
            var screen = store.State.Surface.ToPose(store.State.Screens[0].Placement).Position;
            right.Next = new ManipulationInput { HasRay = true, Origin = eye, Direction = (screen - eye).Normalized };
            return (store, manipulation, router, right, edit);
        }

        [Test]
        public void OutsideEditMode_LasersDoNotGrabScreens()
        {
            var (_, manipulation, router, right, _) = Rig();
            router.Tick();
            right.Next.SelectHeld = true;
            router.Tick();
            Assert.That(manipulation.IsDragging, Is.False);
            Assert.That(manipulation.State, Is.EqualTo(ManipulationState.Idle), "sem hover fora da edição");
        }

        [Test]
        public void InEditMode_LasersGrab_AndTurningItOffCancelsTheDrag()
        {
            var (store, manipulation, router, right, edit) = Rig();
            var before = store.State.Screens[0].Placement;
            edit.Set(true);
            router.Tick();
            right.Next.SelectHeld = true;
            router.Tick();
            Assert.That(manipulation.IsDragging, Is.True);
            right.Next.Direction = WorldDirection(20f);
            router.Tick();
            edit.Set(false);
            router.Tick();
            Assert.That(manipulation.IsDragging, Is.False);
            Assert.That(store.State.Screens[0].Placement, Is.EqualTo(before), "cancelado: nada aplicado");
        }

        private static Vec3 WorldDirection(float azDeg) =>
            new Vec3((float)System.Math.Sin(azDeg * System.Math.PI / 180), 0f, (float)System.Math.Cos(azDeg * System.Math.PI / 180));
    }
}
