using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Volta a tela solta ao ponto da superfície mais próximo da pose atual.</summary>
    public sealed class ReturnScreenToSurface : ScreenCommand
    {
        public ReturnScreenToSurface(ScreenId screenId) : base(screenId) { }

        protected override string Validate(VirtualScreen screen, Workspace state) =>
            screen.Placement.IsLoose ? null : "já está na superfície.";

        protected override VirtualScreen Transform(VirtualScreen screen, Workspace state) =>
            screen.WithPlacement(state.Surface.FromWorldPosition(screen.Placement.LoosePose.Position));
    }
}
