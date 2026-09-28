using System;
using System.Collections.Generic;
using ScreenPortal.Domain.Commands;

namespace ScreenPortal.App
{
    public sealed class CommandBus : ICommandBus
    {
        private readonly WorkspaceStore _store;
        private readonly ICommandObserver[] _observers;

        public CommandBus(WorkspaceStore store, IEnumerable<ICommandObserver> observers)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _observers = observers == null ? new ICommandObserver[0] : new List<ICommandObserver>(observers).ToArray();
        }

        public CommandResult Send(ICommand command)
        {
            var result = _store.Dispatch(command);
            for (int i = 0; i < _observers.Length; i++) _observers[i].OnCommandProcessed(command, result);
            return result;
        }

        public bool Undo() => _store.Undo();
        public bool Redo() => _store.Redo();
    }
}
