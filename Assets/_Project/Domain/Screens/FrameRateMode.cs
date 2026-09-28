using System;

namespace ScreenPortal.Domain.Screens
{
    public enum FrameRateKind
    {
        Auto,
        Fixed,
        Video,
        Eco,
    }

    /// <summary>Modo de fps por tela (brief §8.8). default = Auto.</summary>
    public readonly struct FrameRateMode : IEquatable<FrameRateMode>
    {
        public static readonly FrameRateMode Auto = new FrameRateMode(FrameRateKind.Auto, 0);
        public static readonly FrameRateMode Video = new FrameRateMode(FrameRateKind.Video, 0);
        public static readonly FrameRateMode Eco = new FrameRateMode(FrameRateKind.Eco, 0);

        public readonly FrameRateKind Kind;
        /// <summary>Só para <see cref="FrameRateKind.Fixed"/>; 0 nos demais.</summary>
        public readonly int FixedFps;

        private FrameRateMode(FrameRateKind kind, int fixedFps)
        {
            Kind = kind;
            FixedFps = fixedFps;
        }

        public static FrameRateMode Fixed(int fps)
        {
            if (fps != 15 && fps != 30 && fps != 60 && fps != 90)
                throw new ArgumentOutOfRangeException(nameof(fps), fps, "Fixed aceita 15, 30, 60 ou 90 fps.");
            return new FrameRateMode(FrameRateKind.Fixed, fps);
        }

        public bool Equals(FrameRateMode other) => Kind == other.Kind && FixedFps == other.FixedFps;
        public override bool Equals(object obj) => obj is FrameRateMode other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ FixedFps;
        public override string ToString() => Kind == FrameRateKind.Fixed ? "Fixed(" + FixedFps + ")" : Kind.ToString();
    }
}
