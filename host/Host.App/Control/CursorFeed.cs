using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using ScreenPortal.App.Connection;
using ScreenPortal.Host.App.Display;
using ScreenPortal.Host.Domain.Display;
using ScreenPortal.Host.Transport.Control;
using ScreenPortal.Infrastructure.Protocol;

namespace ScreenPortal.Host.App.Control;

/// <summary>
/// Cursor desenhado no headset (spec 3B §3, bloco 3B-1): uma thread lê o cursor a ~240 Hz e manda a cada headset o
/// <see cref="MouseState"/> por UDP (na mudança e a cada 200 ms) e cada forma nova uma vez pelo controle. O cursor
/// numa tela do próprio headset é <see cref="MouseMode.Screen"/>; em qualquer outro monitor, <see cref="MouseMode.Outside"/>.
/// </summary>
public sealed class CursorFeed : ICursorHost, IDisposable
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(4);
    private static readonly TimeSpan KeepAlive = TimeSpan.FromMilliseconds(200);

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
    }

    private readonly Func<string, (string DeviceId, uint ScreenKey)?> _screenAt;
    private readonly Action<string> _log;
    private readonly Dictionary<string, Target> _targets = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private readonly UdpClient _udp = new(AddressFamily.InterNetwork);
    private readonly CursorCapture _capture = new();
    private readonly Thread _thread;
    private readonly byte[] _packet = new byte[MouseStateCodec.Size];
    private volatile bool _stop;

    public CursorFeed(Func<string, (string, uint)?> screenAt, Action<string> log)
    {
        _screenAt = screenAt;
        _log = log;
        _thread = new Thread(Run) { IsBackground = true, Name = "CursorFeed", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Start(string deviceId, object owner, string address, ushort udpPort, Action<ControlMessage> send)
    {
        if (!IPAddress.TryParse(address, out var ip)) return;
        lock (_lock)
            _targets[deviceId] = new Target { Owner = owner, EndPoint = new IPEndPoint(ip.MapToIPv4(), udpPort), Send = send };
        _log($"cursor: {deviceId} recebe o cursor em {address}:{udpPort}");
    }

    public void Stop(string deviceId, object owner)
    {
        lock (_lock)
            if (_targets.TryGetValue(deviceId, out var t) && ReferenceEquals(t.Owner, owner)) _targets.Remove(deviceId);
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

    private void Tick()
    {
        Target[] targets;
        string[] devices;
        lock (_lock)
        {
            if (_targets.Count == 0) return;
            targets = _targets.Values.ToArray();
            devices = _targets.Keys.ToArray();
        }
        if (!_capture.Read(out int x, out int y, out var shape)) return;
        var (monitor, left, top) = MonitorAt(x, y);
        var screen = monitor.Length > 0 ? _screenAt(monitor) : null;
        long now = Stopwatch.GetTimestamp();
        for (int i = 0; i < targets.Length; i++)
        {
            var t = targets[i];
            bool mine = screen is { } s && s.DeviceId == devices[i];
            var state = new MouseState
            {
                Mode = mine ? MouseMode.Screen : MouseMode.Outside,
                ScreenKey = mine ? screen!.Value.ScreenKey : 0,
                X = mine ? x - left : 0,
                Y = mine ? y - top : 0,
                ShapeId = shape?.Id ?? 0,
            };
            if (mine && shape != null && t.SentShapes.Add(shape.Id))
                t.Send(new CursorShapeMessage(shape.Id, (ushort)shape.Width, (ushort)shape.Height, (ushort)shape.HotX,
                    (ushort)shape.HotY, (byte)shape.Scale, shape.Bgra));
            if (t.HasLast && state.Mode != t.Last.Mode) state.Epoch = t.Last.Epoch + 1;
            else state.Epoch = t.Last.Epoch;
            bool changed = !t.HasLast || state.Mode != t.Last.Mode || state.ScreenKey != t.Last.ScreenKey || state.X != t.Last.X
                           || state.Y != t.Last.Y || state.ShapeId != t.Last.ShapeId;
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
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")] private static extern uint TimeBeginPeriod(uint ms);
    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")] private static extern uint TimeEndPeriod(uint ms);
}
