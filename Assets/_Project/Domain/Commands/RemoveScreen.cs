using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    public sealed class RemoveScreen : ICommand
    {
        public ScreenId ScreenId { get; }
        public bool IsUndoable => true;

        public RemoveScreen(ScreenId screenId) => ScreenId = screenId;

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            if (!state.Contains(ScreenId))
            {
                next = state;
                return CommandResult.Rejected(ScreenId + " não existe.");
            }
            next = state.WithScreenRemoved(ScreenId);
            return CommandResult.Ok;
        }

        public override string ToString() => "RemoveScreen(" + ScreenId + ")";
    }
}
