using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>
    /// Única forma de mudar a resolução/orientação de uma tela existente: ação explícita do usuário
    /// (brief §10.4). O host recria o monitor virtual; aqui mantemos pose e largura física.
    /// </summary>
    public sealed class RecreateScreen : ICommand
    {
        public ScreenId ScreenId { get; }
        public Resolution Resolution { get; }
        public bool IsUndoable => true;

        public RecreateScreen(ScreenId screenId, Resolution resolution)
        {
            ScreenId = screenId;
            Resolution = resolution;
        }

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            next = state;
            if (!state.TryGetScreen(ScreenId, out var screen)) return CommandResult.Rejected(ScreenId + " não existe.");
            if (!Resolution.IsValid) return CommandResult.Rejected("Resolução inválida.");
            next = state.WithScreenReplaced(screen.WithResolution(Resolution));
            return CommandResult.Ok;
        }

        public override string ToString() => $"RecreateScreen({ScreenId}, {Resolution})";
    }
}
