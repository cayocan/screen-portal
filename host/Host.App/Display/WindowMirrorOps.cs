using System.Runtime.InteropServices;
using System.Text;
using ScreenPortal.Host.Domain.Display;

namespace ScreenPortal.Host.App.Display;

/// <summary>Win32 da janela espelhada (3D): existe, título, retângulo visível (DWM), restaurar e trazer para a frente.</summary>
public static class WindowMirrorOps
{
    public static bool IsWindow(IntPtr hwnd) => IsWindowNative(hwnd);

    public static string Title(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Retângulo que o WGC captura (sem a borda invisível de redimensionar do Windows 10).</summary>
    public static bool TryBounds(IntPtr hwnd, out RectI bounds)
    {
        bounds = default;
        if (DwmGetWindowAttribute(hwnd, 9, out var r, Marshal.SizeOf<Rect>()) != 0) return false; // EXTENDED_FRAME_BOUNDS
        bounds = new RectI(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    public static void Restore(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, 9); // SW_RESTORE
    }

    /// <summary>
    /// Traz a janela para a frente para os cliques do espelho caírem nela. O Windows só deixa quem recebeu a última
    /// entrada trocar o foco: o host recebe o mouse (hook), e um Alt sintético desbloqueia o SetForegroundWindow.
    /// </summary>
    public static void BringToFront(IntPtr hwnd)
    {
        Restore(hwnd);
        if (GetForegroundWindow() == hwnd) return;
        keybd_event(0x12, 0, 0, UIntPtr.Zero);      // Alt down
        keybd_event(0x12, 0, 0x0002, UIntPtr.Zero); // Alt up
        SetForegroundWindow(hwnd);
        BringWindowToTop(hwnd);
    }

    /// <summary>Mensagem para o headset espelhar a janela: tamanho em múltiplos de 8, mínimo 320×240.</summary>
    public static bool TryDetachMessage(IntPtr hwnd, out ScreenPortal.App.Connection.DetachWindowMessage? message, out string why)
    {
        message = null;
        why = "";
        if (!TryBounds(hwnd, out var bounds))
        {
            why = "sem retângulo";
            return false;
        }
        uint w = (uint)(bounds.Width / 8 * 8), h = (uint)(bounds.Height / 8 * 8);
        if (w < 320 || h < 240)
        {
            why = $"janela pequena demais ({bounds.Width}×{bounds.Height})";
            return false;
        }
        message = new ScreenPortal.App.Connection.DetachWindowMessage((uint)hwnd.ToInt64(), w, h, Title(hwnd));
        return true;
    }

    /// <summary>Cancela o arraste de janela em andamento (o Windows a devolve ao lugar de antes).</summary>
    public static void CancelMoveSize()
    {
        keybd_event(0x1B, 0, 0, UIntPtr.Zero);      // Esc dentro do loop de mover
        keybd_event(0x1B, 0, 0x0002, UIntPtr.Zero);
    }

    /// <summary>Janela ativa que dá para espelhar: visível, de topo, não é a área de trabalho nem a barra de tarefas.</summary>
    public static bool TryForegroundWindow(out IntPtr hwnd, out RectI bounds)
    {
        hwnd = GetForegroundWindow();
        bounds = default;
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd)) return false;
        hwnd = GetAncestor(hwnd, 2); // GA_ROOT
        var cls = new StringBuilder(64);
        GetClassName(hwnd, cls, cls.Capacity);
        string c = cls.ToString();
        if (c is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        Restore(hwnd);
        return TryBounds(hwnd, out bounds);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "IsWindow")] private static extern bool IsWindowNative(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);
}
