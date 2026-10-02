using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ScreenPortal.Host.Capture;
using ScreenPortal.Host.Encode;
using Vortice.Direct3D11;

namespace ScreenPortal.Host.App.Display;

/// <summary>
/// Spike 3D (espelho de janela, caminho 2): no Windows 10 o WGC captura só a janela pedida; menus, dropdowns e diálogos são
/// janelas à parte. Mede: (1) dá para detectar os popups do app por SetWinEventHook? (2) o WGC captura um menu (#32768) e
/// um diálogo (#32770)? (3) quanto tempo do popup aparecer até o 1º quadro capturado? Usa o Bloco de Notas, sem mexer no mouse.
/// </summary>
public static class PopupSpike
{
    private const uint EventObjectShow = 0x8002, EventSystemMenuPopupStart = 0x0006, WinEventOutOfContext = 0;
    private const uint WmContextMenu = 0x007B, WmCommand = 0x0111, WmClose = 0x0010, WmCancelMode = 0x001F;
    private const int IdmFind = 21; // Editar > Localizar no Bloco de Notas

    private sealed class FirstFrame : ICaptureFrameSink
    {
        public readonly Stopwatch Clock = new();
        public volatile int Width, Height;
        public long Ms = -1;
        public void OnFrame(ID3D11Texture2D bgraTexture, int width, int height, ulong captureTimeUs)
        {
            if (Width != 0) return;
            Ms = Clock.ElapsedMilliseconds;
            Width = width;
            Height = height;
        }
    }

    private sealed record Popup(IntPtr Hwnd, string Class, Stopwatch Since);

