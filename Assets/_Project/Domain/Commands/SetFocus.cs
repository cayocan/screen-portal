using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Foco = tela do cursor (brief §10.7). Não entra no undo: é consequência de navegação, não edição.</summary>
    public sealed class SetFocus : ICommand
    {
        public ScreenId ScreenId { get; }
        public bool IsUndoable => false;

        public SetFocus(ScreenId screenId) => ScreenId = screenId;

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            if (!ScreenId.IsNone && !state.Contains(ScreenId))
            {
                next = state;
                return CommandResult.Rejected(ScreenId + " não existe.");
            }
            next = state.WithFocus(ScreenId);
            return CommandResult.Ok;
        }

        public override string ToString() => "SetFocus(" + ScreenId + ")";
    }
}
