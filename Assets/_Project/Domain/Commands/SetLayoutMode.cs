using ScreenPortal.Domain.Layout;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>
    /// Troca Cilindro ↔ Esfera sem mover nenhuma tela (brief §10.12): os placements são reexpressos no referencial
    /// novo (importa quando a Esfera tem tilt). Telas soltas ficam intactas. Um passo de undo.
    /// </summary>
    public sealed class SetLayoutMode : ICommand
    {
        public LayoutMode Mode { get; }
        public bool IsUndoable => true;

        public SetLayoutMode(LayoutMode mode) => Mode = mode;

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            var from = state.Surface;
            if (from.Mode == Mode)
            {
                next = state;
                return CommandResult.Rejected("Já está no modo " + Mode + ".");
            }
            var to = from.WithMode(Mode);
            var ws = state.WithSurface(to);
            var screens = state.Screens;
            for (int i = 0; i < screens.Count; i++)
            {
                var screen = screens[i];
                if (screen.Placement.IsLoose) continue;
                var world = from.ToPose(screen.Placement).Position;
                ws = ws.WithScreenReplaced(screen.WithPlacement(to.FromWorldPosition(world)));
            }
            next = ws;
            return CommandResult.Ok;
        }
    }
}
