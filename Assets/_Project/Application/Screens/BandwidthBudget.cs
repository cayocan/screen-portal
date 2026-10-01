using System.Collections.Generic;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Screens
{
    /// <summary>Pedido de uma tela com vídeo ao orçamento. <see cref="Order"/>: maior = mais recente.</summary>
    public struct RateRequest
    {
        public ScreenId Screen;
        public FrameRateMode Mode;
        public int Fps;
        public int Order;
    }

    /// <summary>
    /// Orçamento de decode/rede (spec 2F §3) com os limites medidos no ADR 0008: no máximo <see cref="MaxAt60"/> telas a
    /// 60 fps e <see cref="MaxScreens"/> telas acima de 5 fps. Para caber, rebaixa na ordem Eco → Auto fora do foco →
    /// Fixo → Vídeo, a mais recente primeiro; a tela em foco nunca. Sem alocação.
    /// </summary>
    public static class BandwidthBudget
    {
        public const int MaxAt60 = 4;
        public const int MaxScreens = 6;
        private const int Reduced60 = 30;

        /// <returns>true = o corte atingiu um modo manual (aviso "Essa configuração pode causar travadas").</returns>
        public static bool Allocate(IReadOnlyList<RateRequest> requests, ScreenId focus, int[] granted)
        {
            int n = requests.Count;
            int at60 = 0;
            for (int i = 0; i < n; i++)
            {
                granted[i] = requests[i].Fps;
                if (granted[i] >= FrameRates.FocusFps) at60++;
            }

            bool warning = false;
            while (at60 > MaxAt60)
            {
                int victim = NextVictim(requests, focus, granted, FrameRates.FocusFps);
                if (victim < 0) break;
                granted[victim] = Reduced60;
                at60--;
                warning |= IsManual(requests[victim].Mode);
            }

            for (int extra = n - MaxScreens; extra > 0; extra--)
            {
                int victim = NextVictim(requests, focus, granted, FrameRates.EcoFps + 1);
                if (victim < 0) break;
                granted[victim] = FrameRates.EcoFps;
                warning |= IsManual(requests[victim].Mode);
            }
            return warning;
        }

        /// <summary>Tela fora do foco, com fps concedido ≥ <paramref name="minFps"/>, de menor prioridade e mais recente.</summary>
        private static int NextVictim(IReadOnlyList<RateRequest> requests, ScreenId focus, int[] granted, int minFps)
        {
            int best = -1;
            for (int i = 0; i < requests.Count; i++)
            {
                if (requests[i].Screen.Equals(focus) || granted[i] < minFps) continue;
                if (best < 0) { best = i; continue; }
                int p = Priority(requests[i].Mode), bp = Priority(requests[best].Mode);
                if (p < bp || p == bp && requests[i].Order > requests[best].Order) best = i;
            }
            return best;
        }

        /// <summary>Ordem de corte: menor = cortado primeiro.</summary>
        private static int Priority(FrameRateMode mode)
        {
            switch (mode.Kind)
            {
                case FrameRateKind.Eco: return 0;
                case FrameRateKind.Fixed: return 2;
                case FrameRateKind.Video: return 3;
                default: return 1;
            }
        }

        private static bool IsManual(FrameRateMode mode) => mode.Kind == FrameRateKind.Fixed || mode.Kind == FrameRateKind.Video;
    }
}
