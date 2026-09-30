using System;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Menu
{
    /// <summary>Telas sem vídeo (spec 2B §5) para testar o layout antes da 2C. Telas normais do domínio.</summary>
    public static class TestScreens
    {
        public const string LabelPrefix = "Teste ";
        private static readonly Resolution[] Cycle =
        {
            Resolution.Create(1920, 1080), Resolution.Create(2560, 1440), Resolution.Create(1440, 2560),
        };

        public static bool IsTestScreen(VirtualScreen screen) =>
            screen.Label.StartsWith(LabelPrefix, StringComparison.Ordinal);

        /// <param name="index">0, 1, 2… → "Teste 1", "Teste 2"…</param>
        public static CreateScreen Next(int index)
        {
            var resolution = Cycle[index % Cycle.Length];
            float width = resolution.Orientation == Orientation.Portrait ? 0.6f : 1f;
            return new CreateScreen(resolution, null, width, 0f, LabelPrefix + (index + 1));
        }
    }
}
