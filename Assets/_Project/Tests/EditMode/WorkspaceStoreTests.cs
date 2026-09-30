using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class WorkspaceStoreTests
    {
        private static readonly Resolution Hd = Resolution.Create(1920, 1080);
        private static CreateScreen Create() => new CreateScreen(Hd, Placement.Default, 1f);

        [Test]
        public void Dispatch_Accepted_ChangesStateAndRaisesEvent()
        {
            var store = new WorkspaceStore();
            Workspace previous = null, current = null;
            store.StateChanged += (p, c) => { previous = p; current = c; };

            var result = store.Dispatch(Create());

            Assert.That(result.Accepted, Is.True);
            Assert.That(store.State.Screens.Count, Is.EqualTo(1));
            Assert.That(previous, Is.SameAs(Workspace.Empty));
            Assert.That(current, Is.SameAs(store.State));
        }

        [Test]
        public void Dispatch_Rejected_KeepsStateAndRaisesNothing()
        {
            var store = new WorkspaceStore();
            int events = 0;
            store.StateChanged += (_, __) => events++;

            var result = store.Dispatch(new ResizeScreen(new ScreenId(7), 2f));

            Assert.That(result.Accepted, Is.False);
            Assert.That(store.State, Is.SameAs(Workspace.Empty));
            Assert.That(events, Is.EqualTo(0));
            Assert.That(store.CanUndo, Is.False);
        }

        [Test]
        public void UndoRedo_RestoresSnapshots()
        {
            var store = new WorkspaceStore();
            store.Dispatch(Create());
            var id = store.State.FocusedScreenId;
            store.Dispatch(new ResizeScreen(id, 2.5f));

            Assert.That(store.Undo(), Is.True);
            Assert.That(store.State.TryGetScreen(id, out var s), Is.True);
            Assert.That(s.WidthMeters, Is.EqualTo(1f));

            Assert.That(store.Redo(), Is.True);
            store.State.TryGetScreen(id, out s);
            Assert.That(s.WidthMeters, Is.EqualTo(2.5f));

            store.Undo();
            store.Undo();
            Assert.That(store.State.Screens, Is.Empty);
            Assert.That(store.Undo(), Is.False);
        }

        [Test]
        public void NewCommand_ClearsRedo()
        {
            var store = new WorkspaceStore();
            store.Dispatch(Create());
            store.Undo();
            Assert.That(store.CanRedo, Is.True);
            store.Dispatch(Create());
            Assert.That(store.CanRedo, Is.False);
        }

        [Test]
        public void NonUndoableCommand_DoesNotEnterHistory()
        {
            var store = new WorkspaceStore();
            store.Dispatch(Create());
            var id = store.State.FocusedScreenId;
            store.Dispatch(new SetFocus(ScreenId.None));
            store.Dispatch(new SetFocus(id));

            Assert.That(store.Undo(), Is.True);
            Assert.That(store.State.Screens, Is.Empty, "o único passo no histórico é o CreateScreen");
        }

        [Test]
        public void History_IsCappedAtMaxHistory()
        {
            var store = new WorkspaceStore();
            store.Dispatch(Create());
            var id = store.State.FocusedScreenId;
            for (int i = 0; i < WorkspaceStore.MaxHistory + 20; i++)
                store.Dispatch(new ResizeScreen(id, 1f + i * 0.01f));

            int undos = 0;
            while (store.Undo()) undos++;
            Assert.That(undos, Is.EqualTo(WorkspaceStore.MaxHistory));
        }
    }
}
