namespace ScreenPortal.Host.Domain.Display;

/// <summary>Pedaço de um popup copiado para o quadro do espelho: origem no bitmap do popup, destino no quadro.</summary>
public readonly record struct PopupBlit(int SrcX, int SrcY, int DstX, int DstY, int Width, int Height);

/// <summary>
/// Espelho de janela (ADR 0018): onde cada popup (menu, dropdown, diálogo) entra no quadro do tamanho da janela.
/// Coordenadas do desktop → do quadro; o que vaza da borda é cortado (spec 3D §2).
/// </summary>
public static class PopupPlacement
{
    /// <param name="window">retângulo da janela no desktop (origem do quadro).</param>
    /// <param name="canvasWidth">largura do quadro (pode ser menor que a janela: múltiplo de 8).</param>
    /// <param name="popup">retângulo do popup no desktop.</param>
    /// <returns>false se nada do popup cai dentro do quadro.</returns>
    public static bool Clip(RectI window, int canvasWidth, int canvasHeight, RectI popup, out PopupBlit blit)
    {
        int dx = popup.X - window.X, dy = popup.Y - window.Y;
        int left = Math.Max(0, dx), top = Math.Max(0, dy);
        int right = Math.Min(canvasWidth, dx + popup.Width), bottom = Math.Min(canvasHeight, dy + popup.Height);
        blit = default;
        if (right <= left || bottom <= top) return false;
        blit = new PopupBlit(left - dx, top - dy, left, top, right - left, bottom - top);
        return true;
    }
}
