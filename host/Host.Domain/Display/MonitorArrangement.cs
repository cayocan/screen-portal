namespace ScreenPortal.Host.Domain.Display;

/// <summary>
/// Onde pôr o monitor recriado no arranjo do Windows (spec 2D §4): no lugar do antigo, grudado no mesmo vizinho, para o
/// mouse manter a continuidade entre as telas. Mantém a borda que encostava em outro monitor (em cima → a de baixo; à
/// direita → a da esquerda; …); sem vizinho num eixo, mantém o canto superior esquerdo.
/// </summary>
public static class MonitorArrangement
{
    public static RectI Reattach(RectI old, int width, int height, IReadOnlyList<RectI> others)
    {
        bool left = false, right = false, top = false, bottom = false;
        foreach (var o in others)
        {
            bool overlapY = old.Y < o.Bottom && o.Y < old.Bottom;
            bool overlapX = old.X < o.Right && o.X < old.Right;
            if (overlapY && old.X == o.Right) left = true;     // vizinho à esquerda
            if (overlapY && old.Right == o.X) right = true;    // vizinho à direita
            if (overlapX && old.Y == o.Bottom) top = true;     // vizinho em cima
            if (overlapX && old.Bottom == o.Y) bottom = true;  // vizinho embaixo
        }
        int x = !left && right ? old.Right - width : old.X;
        int y = !top && bottom ? old.Bottom - height : old.Y;
        return new RectI(x, y, width, height);
    }
}
