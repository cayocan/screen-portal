using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Esfera: distância ao longo da direção. Cilindro: raio horizontal, mantendo a altura.</summary>
    public sealed class SetScreenDistance : ScreenCommand
    {
        public float Meters { get; }

        public SetScreenDistance(ScreenId screenId, float meters) : base(screenId) => Meters = meters;

        protected override string Validate(VirtualScreen screen, Workspace state) =>
            screen.Placement.IsLoose ? "tela solta não está na superfície." : null;

        protected override VirtualScreen Transform(VirtualScreen screen, Workspace state)
        {
            var p = screen.Placement;
            return screen.WithPlacement(state.Surface.Mode == LayoutMode.Sphere
                ? Placement.OnSurface(p.AzimuthDeg, p.ElevationDeg, Meters)
                : CylinderMath.WithHorizontalRadius(p, Meters));
        }
    }
}
