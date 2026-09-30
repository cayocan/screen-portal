using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Centro = cabeça; frente = yaw do olhar; tilt = inclinação do olhar para cima (0°–60°, só a Esfera usa).</summary>
    public sealed class AlignSurfaceToPosture : ICommand
    {
        public Pose Head { get; }
        public bool IsUndoable => true;

        public AlignSurfaceToPosture(Pose head) => Head = head;

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            var surface = state.Surface
                .WithCenter(Head.Position, LayoutSurface.HeadingOf(Head.Rotation))
                .WithTilt(LayoutSurface.PitchUpOf(Head.Rotation));
            next = state.WithSurface(surface);
            return CommandResult.Ok;
        }
    }
}
