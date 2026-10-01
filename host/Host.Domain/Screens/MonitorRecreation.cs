using ScreenPortal.Host.Domain.Display;

namespace ScreenPortal.Host.Domain.Screens;

/// <summary>Driver de monitor virtual como a recriação o vê (spec 2D §4). <c>Create</c> lança se falhar.</summary>
public interface IVirtualMonitors<TMonitor>
{
    TMonitor Create(int width, int height);
    void Remove(TMonitor monitor);
    /// <summary><c>\\.\DISPLAYn</c> do monitor.</summary>
    string DisplayName(TMonitor monitor);
    /// <summary>Retângulo do monitor no desktop (pixels físicos).</summary>
    RectI Bounds(TMonitor monitor);
    /// <summary>Retângulos dos outros monitores ativos no desktop.</summary>
    IReadOnlyList<RectI> OthersBounds(TMonitor except);
    /// <summary>Move o monitor no arranjo do Windows; false se o Windows recusar.</summary>
    bool MoveTo(TMonitor monitor, int x, int y);
}

/// <summary>Move as janelas de nível superior visíveis de um monitor para outro (spec 2D §4).</summary>
public interface IWindowMover<TMonitor>
{
    /// <returns>Quantas janelas foram movidas.</returns>
    int MoveWindows(TMonitor from, TMonitor to);
}

public sealed record RecreateResult<TMonitor>(bool Ok, TMonitor? Monitor, int WindowsMoved, string Error) where TMonitor : class;

/// <summary>
/// "Recriar com outra resolução" no host (spec 2D §4): cria o monitor novo → move as janelas do antigo para ele → para o
/// stream do antigo e o remove → o aluguel passa ao novo (mesma chave, tamanho novo, sem prazo). Se criar falhar, nada
/// muda. Falha ao mover ou ao remover vai para o log: o novo já está em uso. Não é thread-safe (o ScreenHost chama com
/// o lock).
/// </summary>
public sealed class MonitorRecreator<TMonitor>(IVirtualMonitors<TMonitor> monitors, IWindowMover<TMonitor> windows, Action<string> log)
    where TMonitor : class
{
    public RecreateResult<TMonitor> Recreate(ScreenLeaseTable leases, ScreenLease old, TMonitor oldMonitor, int width, int height,
        Action stopOldStream)
    {
        RectI? oldBounds = null;
        try { oldBounds = monitors.Bounds(oldMonitor); }
        catch (Exception e) { log($"telas: posição de {old.DisplayName} desconhecida: {e.Message}"); }

        TMonitor created;
        try
        {
            created = monitors.Create(width, height);
        }
        catch (Exception e)
        {
            return new RecreateResult<TMonitor>(false, null, 0, "Falha ao criar monitor: " + e.Message);
        }

        int moved = 0;
        try { moved = windows.MoveWindows(oldMonitor, created); }
        catch (Exception e) { log($"telas: falha ao mover as janelas de {old.DisplayName}: {e.Message}"); }

        stopOldStream();
        try { monitors.Remove(oldMonitor); }
        catch (Exception e) { log($"telas: falha ao remover {old.DisplayName}: {e.Message}"); }

        // O novo ocupa o lugar do antigo no arranjo do Windows (o mouse mantém a continuidade entre as telas).
        if (oldBounds is { } bounds)
        {
            try
            {
                var target = MonitorArrangement.Reattach(bounds, width, height, monitors.OthersBounds(created));
                if (!monitors.MoveTo(created, target.X, target.Y))
                    log($"telas: o Windows recusou pôr {monitors.DisplayName(created)} em {target.X},{target.Y}");
            }
            catch (Exception e) { log($"telas: falha ao posicionar {monitors.DisplayName(created)}: {e.Message}"); }
        }

        leases.Add(new ScreenLease(old.DeviceId, old.ScreenKey, ScreenLeaseKind.Virtual, width, height, 0, monitors.DisplayName(created)));
        return new RecreateResult<TMonitor>(true, created, moved, "");
    }
}
