using ScreenPortal.Host.Domain.Screens;

namespace ScreenPortal.Host.App.Display;

/// <summary>O driver de monitor virtual visto pela recriação (spec 2D §4).</summary>
public sealed class VirtualDisplayMonitors(IVirtualDisplayDriver driver, int refresh) : IVirtualMonitors<VirtualMonitor>
{
    public VirtualMonitor Create(int width, int height) => driver.Create(width, height, refresh);
    public void Remove(VirtualMonitor monitor) => driver.Remove(monitor);
    public string DisplayName(VirtualMonitor monitor) => monitor.Display.DeviceName;
}
