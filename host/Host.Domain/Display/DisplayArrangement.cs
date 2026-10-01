namespace ScreenPortal.Host.Domain.Display;

/// <summary>Uma tela do Quest com monitor no Windows: tamanho em px e onde está na superfície (graus).</summary>
public readonly record struct ArrangedScreen(string Monitor, int Width, int Height, double AzimuthDeg, double ElevationDeg,
    double HalfWidthDeg, double HalfHeightDeg);

/// <summary>Um monitor do Windows agora (para os que não estão no Quest, é onde ficam).</summary>
public readonly record struct WindowsMonitor(string Name, RectI Bounds, bool IsPrimary);

/// <summary>
/// Arranjo do Windows que segue a disposição das telas no Quest (spec 3A §2). O principal fica onde está (0,0); se ele
/// aparece no Quest é a âncora, senão a tela mais central vai à direita dele. Cada tela seguinte (a mais perto, em ângulo,
/// de uma já posta) encosta nessa vizinha do lado onde está no Quest e alinha no outro eixo pelo desnível angular,
/// limitado para continuar encostando. Monitores que não estão no Quest não se movem e são obstáculos: sobreposição empurra
/// a tela para além deles. Puro, sem Windows API.
/// </summary>
public static class DisplayArrangement
{
    private const int MaxPushes = 32;

    /// <returns>Retângulo de cada tela do Quest (chave = <see cref="ArrangedScreen.Monitor"/>).</returns>
    public static IReadOnlyDictionary<string, RectI> Plan(IReadOnlyList<ArrangedScreen> screens, IReadOnlyList<WindowsMonitor> windows,
        string primaryName)
    {
        var result = new Dictionary<string, RectI>(StringComparer.OrdinalIgnoreCase);
        var obstacles = new List<RectI>();
        var inQuest = new HashSet<string>(screens.Select(s => s.Monitor), StringComparer.OrdinalIgnoreCase);
        RectI primary = default;
        foreach (var monitor in windows)
        {
            if (monitor.IsPrimary || monitor.Name.Equals(primaryName, StringComparison.OrdinalIgnoreCase)) primary = monitor.Bounds;
            if (!inQuest.Contains(monitor.Name) || monitor.Name.Equals(primaryName, StringComparison.OrdinalIgnoreCase))
                obstacles.Add(monitor.Bounds);
        }

        var placed = new List<ArrangedScreen>();
        var pending = new List<ArrangedScreen>(screens);
        int primaryIndex = pending.FindIndex(s => s.Monitor.Equals(primaryName, StringComparison.OrdinalIgnoreCase));
        if (primaryIndex >= 0)
        {
            result[primaryName] = primary;
            placed.Add(pending[primaryIndex]);
            pending.RemoveAt(primaryIndex);
        }
        else if (pending.Count > 0)
        {
            int central = 0;
            for (int i = 1; i < pending.Count; i++)
                if (Centrality(pending[i]) < Centrality(pending[central])) central = i;
            var anchor = pending[central];
            var rect = Push(new RectI(primary.Right, primary.Y, anchor.Width, anchor.Height), 1, 0, obstacles);
            result[anchor.Monitor] = rect;
            obstacles.Add(rect);
            placed.Add(anchor);
            pending.RemoveAt(central);
        }

        while (pending.Count > 0)
        {
            int bestPending = 0, bestPlaced = 0;
            double best = double.MaxValue;
            for (int i = 0; i < pending.Count; i++)
            for (int j = 0; j < placed.Count; j++)
            {
                double d = Distance(pending[i], placed[j]);
                if (d >= best) continue;
                best = d;
                bestPending = i;
                bestPlaced = j;
            }
            var screen = pending[bestPending];
            var neighbor = placed[bestPlaced];
            var rect = PlaceNextTo(screen, neighbor, result[neighbor.Monitor], obstacles);
            result[screen.Monitor] = rect;
            obstacles.Add(rect);
            placed.Add(screen);
            pending.RemoveAt(bestPending);
        }
        return result;
    }

    private static RectI PlaceNextTo(ArrangedScreen s, ArrangedScreen n, RectI nb, List<RectI> obstacles)
    {
        double dx = s.AzimuthDeg - n.AzimuthDeg;
        double dy = s.ElevationDeg - n.ElevationDeg; // + = em cima (no Windows, y menor)
        double nx = Math.Abs(dx) / Math.Max(1e-6, s.HalfWidthDeg + n.HalfWidthDeg);
        double ny = Math.Abs(dy) / Math.Max(1e-6, s.HalfHeightDeg + n.HalfHeightDeg);
        double pxPerDegX = nb.Width / Math.Max(1e-6, 2 * n.HalfWidthDeg);
        double pxPerDegY = nb.Height / Math.Max(1e-6, 2 * n.HalfHeightDeg);
        if (nx >= ny)
        {
            int x = dx >= 0 ? nb.Right : nb.X - s.Width;
            double centerY = nb.Y + nb.Height / 2.0 - dy * pxPerDegY;
            int y = Math.Clamp((int)Math.Round(centerY - s.Height / 2.0), nb.Y - s.Height + 1, nb.Bottom - 1);
            return Push(new RectI(x, y, s.Width, s.Height), dx >= 0 ? 1 : -1, 0, obstacles);
        }
        else
        {
            int y = dy >= 0 ? nb.Y - s.Height : nb.Bottom;
            double centerX = nb.X + nb.Width / 2.0 + dx * pxPerDegX;
            int x = Math.Clamp((int)Math.Round(centerX - s.Width / 2.0), nb.X - s.Width + 1, nb.Right - 1);
            return Push(new RectI(x, y, s.Width, s.Height), 0, dy >= 0 ? -1 : 1, obstacles);
        }
    }

    /// <summary>Enquanto sobrepõe alguém, passa para além dele no sentido (sx, sy).</summary>
    private static RectI Push(RectI rect, int sx, int sy, List<RectI> obstacles)
    {
        for (int i = 0; i < MaxPushes; i++)
        {
            int hit = obstacles.FindIndex(o => Overlap(rect, o));
            if (hit < 0) return rect;
            var o = obstacles[hit];
            if (sx > 0) rect = rect with { X = o.Right };
            else if (sx < 0) rect = rect with { X = o.X - rect.Width };
            else if (sy < 0) rect = rect with { Y = o.Y - rect.Height };
            else rect = rect with { Y = o.Bottom };
        }
        return rect;
    }

    private static bool Overlap(RectI a, RectI b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    private static double Centrality(ArrangedScreen s) => Math.Abs(s.AzimuthDeg) + Math.Abs(s.ElevationDeg);

    private static double Distance(ArrangedScreen a, ArrangedScreen b)
    {
        double dx = a.AzimuthDeg - b.AzimuthDeg, dy = a.ElevationDeg - b.ElevationDeg;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
