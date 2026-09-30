using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Tira a tela da superfície (pose livre). Liberdade: toda tela pode sair (ADR 0005).</summary>
    public sealed class SetScreenLoose : ScreenCommand
    {
        public Pose Pose { get; }

        public SetScreenLoose(ScreenId screenId, Pose pose) : base(screenId) => Pose = pose;

        protected override VirtualScreen Transform(VirtualScreen screen) => screen.WithPlacement(Placement.Loose(Pose));
    }
}
