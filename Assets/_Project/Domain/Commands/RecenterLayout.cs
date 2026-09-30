using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>
    /// Novo centro e frente (recentrar, §10.12). Placements intactos: o layout inteiro acompanha.
    /// O recentrar automático da abertura não entra no undo (<c>undoable: false</c>).
    /// </summary>
    public sealed class RecenterLayout : ICommand
    {
        public Vec3 Center { get; }
        public float HeadingDeg { get; }
        public bool IsUndoable { get; }

        public RecenterLayout(Vec3 center, float headingDeg, bool undoable = true)
        {
            Center = center;
            HeadingDeg = headingDeg;
            IsUndoable = undoable;
        }

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            next = state.WithSurface(state.Surface.WithCenter(Center, HeadingDeg));
            return CommandResult.Ok;
        }
    }
}
