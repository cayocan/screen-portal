namespace ScreenPortal.Domain.Screens
{
    /// <summary>Limites físicos das telas no espaço. Um só lugar para as regras de tamanho e curvatura.</summary>
    public static class ScreenLimits
    {
        public const float MinWidthMeters = 0.2f;
        public const float MaxWidthMeters = 6f;
        /// <summary>Preset "Média" (brief §10.2): valor do primeiro toggle plana → curva.</summary>
        public const float DefaultCurvature = 0.6f;

        public static float ClampWidth(float widthMeters)
        {
            if (float.IsNaN(widthMeters)) return MinWidthMeters;
            if (widthMeters < MinWidthMeters) return MinWidthMeters;
            if (widthMeters > MaxWidthMeters) return MaxWidthMeters;
            return widthMeters;
        }

        public static float Clamp01(float value)
        {
            if (float.IsNaN(value) || value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
