using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Base para comandos que transformam uma tela existente.</summary>
    public abstract class ScreenCommand : ICommand
    {
        public ScreenId ScreenId { get; }
        public virtual bool IsUndoable => true;

        protected ScreenCommand(ScreenId screenId) => ScreenId = screenId;

        protected abstract VirtualScreen Transform(VirtualScreen screen);

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            if (!state.TryGetScreen(ScreenId, out var screen))
            {
                next = state;
                return CommandResult.Rejected(ScreenId + " não existe.");
            }
            next = state.WithScreenReplaced(Transform(screen));
            return CommandResult.Ok;
        }

        public override string ToString() => GetType().Name + "(" + ScreenId + ")";
    }
}
