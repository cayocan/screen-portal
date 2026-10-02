using System.Runtime.InteropServices;
using ScreenPortal.Host.Domain.Display;

namespace ScreenPortal.Host.App.Display;

/// <summary>
/// Posição e forma do cursor do Windows (spec 3B §3): GetCursorInfo a cada leitura; a forma (GetIconInfo/GetDIBits →
/// <see cref="CursorBitmap"/>) só quando o HCURSOR muda — guardada por handle. Coordenadas físicas (PerMonitorV2).
/// </summary>
public sealed class CursorCapture
{
    private readonly Dictionary<IntPtr, CursorImage?> _shapes = new();

    /// <returns>false se o Windows não respondeu. <paramref name="shape"/> null = cursor escondido.</returns>
    public bool Read(out int x, out int y, out CursorImage? shape)
    {
        var info = new CursorInfo { cbSize = Marshal.SizeOf<CursorInfo>() };
        x = y = 0;
        shape = null;
        if (!GetCursorInfo(ref info)) return false;
        x = info.ptScreenPos.X;
        y = info.ptScreenPos.Y;
        if ((info.flags & CursorShowing) == 0 || info.hCursor == IntPtr.Zero) return true;
        if (!_shapes.TryGetValue(info.hCursor, out shape))
        {
            shape = Extract(info.hCursor);
            if (_shapes.Count > 256) _shapes.Clear(); // apps que criam cursores sem parar
            _shapes[info.hCursor] = shape;
        }
        return true;
    }

    /// <summary>Diagnóstico (cursor-probe): um cursor do sistema pelo IDC_* (32512 = seta, 32513 = I-beam…).</summary>
    public static CursorImage? ExtractSystem(int idc) => Extract(LoadCursor(IntPtr.Zero, idc));

    private static CursorImage? Extract(IntPtr cursor)
    {
        if (!GetIconInfo(cursor, out var icon)) return null;
        IntPtr dc = GetDC(IntPtr.Zero);
        try
        {
            if (icon.hbmColor != IntPtr.Zero)
            {
                GetObject(icon.hbmColor, Marshal.SizeOf<Bitmap>(), out var bm);
                int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
                var color = Bits(dc, icon.hbmColor, w, h, 32);
                var mask = icon.hbmMask != IntPtr.Zero ? Bits(dc, icon.hbmMask, w, h, 1) : null;
                return color == null ? null : CursorBitmap.FromColor(w, h, color, mask, icon.xHotspot, icon.yHotspot);
            }
            if (icon.hbmMask == IntPtr.Zero) return null;
            GetObject(icon.hbmMask, Marshal.SizeOf<Bitmap>(), out var mb);
            int mw = mb.bmWidth, mh = Math.Abs(mb.bmHeight); // AND em cima, XOR embaixo: altura dobrada
            var bits = Bits(dc, icon.hbmMask, mw, mh, 1);
            return bits == null ? null : CursorBitmap.FromMonochrome(mw, mh / 2, bits, icon.xHotspot, icon.yHotspot);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
            if (icon.hbmColor != IntPtr.Zero) DeleteObject(icon.hbmColor);
            if (icon.hbmMask != IntPtr.Zero) DeleteObject(icon.hbmMask);
        }
    }

    private static byte[]? Bits(IntPtr dc, IntPtr bitmap, int w, int h, short bpp)
    {
        int stride = bpp == 32 ? w * 4 : (w + 31) / 32 * 4;
        var bits = new byte[stride * h];
        var info = new BitmapInfo
        {
            biSize = Marshal.SizeOf<BitmapInfoHeader>(), biWidth = w, biHeight = -h, // negativo = de cima para baixo
            biPlanes = 1, biBitCount = bpp, biCompression = 0,
        };
        return GetDIBits(dc, bitmap, 0, (uint)h, bits, ref info, 0) == h ? bits : null;
    }

    private const int CursorShowing = 1;

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo { public int cbSize; public int flags; public IntPtr hCursor; public Point ptScreenPos; }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo { public bool fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap { public int bmType, bmWidth, bmHeight, bmWidthBytes; public short bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader { public int a, b, c; public short d, e; public int f, g, h, i, j, k; }

    // BITMAPINFOHEADER + 2 entradas de cor (1 bpp precisa delas; 32 bpp ignora).
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        public uint color0, color1;
    }

    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll", EntryPoint = "LoadCursorW")] private static extern IntPtr LoadCursor(IntPtr instance, int id);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr obj, int size, out Bitmap bitmap);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfo info, uint usage);
}
