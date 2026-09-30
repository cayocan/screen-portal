using System;

namespace ScreenPortal.App.Menu
{
    /// <summary>Grade do menu de pulso em metros, no espaço do painel (origem no centro, +Y para cima).</summary>
    public static class MenuLayout
    {
        public const int Columns = 2;
        public const float ButtonWidthM = 0.075f;
        public const float ButtonHeightM = 0.03f;
        public const float GapM = 0.006f;
        public const float StatusHeightM = 0.025f;

        public static float PanelWidth => Columns * ButtonWidthM + (Columns + 1) * GapM;

        public static int Rows(int count) => (count + Columns - 1) / Columns;

        public static float PanelHeight(int count) => StatusHeightM + Rows(count) * (ButtonHeightM + GapM) + GapM;

        public static void ButtonCenter(int index, int count, out float x, out float y)
        {
            int column = index % Columns;
            int row = index / Columns;
            float left = -PanelWidth * 0.5f;
            float top = PanelHeight(count) * 0.5f - StatusHeightM;
            x = left + GapM + column * (ButtonWidthM + GapM) + ButtonWidthM * 0.5f;
            y = top - GapM - row * (ButtonHeightM + GapM) - ButtonHeightM * 0.5f;
        }

        /// <returns>Índice do botão, ou −1.</returns>
        public static int ButtonAt(float localX, float localY, int count)
        {
            for (int i = 0; i < count; i++)
            {
                ButtonCenter(i, count, out float x, out float y);
                if (Math.Abs(localX - x) <= ButtonWidthM * 0.5f && Math.Abs(localY - y) <= ButtonHeightM * 0.5f) return i;
            }
            return -1;
        }
    }
}
