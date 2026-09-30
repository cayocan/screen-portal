using System.Runtime.InteropServices;
using ScreenPortal.Host.Domain.Display;
using ScreenPortal.Host.Domain.Screens;

namespace ScreenPortal.Host.App.Display;

/// <summary>
/// Leva as janelas de nível superior visíveis (não "cloaked") do monitor antigo para o novo (spec 2D §4), com posição
/// relativa mantida (<see cref="WindowRelocation.Plan"/>). Maximizada: restaura → move → maximiza. Minimizada: só a
/// posição de restauração, continua minimizada. Os monitores são resolvidos pelo nome na hora (HMONITOR muda a cada
/// mudança de topologia, ADR 0007).
/// </summary>
public sealed class Win32WindowMover(Action<string> log) : IWindowMover<VirtualMonitor>
{
    public int MoveWindows(VirtualMonitor from, VirtualMonitor to)
    {
        var source = Win32Displays.Current(from.Display.DeviceName);
        var target = Win32Displays.Current(to.Display.DeviceName);
        if (source.HMonitor == IntPtr.Zero) return 0; // sem HMONITOR, MonitorFromWindow(NULL) casaria janelas soltas
        var workspace = WorkspaceOrigin();
        int moved = 0;
        foreach (var hwnd in WindowsOn(source.HMonitor))
        {
            try
            {
                if (Move(hwnd, source.Bounds, target.Bounds, workspace)) moved++;
            }
            catch (Exception e)
            {
                log($"telas: janela 0x{hwnd:X} não foi movida: {e.Message}");
            }
        }
        return moved;
    }

    private static bool Move(IntPtr hwnd, RectI from, RectI to, (int X, int Y) workspace)
    {
        var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
        if (!GetWindowPlacement(hwnd, ref placement) || !GetWindowRect(hwnd, out var rect)) return false;
        var state = IsIconic(hwnd) ? WindowShowState.Minimized : IsZoomed(hwnd) ? WindowShowState.Maximized : WindowShowState.Normal;
        var snapshot = new WindowSnapshot(state, ToRectI(rect), ToRectI(placement.NormalPosition));
        var move = WindowRelocation.Plan(snapshot, from, to, workspace.X, workspace.Y);
        switch (move.State)
        {
            case WindowShowState.Normal:
                WindowOps.Move(hwnd, move.Rect);
                return true;
            case WindowShowState.Maximized:
                placement.NormalPosition = FromRectI(move.RestoreRect);
                placement.ShowCmd = SwShowNoActivate;             // restaura já no monitor novo
                if (!SetWindowPlacement(hwnd, ref placement)) return false;
                WindowOps.Move(hwnd, move.Rect);                  // duas chamadas: DPI diferente (ADR 0007)
                ShowWindow(hwnd, SwShowMaximized);
                return true;
            default:
                placement.NormalPosition = FromRectI(move.RestoreRect);
                placement.ShowCmd = SwShowMinNoActive;            // continua minimizada
                return SetWindowPlacement(hwnd, ref placement);
        }
    }

    private static List<IntPtr> WindowsOn(IntPtr hmonitor)
    {
        var result = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && !IsCloaked(hwnd) && MonitorFromWindow(hwnd, MonitorDefaultToNull) == hmonitor) result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    /// <summary>Canto da área de trabalho do monitor principal: origem das coordenadas de <c>rcNormalPosition</c>.</summary>
    private static (int X, int Y) WorkspaceOrigin() =>
        SystemParametersInfo(SpiGetWorkArea, 0, out var work, 0) ? (work.Left, work.Top) : (0, 0);

    private static RectI ToRectI(Rect r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    private static Rect FromRectI(RectI r) => new() { Left = r.X, Top = r.Y, Right = r.Right, Bottom = r.Bottom };

    private const int SwShowMaximized = 3, SwShowNoActivate = 4, SwShowMinNoActive = 7;
    private const uint MonitorDefaultToNull = 0, SpiGetWorkArea = 0x0030, DwmwaCloaked = 14;

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public Point MinPosition;
        public Point MaxPosition;
        public Rect NormalPosition;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, out Rect rect, uint winIni);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out int value, int size);
}
