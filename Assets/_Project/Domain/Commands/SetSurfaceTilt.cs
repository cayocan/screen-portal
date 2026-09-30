namespace ScreenPortal.Domain.Commands
{
    /// <summary>Ajuste fino do tilt (0°–60°).</summary>
    public sealed class SetSurfaceTilt : ICommand
    {
        public float TiltDeg { get; }
        public bool IsUndoable => true;

        public SetSurfaceTilt(float tiltDeg) => TiltDeg = tiltDeg;

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            next = state.WithSurface(state.Surface.WithTilt(TiltDeg));
            return CommandResult.Ok;
        }
    }
}
