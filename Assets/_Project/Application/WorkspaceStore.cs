using System;
using System.Collections.Generic;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;

namespace ScreenPortal.App
{
    /// <summary>
    /// Fonte única da verdade da workspace (brief §8.2). Aplica Commands e guarda snapshots imutáveis
    /// para undo/redo. Uso só na thread principal da Unity.
    /// </summary>
    public sealed class WorkspaceStore
    {
        public const int MaxHistory = 100;

        private readonly List<Workspace> _undo = new List<Workspace>();
        private readonly List<Workspace> _redo = new List<Workspace>();

        public Workspace State { get; private set; }

        /// <summary>(anterior, atual). Só dispara quando o estado realmente muda.</summary>
        public event Action<Workspace, Workspace> StateChanged;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public WorkspaceStore(Workspace initial = null) => State = initial ?? Workspace.Empty;

        public CommandResult Dispatch(ICommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var result = command.Apply(State, out var next);
            if (!result.Accepted) return result;

            if (command.IsUndoable)
            {
                Push(_undo, State);
                _redo.Clear();
            }
            SetState(next);
            return result;
        }

        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            Push(_redo, State);
            SetState(Pop(_undo));
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;
            Push(_undo, State);
            SetState(Pop(_redo));
            return true;
        }

        private void SetState(Workspace next)
        {
            var previous = State;
            State = next;
            if (!ReferenceEquals(previous, next)) StateChanged?.Invoke(previous, next);
        }

        private static void Push(List<Workspace> stack, Workspace state)
        {
            stack.Add(state);
            if (stack.Count > MaxHistory) stack.RemoveAt(0);
        }

        private static Workspace Pop(List<Workspace> stack)
        {
            var top = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            return top;
        }
    }
}
