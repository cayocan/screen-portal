using ScreenPortal.App.Screens;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Menu
{
    /// <summary>
    /// Página "Taxa de quadros" do menu de pulso (spec 2F §5): os modos da tela em foco, com o atual marcado, e na linha
    /// de status o modo + fps concedido (ou o aviso do orçamento). Strings em cache: só remonta quando algo muda.
    /// </summary>
    public sealed class FrameRateMenu
    {
        public const string MarkedPrefix = "● ";
        public const string NoFocusStatus = "Taxa de quadros: sem tela em foco";
        public const string WarningStatus = "Essa configuração pode causar travadas";

        private static readonly FrameRateMode[] Modes =
        {
            FrameRateMode.Auto, FrameRateMode.Video, FrameRateMode.Fixed(30), FrameRateMode.Fixed(15), FrameRateMode.Eco,
        };
        private static readonly string[] Names = Build(string.Empty);
        private static readonly string[] Marked = Build(MarkedPrefix);

        private ScreenId _statusScreen = ScreenId.None;
        private FrameRateMode _statusMode;
        private int _statusFps = -2;
        private bool _statusWarning;
        private string _status = NoFocusStatus;

        public static int ItemCount => Modes.Length;

        public static FrameRateMode Mode(int index) => Modes[index];

        public static string Label(int index, bool isCurrent) => isCurrent ? Marked[index] : Names[index];

        public string Status(Workspace ws, RateSync rates)
        {
            if (!ws.TryGetScreen(ws.FocusedScreenId, out var screen)) return NoFocusStatus;
            bool warning = rates != null && rates.Warning;
            int fps = rates != null && rates.TryGetGranted(screen.Id, out int granted) ? granted : -1;
            if (screen.Id.Equals(_statusScreen) && screen.FrameRateMode.Equals(_statusMode) && fps == _statusFps && warning == _statusWarning)
                return _status;
            _statusScreen = screen.Id;
            _statusMode = screen.FrameRateMode;
            _statusFps = fps;
            _statusWarning = warning;
            _status = warning ? WarningStatus
                : FrameRates.Name(screen.FrameRateMode) + (fps > 0 ? " · " + fps + " fps" : string.Empty);
            return _status;
        }

        private static string[] Build(string prefix)
        {
            var labels = new string[Modes.Length];
            for (int i = 0; i < labels.Length; i++) labels[i] = prefix + FrameRates.Name(Modes[i]);
            return labels;
        }
    }
}
