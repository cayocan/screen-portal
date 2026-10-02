using System.Runtime.InteropServices;
using ScreenPortal.App.Connection;
using ScreenPortal.Host.Transport.Control;

namespace ScreenPortal.Host.App.Control;

/// <summary>
/// Hook <c>WH_MOUSE_LL</c> numa thread própria (spec 3B §2). O callback só consulta o <see cref="MouseRouter"/> sob um
/// lock curto (sem I/O, sem alocação) e devolve engolir/passar. O <c>SetCursorPos</c> de estacionar é feito no próprio
/// callback (os movimentos seguintes já medem a partir do centro; o eco é ignorado pelo roteador).
/// </summary>
public sealed class MouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const uint WmQuit = 0x0012;
    private const int WmMouseMove = 0x0200, WmLDown = 0x0201, WmLUp = 0x0202, WmRDown = 0x0204, WmRUp = 0x0205,
        WmMDown = 0x0207, WmMUp = 0x0208, WmWheel = 0x020A;

    private readonly MouseRouter _router = new();
    private readonly object _lock = new();
    private readonly Action<string> _log;
    private readonly Thread _thread;
    private readonly HookProc _proc; // referência viva: o GC não pode coletar o delegate do hook
    private readonly ManualResetEventSlim _ready = new();
    private uint _threadId;
    private IntPtr _draggedWindow;

    /// <summary>3D: janela arrastada para fora de uma tela (o CursorFeed a destaca). Zero = nenhuma.</summary>
    public IntPtr TakeDraggedWindow()
    {
        lock (_lock)
        {
            var w = _draggedWindow;
            _draggedWindow = IntPtr.Zero;
            return w;
        }
    }

    public MouseHook(Action<string> log)
    {
        _log = log;
        _proc = Callback;
        _router.IsMovingWindow = () => MovingWindow() != IntPtr.Zero;
        _thread = new Thread(Run) { IsBackground = true, Name = "MouseHook", Priority = ThreadPriority.Highest };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(2));
    }

    /// <summary>Copia o estado do roteador (para o MouseState). Chamado pelo CursorFeed a cada leitura.</summary>
    public void Read(out MouseMode mode, out uint epoch, out uint exitKey, out int exitX, out int exitY, out int dx, out int dy,
        out int wheel, out byte buttons)
    {
        lock (_lock)
        {
            mode = _router.Mode;
            epoch = _router.Epoch;
            exitKey = _router.ExitScreenKey;
            exitX = _router.ExitX;
            exitY = _router.ExitY;
            dx = _router.Dx;
            dy = _router.Dy;
            wheel = _router.Wheel;
            buttons = _router.Buttons;
        }
    }

    public void SetMonitors(IReadOnlyList<RouterMonitor> monitors)
    {
        lock (_lock) _router.SetMonitors(monitors);
    }

    /// <summary>3D: retângulos das janelas espelhadas.</summary>
    public void SetWindows(IReadOnlyList<RouterMonitor> windows)
    {
        lock (_lock) _router.SetWindows(windows);
    }

    /// <summary>3D: espelho em que o cursor está (0 = nenhum) e o retângulo da janela.</summary>
    public uint ReadWindow(out ScreenPortal.Host.Domain.Display.RectI bounds)
    {
        lock (_lock)
        {
            bounds = _router.WindowBounds;
            return _router.WindowKey;
        }
    }

    public void SetConnected(bool connected)
    {
        CursorWarp? warp;
        lock (_lock) warp = _router.SetConnected(connected);
        Warp(warp);
    }

    public void Enter(uint screenKey, int x, int y)
    {
        CursorWarp? warp;
        lock (_lock) warp = _router.Enter(screenKey, x, y);
        Warp(warp);
    }

    public void SetEditMode(bool on)
    {
        CursorWarp? warp;
        lock (_lock) warp = _router.SetEditMode(on);
        Warp(warp);
        _log($"cursor: modo edição {(on ? "ligado" : "desligado")}");
    }

    private static void Warp(CursorWarp? warp)
    {
        if (warp is { } w) SetCursorPos(w.X, w.Y);
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // cria a fila da thread antes de alguém postar nela
        var hook = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(null), 0);
        _ready.Set();
        if (hook == IntPtr.Zero)
        {
            _log($"cursor: o hook do mouse falhou ({Marshal.GetLastWin32Error()}); o cursor não sai das telas");
            return;
        }
        while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { } // só bombeia o hook
        UnhookWindowsHookEx(hook);
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        var info = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
        bool swallow;
        CursorWarp? warp = null;
        lock (_lock)
        {
            switch ((int)wParam)
            {
                case WmMouseMove:
                    swallow = _router.OnMove(info.pt.X, info.pt.Y, out warp);
                    if (_router.TakeLeftDraggingWindow()) _draggedWindow = MovingWindow();
                    break;
                case WmLDown: swallow = _router.OnButton(MouseState.ButtonLeft, true); break;
                case WmLUp: swallow = _router.OnButton(MouseState.ButtonLeft, false); break;
                case WmRDown: swallow = _router.OnButton(MouseState.ButtonRight, true); break;
                case WmRUp: swallow = _router.OnButton(MouseState.ButtonRight, false); break;
                case WmMDown: swallow = _router.OnButton(MouseState.ButtonMiddle, true); break;
                case WmMUp: swallow = _router.OnButton(MouseState.ButtonMiddle, false); break;
                case WmWheel: swallow = _router.OnWheel((short)(info.mouseData >> 16)); break;
                default: swallow = _router.Swallowing; break;
            }
        }
        Warp(warp);
        return swallow ? (IntPtr)1 : CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    public void Dispose()
    {
        SetConnected(false); // devolve o cursor se estava no mundo
        Thread.Sleep(20);
        if (_threadId != 0) PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(1));
        _ready.Dispose();
    }

    /// <summary>A janela que o Windows está movendo/redimensionando agora (loop modal de arraste), ou zero.</summary>
    private static IntPtr MovingWindow()
    {
        var info = new GuiThreadInfo { cbSize = Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(0, ref info)) return IntPtr.Zero;
        return (info.flags & 0x2) != 0 ? info.hwndMoveSize : IntPtr.Zero; // GUI_INMOVESIZE
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public Rect rcCaret;
    }

    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MsLlHookStruct { public Point pt; public uint mouseData, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Msg { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public Point pt; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Msg msg, IntPtr hWnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
}
