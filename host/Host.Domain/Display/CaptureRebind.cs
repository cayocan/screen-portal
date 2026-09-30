namespace ScreenPortal.Host.Domain.Display;

/// <summary>
/// Quando recriar a captura WGC de um monitor (bug 2 da 2C): a sessão antiga fica presa à área do desktop onde o
/// monitor estava e manda quadros pretos ou deslocados depois que ele muda de posição. Mudança de tamanho não entra:
/// resolução nova é RecreateScreen, não religar a captura.
/// </summary>
public static class CaptureRebind
{
    public static bool Needed(RectI capturedAt, RectI now) =>
        capturedAt.Width == now.Width && capturedAt.Height == now.Height
        && (capturedAt.X != now.X || capturedAt.Y != now.Y);
}
