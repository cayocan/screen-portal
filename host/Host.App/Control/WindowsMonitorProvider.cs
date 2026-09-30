using System.Runtime.InteropServices;
using ScreenPortal.App.Connection;
using ScreenPortal.Host.Transport.Control;

namespace ScreenPortal.Host.App.Control;

/// <summary>
/// Monitores FÍSICOS do Windows em pixels físicos (o processo é Per-Monitor DPI Aware v2, app.manifest).
/// Id = número do \.\DISPLAYn (estável entre conexões). Monitores virtuais do app ficam de fora (espelhar é só físico).
/// </summary>
public sealed class WindowsMonitorProvider(Func<string, bool>? isVirtual = null) : IMonitorProvider
{
    public IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
        var result = new List<MonitorDescriptor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(monitor, ref info) && isVirtual?.Invoke(info.DeviceName) != true)
            {
                var r = info.Monitor;
                result.Add(new MonitorDescriptor(StableId(info.DeviceName, result.Count), (uint)(r.Right - r.Left), (uint)(r.Bottom - r.Top),
                    info.DeviceName, (info.Flags & MonitorInfoFPrimary) != 0));
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static uint StableId(string deviceName, int index)
    {
        int i = deviceName.Length;
        while (i > 0 && char.IsDigit(deviceName[i - 1])) i--;
        return i < deviceName.Length && uint.TryParse(deviceName.AsSpan(i), out uint n) ? n : (uint)(1000 + index);
    }

    private const uint MonitorInfoFPrimary = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
}
