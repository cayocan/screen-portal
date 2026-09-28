using System;
using ScreenPortal.Domain.Commands;

namespace ScreenPortal.App
{
    public readonly struct JournalEntry
    {
        public ICommand Command { get; }
        public CommandResult Result { get; }

        public JournalEntry(ICommand command, CommandResult result)
        {
            Command = command;
            Result = result;
        }
    }

    /// <summary>Buffer circular dos últimos comandos (debug e replay, brief §8.2). Sem alocação por comando.</summary>
    public sealed class CommandJournal : ICommandObserver
    {
        private readonly JournalEntry[] _entries;
        private int _start;

        public int Count { get; private set; }

        public CommandJournal(int capacity = 256)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _entries = new JournalEntry[capacity];
        }

        /// <summary>0 = entrada mais antiga mantida.</summary>
        public JournalEntry this[int index]
        {
            get
            {
                if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
                return _entries[(_start + index) % _entries.Length];
            }
        }

        public void OnCommandProcessed(ICommand command, CommandResult result)
        {
            int end = (_start + Count) % _entries.Length;
            _entries[end] = new JournalEntry(command, result);
            if (Count < _entries.Length) Count++;
            else _start = (_start + 1) % _entries.Length;
        }
    }
}
