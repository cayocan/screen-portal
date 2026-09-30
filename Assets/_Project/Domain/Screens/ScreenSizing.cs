namespace ScreenPortal.Domain.Screens
{
    /// <summary>Tamanho inicial de uma tela nova: mesma densidade para todas as resoluções (spec 2C §3). É só zoom.</summary>
    public static class ScreenSizing
    {
        /// <summary>2560 px ≈ 1,0 m (a 1,3 m do usuário).</summary>
        public const float PixelsPerMeter = 2560f;

        public static float InitialWidthMeters(Resolution resolution) =>
            ScreenLimits.ClampWidth(resolution.Width / PixelsPerMeter);
    }
}
