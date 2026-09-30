namespace ScreenPortal.Host.Domain.Display;

public readonly record struct RectI(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

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
}
