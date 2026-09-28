using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Zoom proporcional: só a largura física muda; a resolução NUNCA (brief §10.1).</summary>
    public sealed class ResizeScreen : ScreenCommand
    {
        public float WidthMeters { get; }

        public ResizeScreen(ScreenId screenId, float widthMeters) : base(screenId) => WidthMeters = widthMeters;

        protected override VirtualScreen Transform(VirtualScreen screen) => screen.WithWidth(WidthMeters);
    }
}
