using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ScreenPortal.Host.App.Display;

/// <summary>
/// SudoVDA (SudoMaker, usado pelo Apollo): cria cada monitor com a resolução exata por IOCTL.
/// Watchdog: sem ping periódico o driver remove os monitores — uma thread pinga enquanto o driver estiver aberto.
/// Protocolo: Common/Include/sudovda-ioctl.h (github.com/SudoMaker/SudoVDA).
/// </summary>
public sealed class SudoVdaDriver : IVirtualDisplayDriver
{
    private static readonly Guid InterfaceGuid = new("e5bcc234-1e0c-418a-a0d4-ef8b7501414d");
    private const uint IoctlAdd = 0x222000, IoctlRemove = 0x222004, IoctlGetWatchdog = 0x22200C, IoctlPing = 0x222220, IoctlVersion = 0x2223FC;

    private readonly SafeFileHandle _device;
    private readonly Action<string> _log;
    private readonly Thread _pinger;
    private volatile bool _disposed;

    public string Name => "SudoVDA";
    public bool CanRemoveSpecific => true;

    private SudoVdaDriver(SafeFileHandle device, Action<string> log)
    {
        _device = device;
        _log = log;
        var version = new byte[4];
        if (Control(IoctlVersion, null, version))
            _log($"SudoVDA protocolo {version[0]}.{version[1]}.{version[2]}{(version[3] != 0 ? " (test build)" : "")}");
        var watchdog = new byte[8];
        if (Control(IoctlGetWatchdog, null, watchdog))
            _log($"SudoVDA watchdog: timeout {BitConverter.ToUInt32(watchdog, 0)} s");
        _pinger = new Thread(PingLoop) { IsBackground = true, Name = "sudovda-ping" };
        _pinger.Start();
    }

    public static SudoVdaDriver Open(Action<string> log) => new(OpenDevice(), log);

    public VirtualMonitor Create(int width, int height, int refresh)
    {
        var before = Win32Displays.Snapshot();
        var guid = Guid.NewGuid();
        var input = new byte[56];
        BitConverter.TryWriteBytes(input.AsSpan(0), (uint)width);
        BitConverter.TryWriteBytes(input.AsSpan(4), (uint)height);
        BitConverter.TryWriteBytes(input.AsSpan(8), (uint)refresh);
        guid.TryWriteBytes(input.AsSpan(12));
        System.Text.Encoding.ASCII.GetBytes("ScreenPortal").CopyTo(input, 28);          // DeviceName[14]
        System.Text.Encoding.ASCII.GetBytes(guid.ToString("N")[..13]).CopyTo(input, 42); // SerialNumber[14]
        var output = new byte[12];
        if (!Control(IoctlAdd, input, output))
            throw new InvalidOperationException($"IOCTL_ADD_VIRTUAL_DISPLAY falhou ({Marshal.GetLastWin32Error()}).");
        var display = DisplayWait.ForNewDisplay(before, TimeSpan.FromSeconds(10));
        return new VirtualMonitor(guid.ToString(), display);
    }

    public void Remove(VirtualMonitor monitor)
    {
        var input = new byte[16];
        Guid.Parse(monitor.Id).TryWriteBytes(input);
        if (!Control(IoctlRemove, input, null))
            throw new InvalidOperationException($"IOCTL_REMOVE_VIRTUAL_DISPLAY falhou ({Marshal.GetLastWin32Error()}).");
        DisplayWait.ForGone(monitor.Display.DeviceName, TimeSpan.FromSeconds(10));
    }

    private void PingLoop()
    {
        while (!_disposed)
        {
            Control(IoctlPing, null, null);
            Thread.Sleep(1000);
        }
    }

    private bool Control(uint code, byte[]? input, byte[]? output) =>
        DeviceIoControl(_device, code, input, input?.Length ?? 0, output, output?.Length ?? 0, out _, IntPtr.Zero);

    public void Dispose()
    {
        _disposed = true;
        _device.Dispose();
    }

    private static SafeFileHandle OpenDevice()
    {
        var guid = InterfaceGuid;
        IntPtr set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == new IntPtr(-1)) throw new InvalidOperationException("SetupDiGetClassDevs falhou.");
        try
        {
            var data = new SpDeviceInterfaceData { cbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
            if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, 0, ref data))
                throw new InvalidOperationException("SudoVDA não encontrado (driver não instalado?).");
            SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int required, IntPtr.Zero);
            IntPtr detail = Marshal.AllocHGlobal(required);
            try
            {
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6); // cbSize de SP_DEVICE_INTERFACE_DETAIL_DATA_W
                if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required, out _, IntPtr.Zero))
                    throw new InvalidOperationException("SetupDiGetDeviceInterfaceDetail falhou.");
                string path = Marshal.PtrToStringUni(detail + 4)!;
                var handle = CreateFile(path, GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (handle.IsInvalid) throw new InvalidOperationException($"Não abriu o SudoVDA ({Marshal.GetLastWin32Error()}).");
                return handle;
            }
            finally { Marshal.FreeHGlobal(detail); }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    private const uint DigcfPresent = 0x2, DigcfDeviceInterface = 0x10;
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, FileShareRead = 1, FileShareWrite = 2, OpenExisting = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData { public int cbSize; public Guid InterfaceClassGuid; public uint Flags; public IntPtr Reserved; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string? enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, uint index, ref SpDeviceInterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData data, IntPtr detail, int size, out int required, IntPtr devInfo);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? input, int inputSize, byte[]? output, int outputSize, out int returned, IntPtr overlapped);
}
