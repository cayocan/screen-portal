using System;

namespace ScreenPortal.App.Manipulation
{
    /// <summary>
    /// Mãos ligadas ou desligadas (tchau alterna). Desligadas: sem laser, sem interação e sem modelo; o tracking
    /// continua só para reconhecer o próximo tchau. Controles não dependem disto.
    /// </summary>
    public sealed class HandsMode
    {
        public bool Enabled { get; private set; } = true;

        public event Action<bool> Changed;

        public void Toggle()
        {
            Enabled = !Enabled;
            Changed?.Invoke(Enabled);
        }
    }
}
