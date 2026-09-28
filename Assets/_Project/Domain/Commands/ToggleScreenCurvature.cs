using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Plana ↔ última curvatura usada (primeira vez: Média). Atalho Ctrl+Alt+C (brief §10.9).</summary>
    public sealed class ToggleScreenCurvature : ScreenCommand
    {
        public ToggleScreenCurvature(ScreenId screenId) : base(screenId) { }

        protected override VirtualScreen Transform(VirtualScreen screen) =>
            screen.WithCurvature(screen.Curvature > 0f ? 0f : screen.LastNonZeroCurvature);
    }
}
