using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using ScreenPortal.App.Connection;
using ScreenPortal.Host.App.Display;
using ScreenPortal.Host.Transport.Control;
using ScreenPortal.Infrastructure.Protocol;

namespace ScreenPortal.Host.App.Control;

/// <summary>
/// Cursor no headset (spec 3B §2–3): uma thread lê o cursor a ~240 Hz e manda a cada headset o <see cref="MouseState"/>
/// por UDP (na mudança e a cada 200 ms) e cada forma nova uma vez pelo controle. Numa tela do próprio headset:
/// <see cref="MouseMode.Screen"/>; noutro monitor: <see cref="MouseMode.Outside"/>. Fora das telas e no modo edição o
/// estado vem do <see cref="MouseHook"/> (deltas acumulados, botões, roda) para o headset que recebe o mouse.
/// </summary>
public sealed class CursorFeed : ICursorHost, IDisposable
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(4);
    private static readonly TimeSpan KeepAlive = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MonitorRefresh = TimeSpan.FromSeconds(1);

    private sealed class Target
    {
        public required object Owner;
        public required IPEndPoint EndPoint;
        public required Action<ControlMessage> Send;
        public readonly HashSet<uint> SentShapes = new();
        public MouseState Last;
        public bool HasLast;
        public long LastSentTicks;
        public uint Seq;
        public uint LastScreenShape; // forma na última leitura em tela: o cursor 3D usa a mesma no mundo
    }

    private readonly Func<string, (string DeviceId, uint ScreenKey)?> _screenAt;
    private readonly MouseHook _hook;
    private readonly Action<bool> _onEditMode;
    private readonly Action<string> _log;
    private readonly Dictionary<string, Target> _targets = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private readonly UdpClient _udp = new(AddressFamily.InterNetwork);
    private readonly CursorCapture _capture = new();
    private readonly Thread _thread;
    private readonly byte[] _packet = new byte[MouseStateCodec.Size];
    private string? _routerDevice; // o headset que recebe o mouse fora das telas (o último que pediu o cursor)
    private long _monitorsAt;
    private volatile bool _stop;

    /// <param name="onEditMode">modo edição ligou/desligou (o Esc global só vale nele).</param>
    public CursorFeed(Func<string, (string, uint)?> screenAt, MouseHook hook, Action<bool> onEditMode, Action<string> log)
    {
        _screenAt = screenAt;
        _hook = hook;
        _onEditMode = onEditMode;
        _log = log;
        _thread = new Thread(Run) { IsBackground = true, Name = "CursorFeed", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Start(string deviceId, object owner, string address, ushort udpPort, Action<ControlMessage> send)
    {
        if (!IPAddress.TryParse(address, out var ip)) return;
        lock (_lock)
        {
            _targets[deviceId] = new Target { Owner = owner, EndPoint = new IPEndPoint(ip.MapToIPv4(), udpPort), Send = send };
            _routerDevice = deviceId;
            _monitorsAt = 0; // o roteador passa a ver as telas deste headset
        }
        _hook.SetConnected(true);
        _log($"cursor: {deviceId} recebe o cursor em {address}:{udpPort}");
    }

    public void Stop(string deviceId, object owner)
    {
        bool none;
        lock (_lock)
        {
            if (!_targets.TryGetValue(deviceId, out var t) || !ReferenceEquals(t.Owner, owner)) return;
            _targets.Remove(deviceId);
            if (_routerDevice == deviceId) _routerDevice = _targets.Keys.FirstOrDefault();
            _monitorsAt = 0;
            none = _targets.Count == 0;
        }
        if (!none) return;
        _hook.SetConnected(false); // sem headset, o mouse volta inteiro ao Windows
        _onEditMode(false);
    }

    public void Enter(string deviceId, object owner, uint screenKey, int x, int y)
    {
        if (Owns(deviceId, owner)) _hook.Enter(screenKey, x, y);
    }

    public void SetEditMode(string deviceId, object owner, bool on)
    {
        if (!Owns(deviceId, owner)) return;
        _hook.SetEditMode(on);
        _onEditMode(on);
    }

    private bool Owns(string deviceId, object owner)
    {
        lock (_lock)
            return deviceId == _routerDevice && _targets.TryGetValue(deviceId, out var t) && ReferenceEquals(t.Owner, owner);
    }

    private void Run()
    {
        TimeBeginPeriod(1);
        try
        {
            while (!_stop)
            {
                Thread.Sleep(Period);
                try { Tick(); }
                catch (Exception e) { _log($"cursor: {e.GetType().Name}: {e.Message}"); Thread.Sleep(500); }
            }
        }
        finally { TimeEndPeriod(1); }
    }

    /// <summary>Monitores do Windows para o roteador; tela do Quest só as do headset que recebe o mouse.</summary>
    private void RefreshMonitors(string? device)
    {
        var list = new List<RouterMonitor>();
        foreach (var d in Win32Displays.Snapshot())
        {
            uint key = device != null && _screenAt(d.DeviceName) is { } s && s.DeviceId == device ? s.ScreenKey : 0;
            list.Add(new RouterMonitor(d.DeviceName, d.Bounds, key));
        }
        _hook.SetMonitors(list);
    }

    private void Tick()
    {
        Target[] targets;
        string[] devices;
        string? routerDevice;
        long now = Stopwatch.GetTimestamp();
        bool refresh;
        lock (_lock)
        {
            if (_targets.Count == 0) return;
            targets = _targets.Values.ToArray();
            devices = _targets.Keys.ToArray();
            routerDevice = _routerDevice;
            refresh = _monitorsAt == 0 || Stopwatch.GetElapsedTime(_monitorsAt, now) >= MonitorRefresh;
            if (refresh) _monitorsAt = now;
        }
        if (refresh) RefreshMonitors(routerDevice);
        if (!_capture.Read(out int x, out int y, out var shape)) return;
        _hook.Read(out var mode, out uint epoch, out uint exitKey, out int exitX, out int exitY, out int dx, out int dy,
            out int wheel, out byte buttons);
        bool away = mode is MouseMode.World or MouseMode.Edit; // cursor estacionado: a posição do Windows não vale
        var (monitor, left, top) = away ? ("", 0, 0) : MonitorAt(x, y);
        var screen = monitor.Length > 0 ? _screenAt(monitor) : null;
        byte flags = (byte)(((GetAsyncKeyState(0x10) & 0x8000) != 0 ? MouseState.FlagShift : 0)
                            | ((GetAsyncKeyState(0x11) & 0x8000) != 0 && (GetAsyncKeyState(0x12) & 0x8000) != 0 ? MouseState.FlagCtrlAlt : 0));
        for (int i = 0; i < targets.Length; i++)
        {
            var t = targets[i];
            MouseState state;
            if (away && devices[i] == routerDevice)
            {
                state = new MouseState
                {
                    Mode = mode, Epoch = epoch, ScreenKey = exitKey, X = exitX, Y = exitY, Dx = dx, Dy = dy, Wheel = wheel,
                    Buttons = buttons, Flags = flags, ShapeId = t.LastScreenShape,
                };
            }
            else
            {
                bool mine = !away && screen is { } s && s.DeviceId == devices[i];
                state = new MouseState
                {
                    Mode = mine ? MouseMode.Screen : MouseMode.Outside,
                    Epoch = epoch,
                    ScreenKey = mine ? screen!.Value.ScreenKey : 0,
                    X = mine ? x - left : 0,
                    Y = mine ? y - top : 0,
                    ShapeId = shape?.Id ?? 0,
                    Flags = flags,
                };
                if (mine && shape != null)
                {
                    t.LastScreenShape = shape.Id;
                    if (t.SentShapes.Add(shape.Id))
                        t.Send(new CursorShapeMessage(shape.Id, (ushort)shape.Width, (ushort)shape.Height, (ushort)shape.HotX,
                            (ushort)shape.HotY, (byte)shape.Scale, shape.Bgra));
                }
            }
            var l = t.Last;
            bool changed = !t.HasLast || state.Mode != l.Mode || state.Epoch != l.Epoch || state.ScreenKey != l.ScreenKey
                           || state.X != l.X || state.Y != l.Y || state.ShapeId != l.ShapeId || state.Dx != l.Dx || state.Dy != l.Dy
                           || state.Wheel != l.Wheel || state.Buttons != l.Buttons || state.Flags != l.Flags;
            if (!changed && Stopwatch.GetElapsedTime(t.LastSentTicks, now) < KeepAlive) continue;
            state.Seq = ++t.Seq;
            MouseStateCodec.Write(in state, _packet);
            try { _udp.Send(_packet, _packet.Length, t.EndPoint); }
            catch (SocketException) { } // headset saiu da rede: a sessão cai e Stop limpa
            t.Last = state;
            t.HasLast = true;
            t.LastSentTicks = now;
        }
    }

    private static (string Name, int Left, int Top) MonitorAt(int x, int y)
    {
        var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
        var handle = MonitorFromPoint(new Point { X = x, Y = y }, 2);
        return GetMonitorInfo(handle, ref info) ? (info.szDevice, info.rcMonitor.Left, info.rcMonitor.Top) : ("", 0, 0);
    }

    public void Dispose()
    {
        _stop = true;
        _thread.Join(TimeSpan.FromSeconds(1));
        _udp.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public Rect rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point p, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")] private static extern uint TimeBeginPeriod(uint ms);
    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")] private static extern uint TimeEndPeriod(uint ms);
}
