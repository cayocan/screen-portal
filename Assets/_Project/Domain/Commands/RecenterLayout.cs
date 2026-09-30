using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Novo centro e frente (recentrar, §10.12). Placements intactos: o layout inteiro acompanha.</summary>
    public sealed class RecenterLayout : ICommand
    {
        public Vec3 Center { get; }
        public float HeadingDeg { get; }
        public bool IsUndoable => true;

        public RecenterLayout(Vec3 center, float headingDeg)
        {
            Center = center;
            HeadingDeg = headingDeg;
        }

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            next = state.WithSurface(state.Surface.WithCenter(Center, HeadingDeg));
            return CommandResult.Ok;
        }
    }
}
