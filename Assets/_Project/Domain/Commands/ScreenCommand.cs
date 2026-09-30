using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Base para comandos que transformam uma tela existente.</summary>
    public abstract class ScreenCommand : ICommand
    {
        public ScreenId ScreenId { get; }
        public virtual bool IsUndoable => true;

        protected ScreenCommand(ScreenId screenId) => ScreenId = screenId;

        /// <summary>Transformação que só depende da tela.</summary>
        protected virtual VirtualScreen Transform(VirtualScreen screen) => screen;

        /// <summary>Comandos que precisam da superfície sobrescrevem esta.</summary>
        protected virtual VirtualScreen Transform(VirtualScreen screen, Workspace state) => Transform(screen);

        /// <summary>null = válido; senão, o motivo da rejeição.</summary>
        protected virtual string Validate(VirtualScreen screen, Workspace state) => null;

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            if (!state.TryGetScreen(ScreenId, out var screen))
            {
                next = state;
                return CommandResult.Rejected(ScreenId + " não existe.");
            }
            string error = Validate(screen, state);
            if (error != null)
            {
                next = state;
                return CommandResult.Rejected(ScreenId + ": " + error);
            }
            next = state.WithScreenReplaced(Transform(screen, state));
            return CommandResult.Ok;
        }

        public override string ToString() => GetType().Name + "(" + ScreenId + ")";
    }
}
