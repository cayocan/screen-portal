using ScreenPortal.Domain;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Menu
{
    /// <summary>
    /// Página "Curvatura" do menu de pulso (spec 2E §3): os presets, com o atual marcado, e o rótulo da tela em foco na
    /// linha de status. Strings em cache (sem alocar por quadro).
    /// </summary>
    public sealed class CurvatureMenu
    {
        public const string MarkedPrefix = "● ";
        public const string NoFocusStatus = "Curvatura: sem tela em foco";

        private static readonly string[] Marked = BuildMarked();

        private Workspace _statusFor;
        private string _status = string.Empty;

        public static int ItemCount => CurvaturePresets.Count;

        /// <param name="current">Curvatura pedida da tela em foco (a marca segue o valor guardado, não o efetivo).</param>
        public static string Label(int index, float current) =>
            CurvaturePresets.IndexOf(current) == index ? Marked[index] : CurvaturePresets.Name(index);

        public string Status(Workspace ws)
        {
            if (ReferenceEquals(ws, _statusFor)) return _status;
            _statusFor = ws;
            _status = ws.TryGetScreen(ws.FocusedScreenId, out var screen)
                ? CurvedGeometry.Of(screen, ws.Surface).Label()
                : NoFocusStatus;
            return _status;
        }

        private static string[] BuildMarked()
        {
            var marked = new string[CurvaturePresets.Count];
            for (int i = 0; i < marked.Length; i++) marked[i] = MarkedPrefix + CurvaturePresets.Name(i);
            return marked;
        }
    }
}