    public static int Run(GpuDevice gpu)
    {
        static void Log(string m) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {m}");
        var (process, notepad) = WindowOps.StartTestWindow();
        var popups = new System.Collections.Concurrent.ConcurrentQueue<Popup>();
        uint pid = (uint)process.Id;
        WinEventProc proc = (hook, ev, hwnd, idObject, idChild, thread, time) =>
        {
            if (idObject != 0 || hwnd == IntPtr.Zero) return; // OBJID_WINDOW
            if (GetAncestor(hwnd, 2) != hwnd) return;          // só janelas de topo (GA_ROOT)
            popups.Enqueue(new Popup(hwnd, ClassOf(hwnd), Stopwatch.StartNew()));
        };
        var hookThread = new Thread(() =>
        {
            var h = SetWinEventHook(EventSystemMenuPopupStart, EventObjectShow, IntPtr.Zero, proc, pid, 0, WinEventOutOfContext);
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 12000)
            {
                while (PeekMessage(out _, IntPtr.Zero, 0, 0, 1)) { }
                Thread.Sleep(2);
            }
            UnhookWinEvent(h);
        }) { IsBackground = true };
        hookThread.Start();
        Thread.Sleep(500);
        try
        {
            try
            {
                using var main = WgcMonitorCapture.ForWindow(gpu, notepad, captureCursor: false);
                Log($"janela principal: captura OK {main.Width}×{main.Height}");
            }
            catch (Exception e) { Log($"janela principal: WGC recusou ({e.Message.Trim()})"); }
            var edit = FindWindowEx(notepad, IntPtr.Zero, "Edit", null);
            var r = WindowOps.GetRect(notepad);
            Log($"Bloco de Notas {r.Width}×{r.Height} em {r.X},{r.Y}; abrindo o menu de contexto");
            PostMessage(edit, WmContextMenu, edit, (IntPtr)(((r.Y + 150) << 16) | ((r.X + 150) & 0xFFFF)));
            Measure(gpu, popups, "#32768", Log);
            PostMessage(notepad, WmCancelMode, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(400);
            Log("abrindo o diálogo Localizar");
            PostMessage(notepad, WmCommand, (IntPtr)IdmFind, IntPtr.Zero);
            Measure(gpu, popups, "#32770", Log);
        }
        finally
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
        }
        GC.KeepAlive(proc);
        return 0;
    }

    private static void Measure(GpuDevice gpu, System.Collections.Concurrent.ConcurrentQueue<Popup> popups, string wanted,
        Action<string> log)
    {
        var sw = Stopwatch.StartNew();
        Popup? found = null;
        while (sw.ElapsedMilliseconds < 3000 && found == null)
        {
            while (popups.TryDequeue(out var p))
            {
                log($"  evento: janela {p.Class} (0x{p.Hwnd.ToInt64():X})");
                if (p.Class == wanted) found = p;
            }
            Thread.Sleep(5);
        }
        if (found == null)
        {
            log($"  ✗ nenhum {wanted} detectado em 3 s");
            return;
        }
        long detected = found.Since.ElapsedMilliseconds;
        PrintWindowProbe(found.Hwnd, wanted, log);
        try
        {
            var item = Stopwatch.StartNew();
            WgcMonitorCapture? capture = null;
            int tries = 0;
            string last = "";
            while (item.ElapsedMilliseconds < 1500)
            {
                tries++;
                try
                {
                    capture = WgcMonitorCapture.ForWindow(gpu, found.Hwnd, captureCursor: false);
                    if (capture.Width > 0) break;
                    capture.Dispose();
                }
                catch (Exception e) { last = e.Message.Trim(); }
                capture = null;
                Thread.Sleep(10);
            }
            log($"  {tries} tentativa(s) em {item.ElapsedMilliseconds} ms{(capture == null ? " — último erro: " + last : "")}");
            if (capture == null)
            {
                log($"  ✗ {wanted}: item de captura ficou 0×0");
                return;
            }
            using (capture)
            {
                var sink = new FirstFrame();
                sink.Clock.Start();
                capture.Start(sink);
                var wait = Stopwatch.StartNew();
                while (sink.Width == 0 && wait.ElapsedMilliseconds < 2000) Thread.Sleep(2);
                log(sink.Width == 0
                    ? $"  ✗ {wanted}: sem quadro em 2 s"
                    : $"  ✓ {wanted} {sink.Width}×{sink.Height}: detectado +{detected} ms, item {item.ElapsedMilliseconds} ms, " +
                      $"1º quadro {sink.Ms} ms depois de iniciar (total ≈ {found.Since.ElapsedMilliseconds - (wait.ElapsedMilliseconds - sink.Ms)} ms desde o evento)");
            }
        }
        catch (Exception e)
        {
            log($"  ✗ {wanted}: WGC recusou ({e.GetType().Name}: {e.Message})");
        }
    }

    /// <summary>PrintWindow(PW_RENDERFULLCONTENT): aceita janelas possuídas (menus, diálogos). Mede o custo e se veio imagem.</summary>
    private static void PrintWindowProbe(IntPtr hwnd, string what, Action<string> log)
    {
        Thread.Sleep(80); // deixa o popup desenhar
        GetWindowRect(hwnd, out var r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0) { log($"  PrintWindow {what}: retângulo vazio"); return; }
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr dc = CreateCompatibleDC(screen);
        IntPtr bmp = CreateCompatibleBitmap(screen, w, h);
        IntPtr old = SelectObject(dc, bmp);
        try
        {
            var sw = Stopwatch.StartNew();
            const int runs = 20;
            bool ok = true;
            for (int i = 0; i < runs; i++) ok &= PrintWindow(hwnd, dc, 2);
            double ms = sw.Elapsed.TotalMilliseconds / runs;
            // conta pixels distintos de preto numa amostra (imagem vazia = tudo preto)
            int lit = 0, samples = 0;
            for (int y = 2; y < h; y += Math.Max(1, h / 20))
                for (int x = 2; x < w; x += Math.Max(1, w / 20))
                {
                    samples++;
                    if ((GetPixel(dc, x, y) & 0xFFFFFF) != 0) lit++;
                }
            log($"  PrintWindow {what} {w}×{h}: {(ok ? "ok" : "falhou")}, {ms:0.0} ms por captura, {lit}/{samples} amostras com imagem");
        }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(bmp);
            DeleteDC(dc);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect r);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);

    private static string ClassOf(IntPtr hwnd)
    {
        var sb = new StringBuilder(64);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Msg msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x, y; }
}
