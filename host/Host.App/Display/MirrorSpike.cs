using System.Diagnostics;
using System.Runtime.InteropServices;
using ScreenPortal.Host.Capture;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ScreenPortal.Host.App.Display;

/// <summary>
/// Spike 3D: o espelho (WGC + popups) mostra o menu de contexto no quadro composto? Bloco de Notas, menu por
/// WM_CONTEXTMENU (sem mexer no mouse); compara a região do menu no quadro antes e depois.
/// </summary>
public static class MirrorSpike
{
    private sealed class Probe : ICaptureFrameSink
    {
        private readonly GpuDevice _gpu;
        private ID3D11Texture2D? _staging;
        public volatile int Frames;
        public byte[]? Last;
        public int W, H;
        public Probe(GpuDevice gpu) => _gpu = gpu;

        public void OnFrame(ID3D11Texture2D tex, int width, int height, ulong t)
        {
            _staging ??= _gpu.Device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read,
            });
            _gpu.Context.CopyResource(_staging, tex);
            var map = _gpu.Context.Map(_staging, 0, MapMode.Read);
            var bytes = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
                Marshal.Copy(map.DataPointer + (nint)(y * map.RowPitch), bytes, y * width * 4, width * 4);
            _gpu.Context.Unmap(_staging, 0);
            W = width;
            H = height;
            Last = bytes;
            Frames++;
        }
    }

    public static int Run(GpuDevice gpu)
    {
        static void Log(string m) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {m}");
        var (process, notepad) = WindowOps.StartTestWindow();
        try
        {
            Thread.Sleep(400);
            var r = WindowOps.GetRect(notepad);
            int w = r.Width / 8 * 8, h = r.Height / 8 * 8;
            var wgc = WgcMonitorCapture.ForWindow(gpu, notepad, captureCursor: false);
            using var mirror = new WindowMirrorCapture(gpu, notepad, wgc, w, h);
            var probe = new Probe(gpu);
            mirror.Start(probe);
            Wait(() => probe.Frames > 0, 2000);
            Thread.Sleep(600);
            var before = probe.Last!;
            Log($"antes: média da região {Mean(before, w, 150, 150, 300, 250)}");
            Log($"espelho {w}×{h}: {probe.Frames} quadro(s)");
            var edit = FindWindowEx(notepad, IntPtr.Zero, "Edit", null);
            PostMessage(edit, 0x007B, edit, (IntPtr)(((r.Y + 150) << 16) | ((r.X + 150) & 0xFFFF)));
            var sw = Stopwatch.StartNew();
            int changed = 0;
            while (sw.ElapsedMilliseconds < 2000)
            {
                Thread.Sleep(20);
                changed = Diff(before, probe.Last!, w, 150, 150, 300, 250);
                if (changed > 2000) break;
            }
            Log(changed > 2000
                ? $"✓ menu apareceu no quadro do espelho após ~{sw.ElapsedMilliseconds} ms ({changed} px mudaram na região do menu)"
                : $"✗ menu não apareceu no quadro ({changed} px mudaram)");
            Log($"com menu: média da região {Mean(probe.Last!, w, 150, 150, 300, 250)}");
            var open = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "#32768", null);
            PostMessage(open, 0x0100, (IntPtr)0x1B, IntPtr.Zero); // WM_KEYDOWN Esc no menu
            Thread.Sleep(400);
            var closed = Diff(before, probe.Last!, w, 150, 150, 300, 250);
            var menu = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "#32768", null);
            Log($"menu #32768 ainda visível no Windows? {(menu != IntPtr.Zero && IsWindowVisible(menu))}");
            Log($"depois: média da região {Mean(probe.Last!, w, 150, 150, 300, 250)}");
            Log($"menu fechado: {closed} px ainda diferentes do quadro inicial; {probe.Frames} quadro(s) no total");
        }
        finally
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
        }
        return 0;
    }

    private static int Diff(byte[] a, byte[] b, int width, int x0, int y0, int w, int h)
    {
        int n = 0;
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                int i = (y * width + x) * 4;
                if (i + 2 >= a.Length) continue;
                if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) > 30) n++;
            }
        return n;
    }

    private static string Mean(byte[] a, int width, int x0, int y0, int w, int h)
    {
        long b = 0, g = 0, r = 0, n = 0;
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                int i = (y * width + x) * 4;
                b += a[i]; g += a[i + 1]; r += a[i + 2]; n++;
            }
        return $"rgb({r / n},{g / n},{b / n})";
    }

    private static void Wait(Func<bool> done, int ms)
    {
        var sw = Stopwatch.StartNew();
        while (!done() && sw.ElapsedMilliseconds < ms) Thread.Sleep(10);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
}
