using System.Runtime.InteropServices;
using ScreenPortal.App.Connection;
using ScreenPortal.Host.Transport.Control;

namespace ScreenPortal.Host.App.Control;

/// <summary>
/// Atalhos globais (spec 3A §5): RegisterHotKey numa thread própria com fila de mensagens. A tela é a do monitor sob o
/// cursor do mouse; sem tela do Quest ali, a mensagem vai com a tela 0 (o headset só faz as ações globais).
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const uint WmHotkey = 0x0312, WmQuit = 0x0012, WmApp = 0x8000, ModNoRepeat = 0x4000, VkEscape = 0x1B;

    private readonly Func<string, (string DeviceId, uint ScreenKey)?> _screenAt;
    private readonly HotkeyRouter _router;
    private readonly Action<string> _log;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private uint _threadId;

    /// <param name="screenAt">nome GDI do monitor → tela de um headset, se for de alguma.</param>
    public HotkeyService(string path, Func<string, (string, uint)?> screenAt, HotkeyRouter router, Action<string> log)
    {
        _screenAt = screenAt;
        _router = router;
        _log = log;
        string? json = null;
        try
        {
            if (File.Exists(path)) json = File.ReadAllText(path);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, HotkeyMap.DefaultsJson());
            }
        }
        catch (Exception e) { log($"atalhos: falha ao ler/criar {path}: {e.Message}"); }
        var map = HotkeyMap.Load(json, log);
        _thread = new Thread(() => Run(map, path)) { IsBackground = true, Name = "Hotkeys" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(2));
    }

    private void Run(Dictionary<HotkeyAction, KeyChord> map, string path)
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // cria a fila da thread antes de alguém postar nela
        _ready.Set();
        int ok = 0;
        foreach (var (action, chord) in map)
        {
            uint mods = chord.Modifiers | (HotkeyMap.Repeats(action) ? 0 : ModNoRepeat);
            if (RegisterHotKey(IntPtr.Zero, (int)action, mods, chord.VirtualKey)) ok++;
            else _log($"atalhos: {HotkeyMap.Format(chord)} ({action}) já é usado por outro programa; troque em {path}");
        }
        _log($"atalhos: {ok} de {map.Count} registrados ({path})");
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WmApp)
            {
                // 3B: Esc só durante o modo edição (fora dele o Esc é do Windows)
                if (msg.wParam != IntPtr.Zero) RegisterHotKey(IntPtr.Zero, (int)HotkeyAction.Escape, ModNoRepeat, VkEscape);
                else UnregisterHotKey(IntPtr.Zero, (int)HotkeyAction.Escape);
                continue;
            }
            if (msg.message != WmHotkey) continue;
            try { OnHotkey((HotkeyAction)(int)msg.wParam); }
            catch (Exception e) { _log($"atalhos: {e.GetType().Name}: {e.Message}"); }
        }
        foreach (var action in map.Keys) UnregisterHotKey(IntPtr.Zero, (int)action);
        UnregisterHotKey(IntPtr.Zero, (int)HotkeyAction.Escape);
    }

    /// <summary>Liga/desliga o Esc global (modo edição do headset).</summary>
    public void SetEscapeActive(bool on)
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WmApp, on ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>3D: o espelho de janela sob o cursor (o roteador sabe; o monitor não diz).</summary>
    public Func<(string DeviceId, uint ScreenKey)?>? ActiveMirror { get; set; }

    private void OnHotkey(HotkeyAction action)
    {
        if (action == HotkeyAction.DetachWindow)
        {
            Detach();
            return;
        }
        if (action == HotkeyAction.ReattachWindow && ActiveMirror?.Invoke() is { } mirror)
        {
            _router.Send(mirror.DeviceId, new HotkeyMessage(action, mirror.ScreenKey));
            return;
        }
        var target = GetCursorPos(out var p) ? _screenAt(MonitorName(p)) : null;
        if (target is { } t)
        {
            _router.Send(t.DeviceId, new HotkeyMessage(action, t.ScreenKey));
            return;
        }
        foreach (var device in _router.Devices) _router.Send(device, new HotkeyMessage(action, 0)); // fora das telas: só ações globais
    }

    /// <summary>Ctrl+Alt+D: a janela ativa vira uma tela-espelho no headset (ADR 0018). Tamanho em múltiplos de 8.</summary>
    private void Detach()
    {
        if (!ScreenPortal.Host.App.Display.WindowMirrorOps.TryForegroundWindow(out var hwnd, out var bounds))
        {
            _log("destacar: nenhuma janela ativa que dê para espelhar");
            return;
        }
        if (!ScreenPortal.Host.App.Display.WindowMirrorOps.TryDetachMessage(hwnd, out var message, out var why))
        {
            _log($"destacar: {why}");
            return;
        }
        var target = GetCursorPos(out var p) ? _screenAt(MonitorName(p)) : null;
        if (target is { } t) _router.Send(t.DeviceId, message!);
        else foreach (var device in _router.Devices) _router.Send(device, message!);
        _log($"destacar: \"{message!.Title}\" {message.Width}×{message.Height}");
    }

    private static string MonitorName(Point p)
    {
        var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
        return GetMonitorInfo(MonitorFromPoint(p, 2), ref info) ? info.szDevice : "";
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public Point pt; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public Rect rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Msg msg, IntPtr hWnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point p, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
}
