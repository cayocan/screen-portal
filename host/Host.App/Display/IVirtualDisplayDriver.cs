using System.Diagnostics;

namespace ScreenPortal.Host.App.Display;

public sealed record VirtualMonitor(string Id, DisplayInfo Display);

/// <summary>Driver de monitor virtual (spike 2A). Create devolve o monitor já ativo no desktop.</summary>
public interface IVirtualDisplayDriver : IDisposable
{
    string Name { get; }
    /// <summary>false no VDD: ele só ajusta a quantidade; remove sempre o último.</summary>
    bool CanRemoveSpecific { get; }
    VirtualMonitor Create(int width, int height, int refresh);
    void Remove(VirtualMonitor monitor);
}

internal static class DisplayWait
{
    /// <summary>Espera um display novo (que não estava em <paramref name="before"/>) aparecer.</summary>
    public static DisplayInfo ForNewDisplay(IReadOnlyList<DisplayInfo> before, TimeSpan timeout)
    {
        var known = before.Select(d => d.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var added = Win32Displays.Snapshot().FirstOrDefault(d => !known.Contains(d.DeviceName) && d.HMonitor != IntPtr.Zero);
            if (added != null) return added;
            Thread.Sleep(100);
        }
        throw new TimeoutException("O monitor virtual não apareceu no desktop.");
    }

    public static void ForGone(string deviceName, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (Win32Displays.Snapshot().All(d => !d.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))) return;
            Thread.Sleep(100);
        }
        throw new TimeoutException("O monitor virtual não saiu do desktop.");
    }
}
