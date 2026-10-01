using System;

namespace ScreenPortal.Domain.Screens
{
    /// <summary>Presets de curvatura (brief §10.2, spec 2E §3), na ordem do menu. Índice 0 = Plana.</summary>
    public static class CurvaturePresets
    {
        public const int Count = 4;
        public const int Flat = 0;
        /// <summary>Quanto um valor pode diferir do preset e ainda ser ele.</summary>
        public const float Tolerance = 0.005f;

        private static readonly float[] Values = { 0f, 0.3f, ScreenLimits.DefaultCurvature, 1f };
        private static readonly string[] Names = { "Plana", "Suave", "Média", "Envolvente" };

        public static float Value(int index) => Values[index];
        public static string Name(int index) => Names[index];

        /// <returns>Índice do preset igual a <paramref name="curvature"/>, ou −1.</returns>
        public static int IndexOf(float curvature)
        {
            for (int i = 0; i < Count; i++)
                if (Math.Abs(Values[i] - curvature) <= Tolerance) return i;
            return -1;
        }
    }
}
