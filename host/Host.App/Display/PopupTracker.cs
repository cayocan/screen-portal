using System.Runtime.InteropServices;
using System.Text;
using ScreenPortal.Host.Domain.Display;

namespace ScreenPortal.Host.App.Display;

/// <summary>
/// Popups de uma janela espelhada (ADR 0018): menus, dropdowns, tooltips e diálogos do mesmo processo, que o WGC do
/// Windows 10 não captura. Detectados por <c>SetWinEventHook</c> (mostrar/esconder) e capturados com
/// <c>PrintWindow(PW_RENDERFULLCONTENT)</c> a ~30 fps enquanto visíveis (CPU; são pequenos). <see cref="Snapshot"/> é
/// imutável (trocado inteiro a cada captura).
/// </summary>
public sealed class PopupTracker : IDisposable
{
    public sealed record Popup(IntPtr Hwnd, RectI Rect, int Width, int Height, byte[] Bgra, int Version);

    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(33);
    private const uint EventSystemMenuPopupStart = 0x0006, EventSystemMenuPopupEnd = 0x0007;
    private const uint EventObjectDestroy = 0x8001, EventObjectShow = 0x8002, EventObjectHide = 0x8003;

    private readonly IntPtr _main;
    private readonly uint _pid;
    private readonly uint _mainThread;
    private readonly HashSet<IntPtr> _candidates = new();
    private readonly object _lock = new();
    private readonly Thread _hookThread;
    private readonly Thread _captureThread;
    private readonly WinEventProc _proc; // referência viva para o GC
    private readonly Dictionary<IntPtr, Popup> _last = new();
    private volatile bool _stop;
    private uint _hookThreadId;
    private int _version;

    /// <summary>Popups visíveis agora (vazio = nenhum).</summary>
    public IReadOnlyList<Popup> Snapshot { get; private set; } = Array.Empty<Popup>();

    /// <summary>Muda a cada captura (o compositor refaz o quadro quando muda).</summary>
    public int Version => Volatile.Read(ref _version);

    public PopupTracker(IntPtr mainWindow)
    {
        _main = mainWindow;
        _mainThread = GetWindowThreadProcessId(mainWindow, out _pid);
        _proc = OnEvent;
        _hookThread = new Thread(RunHooks) { IsBackground = true, Name = "PopupHooks" };
        _hookThread.Start();
        _captureThread = new Thread(RunCapture) { IsBackground = true, Name = "PopupCapture" };
        _captureThread.Start();
    }

    private void RunHooks()
    {
        _hookThreadId = GetCurrentThreadId();
        var a = SetWinEventHook(EventSystemMenuPopupStart, EventSystemMenuPopupEnd, IntPtr.Zero, _proc, _pid, 0, 0);
        var b = SetWinEventHook(EventObjectDestroy, EventObjectHide, IntPtr.Zero, _proc, _pid, 0, 0);
        while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
        UnhookWinEvent(a);
        UnhookWinEvent(b);
    }

    private void OnEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || hwnd == IntPtr.Zero || hwnd == _main) return; // só a janela inteira (OBJID_WINDOW)
        lock (_lock)
        {
            if (ev == EventObjectShow || ev == EventSystemMenuPopupStart)
            {
                if (GetAncestor(hwnd, 2) == hwnd && IsPopupOfMain(hwnd)) _candidates.Add(hwnd);
            }
            else _candidates.Remove(hwnd); // escondido/destruído/menu fechou
        }
    }

    /// <summary>Popup desta janela: dono (em cadeia) é a janela, ou menu/tooltip da mesma thread dela.</summary>
    private bool IsPopupOfMain(IntPtr hwnd)
    {
        for (IntPtr owner = GetWindow(hwnd, 4), n = 0; owner != IntPtr.Zero && n < 8; owner = GetWindow(owner, 4), n++)
            if (owner == _main) return true;
        if (GetWindowThreadProcessId(hwnd, out _) != _mainThread) return false;
        string cls = ClassOf(hwnd);
        return cls == "#32768" || cls == "tooltips_class32" || cls == "ComboLBox" || cls == "#32770";
    }

    private void RunCapture()
    {
        while (!_stop)
        {
            Thread.Sleep(Period);
            IntPtr[] list;
            lock (_lock)
            {
                if (_candidates.Count == 0 && _last.Count == 0) continue;
                _candidates.RemoveWhere(h => !IsWindow(h) || !IsWindowVisible(h));
                list = _candidates.ToArray();
            }
            var next = new List<Popup>(list.Length);
            foreach (var hwnd in list)
            {
                var popup = Capture(hwnd);
                if (popup != null) next.Add(popup);
            }
            foreach (var gone in _last.Keys.Where(k => !list.Contains(k)).ToList()) _last.Remove(gone);
            Snapshot = next;
            Interlocked.Increment(ref _version);
        }
    }

    private Popup? Capture(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var r)) return null;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0 || w > 4096 || h > 4096) return null;
        _last.TryGetValue(hwnd, out var previous);
        var pixels = previous != null && previous.Width == w && previous.Height == h ? previous.Bgra : new byte[w * h * 4];
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr dc = CreateCompatibleDC(screen);
        var info = new BitmapInfo { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        IntPtr bmp = CreateDIBSection(dc, ref info, 0, out var bits, IntPtr.Zero, 0);
        IntPtr old = SelectObject(dc, bmp);
        try
        {
            if (bmp == IntPtr.Zero || !PrintWindow(hwnd, dc, 2)) return null; // PW_RENDERFULLCONTENT
            GdiFlush();
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255; // GDI não preenche o alfa
        }
        finally
        {
            SelectObject(dc, old);
            if (bmp != IntPtr.Zero) DeleteObject(bmp);
            DeleteDC(dc);
            ReleaseDC(IntPtr.Zero, screen);
        }
        var popup = new Popup(hwnd, new RectI(r.Left, r.Top, w, h), w, h, pixels, (previous?.Version ?? 0) + 1);
        _last[hwnd] = popup;
        return popup;
    }

    private static string ClassOf(IntPtr hwnd)
    {
        var sb = new StringBuilder(64);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public void Dispose()
    {
        _stop = true;
        if (_hookThreadId != 0) PostThreadMessage(_hookThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
        _hookThread.Join(TimeSpan.FromSeconds(1));
        _captureThread.Join(TimeSpan.FromSeconds(1));
    }

    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Msg { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
}
