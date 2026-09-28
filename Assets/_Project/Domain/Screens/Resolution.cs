using System;

namespace ScreenPortal.Domain.Screens
{
    /// <summary>
    /// Resolução de um monitor virtual, escolhida na criação e nunca trocada ao vivo (brief §10.1).
    /// Regras (brief §10.3, D5): múltiplos de 8, mínimo 640×480 em qualquer orientação,
    /// máximo 4096 px por dimensão enquanto o MVP usa H.264.
    /// </summary>
    public readonly struct Resolution : IEquatable<Resolution>
    {
        public const int Alignment = 8;
        public const int MinLongSide = 640;
        public const int MinShortSide = 480;
        public const int MaxDimension = 4096;

        public readonly int Width;
        public readonly int Height;

        private Resolution(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public bool IsValid => Width > 0 && Height > 0;
        public Orientation Orientation => Height > Width ? Orientation.Portrait : Orientation.Landscape;
        public float AspectRatio => (float)Width / Height;

        public static bool TryCreate(int width, int height, out Resolution resolution, out string error)
        {
            resolution = default;
            if (width <= 0 || height <= 0)
            {
                error = "Largura e altura devem ser positivas.";
                return false;
            }
            if (width % Alignment != 0 || height % Alignment != 0)
            {
                error = "Largura e altura devem ser múltiplos de 8 (exigência do encoder).";
                return false;
            }
            int longSide = Math.Max(width, height);
            int shortSide = Math.Min(width, height);
            if (longSide < MinLongSide || shortSide < MinShortSide)
            {
                error = "Resolução abaixo do mínimo de 640×480.";
                return false;
            }
            if (longSide > MaxDimension)
            {
                error = "Acima de 4096 px em uma dimensão requer HEVC (v1.1).";
                return false;
            }
            resolution = new Resolution(width, height);
            error = null;
            return true;
        }

        public static Resolution Create(int width, int height)
        {
            if (!TryCreate(width, height, out var resolution, out var error))
                throw new ArgumentException(error);
            return resolution;
        }

        /// <summary>Mesma resolução na outra orientação (as regras são simétricas, então continua válida).</summary>
        public Resolution Rotated() => new Resolution(Height, Width);

        public bool Equals(Resolution other) => Width == other.Width && Height == other.Height;
        public override bool Equals(object obj) => obj is Resolution other && Equals(other);
        public override int GetHashCode() => (Width, Height).GetHashCode();
        public override string ToString() => Width + "×" + Height;
    }
}
