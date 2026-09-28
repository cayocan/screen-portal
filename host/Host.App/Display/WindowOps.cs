using System.Diagnostics;
using System.Runtime.InteropServices;
using ScreenPortal.Host.Domain.Display;

namespace ScreenPortal.Host.App.Display;

/// <summary>Operações de janela para os testes de recriar e de monitor-palco.</summary>
public static class WindowOps
{
    /// <summary>Abre o Bloco de Notas como janela de teste e devolve o HWND.</summary>
    public static (Process Process, IntPtr Hwnd) StartTestWindow()
    {
        var process = Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true })!;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000)
        {
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero) return (process, process.MainWindowHandle);
            Thread.Sleep(100);
        }
        throw new InvalidOperationException("Janela de teste não apareceu.");
    }

    public static RectI GetRect(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var r);
        return new RectI(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    public static void Move(IntPtr hwnd, RectI rect) =>
        SetWindowPos(hwnd, IntPtr.Zero, rect.X, rect.Y, rect.Width, rect.Height, SwpNoZOrder | SwpNoActivate);

    public static IntPtr MonitorOf(IntPtr hwnd) => MonitorFromWindow(hwnd, MonitorDefaultToNull);

    public static IReadOnlyList<IntPtr> WindowsOn(IntPtr hmonitor)
    {
        var result = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && MonitorFromWindow(hwnd, MonitorDefaultToNull) == hmonitor) result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private const uint SwpNoZOrder = 0x4, SwpNoActivate = 0x10, MonitorDefaultToNull = 0;

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
}
