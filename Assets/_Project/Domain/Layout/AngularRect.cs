namespace ScreenPortal.Domain.Layout
{
    /// <summary>Retângulo de uma tela em ângulos sobre a superfície (encaixe e espaço livre).</summary>
    public readonly struct AngularRect
    {
        public readonly float AzimuthDeg;
        public readonly float ElevationDeg;
        public readonly float HalfWidthDeg;
        public readonly float HalfHeightDeg;

        public AngularRect(float azimuthDeg, float elevationDeg, float halfWidthDeg, float halfHeightDeg)
        {
            AzimuthDeg = azimuthDeg;
            ElevationDeg = elevationDeg;
            HalfWidthDeg = halfWidthDeg;
            HalfHeightDeg = halfHeightDeg;
        }
    }
}
