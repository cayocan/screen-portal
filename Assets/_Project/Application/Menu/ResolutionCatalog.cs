using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Menu
{
    /// <summary>
    /// Proporções e resoluções do seletor "+ Monitor virtual" (spec 2C §3; brief §10.3). Todas múltiplas de 8 e
    /// ≤ 4096 (H.264). Retrato inverte. Rótulos em cache: o painel lê a cada quadro sem alocar. Custom fica na 2D.
    /// </summary>
    public static class ResolutionCatalog
    {
        private sealed class Aspect
        {
            public readonly int W;
            public readonly int H;
            public readonly string Landscape;
            public readonly string Portrait;
            public readonly Resolution[] Resolutions;
            public readonly string[] LandscapeLabels;
            public readonly string[] PortraitLabels;

            public Aspect(int w, int h, params Resolution[] resolutions)
            {
                W = w;
                H = h;
                Landscape = w + ":" + h;
                Portrait = h + ":" + w;
                Resolutions = resolutions;
                LandscapeLabels = new string[resolutions.Length];
                PortraitLabels = new string[resolutions.Length];
                for (int i = 0; i < resolutions.Length; i++)
                {
                    LandscapeLabels[i] = resolutions[i].ToString();
                    PortraitLabels[i] = resolutions[i].Rotated().ToString();
                }
            }
        }

        private static readonly Aspect[] Aspects =
        {
            new Aspect(16, 9, R(1920, 1080), R(2560, 1440), R(3840, 2160)),
            new Aspect(16, 10, R(1920, 1200), R(2560, 1600), R(3840, 2400)),
            new Aspect(21, 9, R(2560, 1080), R(3440, 1440)),
            new Aspect(32, 9, R(3840, 1080)),
            new Aspect(1, 1, R(1440, 1440), R(2160, 2160)),
            new Aspect(4, 3, R(1600, 1200), R(2048, 1536)),
        };

        private static Resolution R(int w, int h) => Resolution.Create(w, h);

        public static int AspectCount => Aspects.Length;

        public static string Name(int aspect, Orientation orientation) =>
            orientation == Orientation.Portrait ? Aspects[aspect].Portrait : Aspects[aspect].Landscape;

        /// <summary>Largura/altura do frame de exemplo no menu (gira em retrato).</summary>
        public static float FrameAspect(int aspect, Orientation orientation)
        {
            var a = Aspects[aspect];
            return orientation == Orientation.Portrait ? (float)a.H / a.W : (float)a.W / a.H;
        }

        public static int Count(int aspect) => Aspects[aspect].Resolutions.Length;

        public static Resolution Get(int aspect, int index, Orientation orientation)
        {
            var r = Aspects[aspect].Resolutions[index];
            return orientation == Orientation.Portrait ? r.Rotated() : r;
        }

        public static string Label(int aspect, int index, Orientation orientation) =>
            orientation == Orientation.Portrait ? Aspects[aspect].PortraitLabels[index] : Aspects[aspect].LandscapeLabels[index];
    }
}
