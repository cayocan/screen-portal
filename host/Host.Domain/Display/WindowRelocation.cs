namespace ScreenPortal.Host.Domain.Display;

public readonly record struct RectI(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

public enum WindowShowState { Normal, Maximized, Minimized }

/// <summary>
/// Uma janela como o Windows a descreve: <see cref="Rect"/> na tela (<c>GetWindowRect</c>) e a posição de restauração
/// (<c>rcNormalPosition</c>), em coordenadas de área de trabalho.
/// </summary>
public readonly record struct WindowSnapshot(WindowShowState State, RectI Rect, RectI RestoreRect);

/// <summary>
/// Para onde a janela vai (spec 2D §4). Normal: <see cref="Rect"/> (tela) por <c>SetWindowPos</c>. Maximizada: restaura em
/// <see cref="RestoreRect"/>, ajusta em <see cref="Rect"/> e maximiza. Minimizada: só <see cref="RestoreRect"/> por
/// <c>SetWindowPlacement</c>, continua minimizada.
/// </summary>
public readonly record struct WindowMove(WindowShowState State, RectI Rect, RectI RestoreRect);

/// <summary>
/// "Recriar com outra resolução" (brief §10.4): leva uma janela para o monitor novo mantendo a posição relativa do
/// centro e o tamanho — reduzindo e encaixando se o destino for menor.
/// </summary>
public static class WindowRelocation
{
    public static RectI Map(RectI window, RectI from, RectI to)
    {
        int width = Math.Min(window.Width, to.Width);
        int height = Math.Min(window.Height, to.Height);

        double cx = (window.X + window.Width / 2.0 - from.X) / from.Width;
        double cy = (window.Y + window.Height / 2.0 - from.Y) / from.Height;

        int x = (int)Math.Round(to.X + cx * to.Width - width / 2.0);
        int y = (int)Math.Round(to.Y + cy * to.Height - height / 2.0);
        x = Math.Clamp(x, to.X, to.Right - width);
        y = Math.Clamp(y, to.Y, to.Bottom - height);
        return new RectI(x, y, width, height);
    }

    /// <summary>
    /// Maximizada e minimizada levam a posição de restauração (o retângulo atual é o maximizado ou o ícone).
    /// <paramref name="workspaceX"/>/<paramref name="workspaceY"/>: canto da área de trabalho do monitor principal na tela
    /// (coordenadas de área de trabalho = tela − esse canto).
    /// </summary>
    public static WindowMove Plan(WindowSnapshot window, RectI from, RectI to, int workspaceX, int workspaceY)
    {
        var source = window.State == WindowShowState.Normal
            ? window.Rect
            : Offset(window.RestoreRect, workspaceX, workspaceY);
        var target = Map(source, from, to);
        return new WindowMove(window.State, target, Offset(target, -workspaceX, -workspaceY));
    }

    private static RectI Offset(RectI r, int dx, int dy) => r with { X = r.X + dx, Y = r.Y + dy };
}
