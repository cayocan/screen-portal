using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Curvatura 0..1 (limitada). Nunca altera resolução, orientação nem largura (brief §10.2).</summary>
    public sealed class SetScreenCurvature : ScreenCommand
    {
        public float Curvature { get; }

        public SetScreenCurvature(ScreenId screenId, float curvature) : base(screenId) => Curvature = curvature;

        protected override VirtualScreen Transform(VirtualScreen screen) => screen.WithCurvature(Curvature);
    }
}
