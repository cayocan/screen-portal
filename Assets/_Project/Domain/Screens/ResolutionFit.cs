using System;

namespace ScreenPortal.Domain.Screens
{
    /// <summary>
    /// Converte o tamanho capturado de um monitor numa <see cref="Resolution"/> válida para o encoder
    /// (≤ 4096 por dimensão, múltiplos de 8). Usado pelo host (tamanho do encode) e pelo headset (tamanho da tela).
    /// </summary>
    public static class ResolutionFit
    {
        public static Resolution FromCapture(int width, int height)
        {
            if (width <= 0 || height <= 0) return default;
            double scale = Math.Min(1.0, (double)Resolution.MaxDimension / Math.Max(width, height));
            // epsilon: 7680 * (4096 / 7680) pode dar 4095,999… em ponto flutuante
            int w = AlignDown((int)Math.Floor(width * scale + 1e-6));
            int h = AlignDown((int)Math.Floor(height * scale + 1e-6));
            return Resolution.TryCreate(w, h, out var r, out _) ? r : default;
        }

        private static int AlignDown(int value) => value - value % Resolution.Alignment;
    }
}
