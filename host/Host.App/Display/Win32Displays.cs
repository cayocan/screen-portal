using System.Runtime.InteropServices;
using ScreenPortal.Host.Domain.Display;

namespace ScreenPortal.Host.App.Display;

public sealed record DisplayInfo(string DeviceName, string AdapterName, RectI Bounds, bool IsPrimary, IntPtr HMonitor);

/// <summary>Monitores ativos no desktop (pixels físicos; o processo é Per-Monitor DPI Aware v2).</summary>
public static class Win32Displays
{
    public static IReadOnlyList<DisplayInfo> Snapshot()
    {
        var monitors = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hmon, IntPtr hdc, ref Rect r, IntPtr data) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(hmon, ref info)) monitors[info.DeviceName] = hmon;
            return true;
        }, IntPtr.Zero);

        var result = new List<DisplayInfo>();
        var device = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
        for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++, device.cb = Marshal.SizeOf<DisplayDevice>())
        {
            if ((device.StateFlags & AttachedToDesktop) == 0) continue;
            var mode = NewDevMode();
            if (!EnumDisplaySettings(device.DeviceName, EnumCurrentSettings, ref mode)) continue;
            monitors.TryGetValue(device.DeviceName, out var hmon);
            result.Add(new DisplayInfo(device.DeviceName, device.DeviceString,
                new RectI(mode.dmPositionX, mode.dmPositionY, (int)mode.dmPelsWidth, (int)mode.dmPelsHeight),
                (device.StateFlags & PrimaryDevice) != 0, hmon));
        }
        return result;
    }

    /// <summary>
    /// Estado atual de um display. HMONITOR muda a cada mudança de topologia (criar/remover outro monitor), então
    /// quem vai capturar resolve o handle na hora, nunca usa o guardado na criação.
    /// </summary>
    public static DisplayInfo Current(string deviceName) =>
        Snapshot().FirstOrDefault(d => d.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"{deviceName} não está ativo no desktop.");

    /// <summary>Troca o modo de um monitor (usado só pelo VDD, que não cria monitor com resolução definida).</summary>
    public static bool SetMode(string deviceName, int width, int height, int refresh)
    {
        var mode = NewDevMode();
        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode)) return false;
        mode.dmPelsWidth = (uint)width;
        mode.dmPelsHeight = (uint)height;
        mode.dmDisplayFrequency = (uint)refresh;
        mode.dmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency;
        return ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero) == 0;
    }

    private static DevMode NewDevMode() => new() { dmSize = (ushort)Marshal.SizeOf<DevMode>() };

    private const int EnumCurrentSettings = -1;
    private const uint AttachedToDesktop = 0x1, PrimaryDevice = 0x4;
    private const uint DmPelsWidth = 0x80000, DmPelsHeight = 0x100000, DmDisplayFrequency = 0x400000;
    private const uint CdsUpdateRegistry = 0x1;

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size; public Rect Monitor; public Rect Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    private delegate bool MonitorEnumProc(IntPtr hmon, IntPtr hdc, ref Rect rect, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hmon, ref MonitorInfoEx info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice dd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string device, int mode, ref DevMode dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string device, ref DevMode dm, IntPtr hwnd, uint flags, IntPtr param);
}
