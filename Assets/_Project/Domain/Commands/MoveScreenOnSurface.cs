using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>
    /// Põe a tela num ponto da superfície (spec superfície §4). Sem distância: mantém a atual (ou, se a tela estava
    /// solta, a distância até ela). O arraste manda a distância junto: mover + afastar = um passo de undo.
    /// </summary>
    public sealed class MoveScreenOnSurface : ScreenCommand
    {
        public float AzimuthDeg { get; }
        public float ElevationDeg { get; }
        public float? DistanceM { get; }

        public MoveScreenOnSurface(ScreenId screenId, float azimuthDeg, float elevationDeg, float? distanceM = null) : base(screenId)
        {
            AzimuthDeg = azimuthDeg;
            ElevationDeg = elevationDeg;
            DistanceM = distanceM;
        }

        protected override VirtualScreen Transform(VirtualScreen screen, Workspace state)
        {
            var current = screen.Placement;
            float distance = DistanceM ?? (current.IsLoose
                ? state.Surface.FromWorldPosition(current.LoosePose.Position).DistanceM
                : current.DistanceM);
            return screen.WithPlacement(Placement.OnSurface(AzimuthDeg, ElevationDeg, distance));
        }
    }
}
