using ScreenPortal.Host.Domain.Display;
using ScreenPortal.Host.Domain.Screens;

namespace ScreenPortal.Host.App.Display;

/// <summary>O driver de monitor virtual visto pela recriação (spec 2D §4).</summary>
public sealed class VirtualDisplayMonitors(IVirtualDisplayDriver driver, int refresh) : IVirtualMonitors<VirtualMonitor>
{
    public VirtualMonitor Create(int width, int height) => driver.Create(width, height, refresh);
    public void Remove(VirtualMonitor monitor) => driver.Remove(monitor);
    public string DisplayName(VirtualMonitor monitor) => monitor.Display.DeviceName;
    public RectI Bounds(VirtualMonitor monitor) => Win32Displays.Current(monitor.Display.DeviceName).Bounds;

    public IReadOnlyList<RectI> OthersBounds(VirtualMonitor except) =>
        Win32Displays.Snapshot()
            .Where(d => !d.DeviceName.Equals(except.Display.DeviceName, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Bounds)
            .ToList();

    public bool MoveTo(VirtualMonitor monitor, int x, int y) =>
        DisplayConfigPosition.SetPosition(monitor.Display.DeviceName, x, y) == 0;
}
