using System.Collections.Generic;
using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class CommandBusTests
    {
        private sealed class RecordingObserver : ICommandObserver
        {
            public readonly List<(ICommand command, CommandResult result)> Seen = new List<(ICommand, CommandResult)>();
            public void OnCommandProcessed(ICommand command, CommandResult result) => Seen.Add((command, result));
        }

        [Test]
        public void Send_DispatchesToStoreAndNotifiesObserversWithResult()
        {
            var store = new WorkspaceStore();
            var observer = new RecordingObserver();
            var bus = new CommandBus(store, new ICommandObserver[] { observer });

            var ok = new CreateScreen(Resolution.Create(1920, 1080), Placement.Default, 1f);
            var bad = new RemoveScreen(new ScreenId(99));
            bus.Send(ok);
            bus.Send(bad);

            Assert.That(store.State.Screens.Count, Is.EqualTo(1));
            Assert.That(observer.Seen.Count, Is.EqualTo(2));
            Assert.That(observer.Seen[0].command, Is.SameAs(ok));
            Assert.That(observer.Seen[0].result.Accepted, Is.True);
            Assert.That(observer.Seen[1].result.Accepted, Is.False);
        }

        [Test]
        public void UndoRedo_GoThroughStore()
        {
            var store = new WorkspaceStore();
            var bus = new CommandBus(store, new ICommandObserver[0]);
            bus.Send(new CreateScreen(Resolution.Create(1920, 1080), Placement.Default, 1f));

            Assert.That(bus.Undo(), Is.True);
            Assert.That(store.State.Screens, Is.Empty);
            Assert.That(bus.Redo(), Is.True);
            Assert.That(store.State.Screens.Count, Is.EqualTo(1));
        }

        [Test]
        public void Journal_KeepsMostRecentEntriesUpToCapacity()
        {
            var journal = new CommandJournal(capacity: 3);
            var store = new WorkspaceStore();
            var bus = new CommandBus(store, new ICommandObserver[] { journal });
            for (int i = 0; i < 5; i++) bus.Send(new SetFocus(new ScreenId(100 + i)));

            Assert.That(journal.Count, Is.EqualTo(3));
            Assert.That(((SetFocus)journal[0].Command).ScreenId, Is.EqualTo(new ScreenId(102)), "o mais antigo mantido");
            Assert.That(((SetFocus)journal[2].Command).ScreenId, Is.EqualTo(new ScreenId(104)));
            Assert.That(journal[2].Result.Accepted, Is.False);
        }
    }
}
