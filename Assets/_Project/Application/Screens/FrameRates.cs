using System;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Screens
{
    /// <summary>
    /// fps pedido e bitrate de uma tela (spec 2F §2, brief §8.8). O modo manual sempre manda; o foco (olhar) só decide em
    /// Auto. Bitrate proporcional ao fps (mesma qualidade por quadro, sem piso: o encoder do host mantém os bits por quadro
    /// fixos e só o Vídeo pede uma base maior); Vídeo tem bitrate maior.
    /// </summary>
    public static class FrameRates
    {
        public const int FocusFps = 60;
        public const int UnfocusedFps = 15;
        public const int EcoFps = 5;
        public const int KbpsAt60 = 20000;
        public const int VideoKbps = 30000;

        public static int RequestedFps(FrameRateMode mode, bool focused)
        {
            switch (mode.Kind)
            {
                case FrameRateKind.Video: return FocusFps;
                case FrameRateKind.Fixed: return mode.FixedFps;
                case FrameRateKind.Eco: return EcoFps;
                default: return focused ? FocusFps : UnfocusedFps;
            }
        }

        public static int Kbps(FrameRateMode mode, int fps)
        {
            int perSixty = mode.Kind == FrameRateKind.Video ? VideoKbps : KbpsAt60;
            return (int)Math.Round(perSixty * fps / 60.0, MidpointRounding.AwayFromZero);
        }

        public static string Name(FrameRateMode mode)
        {
            switch (mode.Kind)
            {
                case FrameRateKind.Video: return "Vídeo";
                case FrameRateKind.Fixed: return "Fixo " + mode.FixedFps;
                case FrameRateKind.Eco: return "Eco";
                default: return "Auto";
            }
        }
    }
}
