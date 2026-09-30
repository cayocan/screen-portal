using System.Diagnostics;
using System.Net;
using System.Text;
using ScreenPortal.Host.Capture;
using ScreenPortal.Host.Domain.Display;
using ScreenPortal.Host.Encode;
using Vortice.Direct3D11;

namespace ScreenPortal.Host.App.Display;

/// <summary>Os 6 testes do spike do driver (spec 2A §2). Cada teste é isolado: falha em um não impede os outros.</summary>
public static class DisplaySpike
{
    private sealed record Row(string Test, string Result, string Details);

    /// <param name="only">0 = todos; 1..6 = só aquele teste (diagnóstico).</param>
    public static string Run(IVirtualDisplayDriver driver, GpuDevice gpu, Action<string> log, int only = 0)
    {
        var tests = new (string Name, Func<(bool, string)> Body)[]
        {
            ("1. Resolução exata", () => ExactResolutions(driver, log)),
            ("2. Monitores simultâneos", () => Simultaneous(driver, log)),
            ("3. Retrato nativo 1440×2560", () => Portrait(driver, gpu, log)),
            ("4. Recriar movendo janelas", () => Recreate(driver, log)),
            ("5. Monitor-palco com recorte", () => StageCrop(driver, gpu, log)),
            ("6. Limite do H.264 da GPU", () => EncoderLimits(gpu, log)),
        };
        var rows = new List<Row>();
        for (int i = 0; i < tests.Length; i++)
        {
            if (only != 0 && only != i + 1) continue;
            log($"--- {tests[i].Name}");
            rows.Add(Guard(tests[i].Name, tests[i].Body, log));
        }

        var md = new StringBuilder();
        md.AppendLine($"# Fase 2A — spike do driver: {driver.Name}");
        md.AppendLine();
        md.AppendLine($"Data: {DateTime.Now:dd/MM/yyyy HH:mm} · GPU: {gpu.AdapterName} · Windows {Environment.OSVersion.Version}");
        md.AppendLine();
        md.AppendLine("| Teste | Resultado | Detalhes |");
        md.AppendLine("|---|---|---|");
        foreach (var r in rows) md.AppendLine($"| {r.Test} | {r.Result} | {r.Details.Replace("|", "/")} |");
        return md.ToString();
    }

    /// <summary>
    /// Logo após criar um monitor, o WGC rejeita o HMONITOR (E_INVALIDARG) por ~1 s enquanto o DWM assenta a
    /// topologia (medido no spike). Tenta de novo, resolvendo o handle a cada tentativa. A 2C precisa do mesmo.
    /// </summary>
    private static WgcMonitorCapture CaptureMonitor(GpuDevice gpu, string deviceName, Action<string> log)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                var capture = WgcMonitorCapture.ForMonitor(gpu, Win32Displays.Current(deviceName).HMonitor, captureCursor: false);
                log($"captura de {deviceName} pronta após {sw.ElapsedMilliseconds} ms");
                return capture;
            }
            catch (ArgumentException) when (sw.ElapsedMilliseconds < 3000) { Thread.Sleep(100); }
        }
    }

    private static Row Guard(string name, Func<(bool ok, string details)> test, Action<string> log)
    {
        try
        {
            var (ok, details) = test();
            return new Row(name, ok ? "✅" : "❌", details);
        }
        catch (NotSupportedException e) { return new Row(name, "N/A", e.Message); }
        catch (Exception e)
        {
            log(e.ToString());
            return new Row(name, "❌", $"{e.GetType().Name}: {e.Message.Trim()}");
        }
    }

    private static (bool, string) ExactResolutions(IVirtualDisplayDriver driver, Action<string> log)
    {
        var details = new List<string>();
        bool all = true;
        foreach (var (w, h) in new[] { (2560, 1440), (3440, 1440), (1920, 1080) })
        {
            var sw = Stopwatch.StartNew();
            var monitor = driver.Create(w, h, 60);
            long createMs = sw.ElapsedMilliseconds;
            var b = monitor.Display.Bounds;
            bool ok = b.Width == w && b.Height == h;
            all &= ok;
            sw.Restart();
            driver.Remove(monitor);
            details.Add($"{w}×{h}: {(ok ? "exato" : $"veio {b.Width}×{b.Height}")}, cria {createMs} ms, remove {sw.ElapsedMilliseconds} ms");
            log(details[^1]);
        }
        return (all, string.Join("; ", details));
    }

    private static (bool, string) Simultaneous(IVirtualDisplayDriver driver, Action<string> log)
    {
        var created = new List<VirtualMonitor>();
        string stop = "chegou a 8";
        try
        {
            for (int i = 0; i < 8; i++)
            {
                try { created.Add(driver.Create(1920, 1080, 60)); }
                catch (Exception e) { stop = $"falhou no {i + 1}º: {e.Message}"; break; }
                log($"simultâneos: {created.Count}");
            }
        }
        finally
        {
            for (int i = created.Count - 1; i >= 0; i--) driver.Remove(created[i]);
        }
        return (created.Count >= 4, $"{created.Count} simultâneos ({stop})");
    }

    private static (bool, string) Portrait(IVirtualDisplayDriver driver, GpuDevice gpu, Action<string> log)
    {
        var monitor = driver.Create(1440, 2560, 60);
        try
        {
            var b = monitor.Display.Bounds;
            if (b.Width != 1440 || b.Height != 2560) return (false, $"modo veio {b.Width}×{b.Height}");
            using var capture = CaptureMonitor(gpu, monitor.Display.DeviceName, log);
            string dump = Path.Combine(Path.GetTempPath(), "sp-portrait.h264");
            using (var pipeline = new StreamPipeline(gpu, capture, new EncoderSettings(1440, 2560, 30, 8000),
                       new IPEndPoint(IPAddress.Loopback, 9050), 1, dump))
            {
                pipeline.Start();
                Thread.Sleep(3000);
            }
            string probe = Ffprobe(dump);
            log($"retrato: ffprobe {probe}");
            return (probe.Contains("width=1440") && probe.Contains("height=2560"), $"captura {capture.Width}×{capture.Height}; stream {probe}");
        }
        finally { driver.Remove(monitor); }
    }

    private static (bool, string) Recreate(IVirtualDisplayDriver driver, Action<string> log)
    {
        if (!driver.CanRemoveSpecific)
            throw new NotSupportedException("o driver só remove o último monitor; recriar exigiria troca de modo ao vivo (evitada pelo brief §10.1)");

        var (process, hwnd) = WindowOps.StartTestWindow();
        var a = driver.Create(1920, 1080, 60);
        try
        {
            var start = new RectI(a.Display.Bounds.X + 480, a.Display.Bounds.Y + 270, 960, 540);
            WindowOps.Move(hwnd, start);
            Thread.Sleep(300);

            var b = driver.Create(2560, 1440, 60);
            foreach (var w in WindowOps.WindowsOn(Win32Displays.Current(a.Display.DeviceName).HMonitor))
                WindowOps.Move(w, WindowRelocation.Map(WindowOps.GetRect(w), a.Display.Bounds, b.Display.Bounds));
            driver.Remove(a);
            Thread.Sleep(500);

            // Remover A reorganiza o desktop (B desliza) e a troca de DPI pode redimensionar a janela: compara o centro
            // relativo ao monitor, com os limites ATUAIS de B.
            var bNow = Win32Displays.Current(b.Display.DeviceName);
            var actual = WindowOps.GetRect(hwnd);
            var startRel = RelativeCenter(start, a.Display.Bounds);
            var actualRel = RelativeCenter(actual, bNow.Bounds);
            bool onB = WindowOps.MonitorOf(hwnd) == bNow.HMonitor;
            bool near = Math.Abs(actualRel.x - startRel.x) <= 0.02 && Math.Abs(actualRel.y - startRel.y) <= 0.02;
            driver.Remove(b);
            log($"recriar: no monitor novo={onB}, centro relativo esperado {startRel}, real {actualRel}, janela {actual}");
            return (onB && near, $"janela no monitor novo: {onB}; centro relativo esperado ({startRel.x:F2}, {startRel.y:F2}), " +
                                 $"real ({actualRel.x:F2}, {actualRel.y:F2}); tamanho {start.Width}×{start.Height} → {actual.Width}×{actual.Height}");
        }
        finally
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
        }
    }

    /// <summary>Mesmo assentamento do monitor: numa janela recém-movida para um monitor novo, o item nasce 0×0.</summary>
    private static WgcMonitorCapture CaptureWindow(GpuDevice gpu, IntPtr hwnd, Action<string> log)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            var capture = WgcMonitorCapture.ForWindow(gpu, hwnd, captureCursor: false);
            if (capture.Width > 0 && capture.Height > 0)
            {
                log($"captura da janela {capture.Width}×{capture.Height} pronta após {sw.ElapsedMilliseconds} ms");
                return capture;
            }
            capture.Dispose();
            if (sw.ElapsedMilliseconds > 3000) throw new TimeoutException("item de captura da janela continuou 0×0");
            Thread.Sleep(100);
        }
    }

    private static (double x, double y) RelativeCenter(RectI window, RectI monitor) =>
        ((window.X + window.Width / 2.0 - monitor.X) / monitor.Width, (window.Y + window.Height / 2.0 - monitor.Y) / monitor.Height);

    private static (bool, string) StageCrop(IVirtualDisplayDriver driver, GpuDevice gpu, Action<string> log)
    {
        var (process, hwnd) = WindowOps.StartTestWindow();
        var stage = driver.Create(3840, 2160, 60);
        try
        {
            WindowOps.Move(hwnd, new RectI(stage.Display.Bounds.X + 100, stage.Display.Bounds.Y + 100, 800, 600));
            Thread.Sleep(300);
            using var capture = CaptureWindow(gpu, hwnd, log);
            var sink = new SizeProbe();
            capture.Start(sink);
            var first = sink.WaitSize(TimeSpan.FromSeconds(3));
            WindowOps.Move(hwnd, new RectI(stage.Display.Bounds.X + 100, stage.Display.Bounds.Y + 100, 1200, 900));
            var second = sink.WaitSizeDifferentFrom(first, TimeSpan.FromSeconds(3));
            log($"palco: {first} → {second}");
            bool ok = second.w > first.w && second.h > first.h;
            return (ok, $"recorte da janela {first.w}×{first.h} → {second.w}×{second.h} após redimensionar");
        }
        finally
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
            driver.Remove(stage);
        }
    }

    private static (bool, string) EncoderLimits(GpuDevice gpu, Action<string> log)
    {
        var results = new List<string>();
        foreach (var (w, h) in new[] { (4096, 1152), (5120, 1440), (7680, 2160), (1440, 5120) })
        {
            try
            {
                using var encoder = new MfH264Encoder(gpu.Device, new EncoderSettings(w, h, 30, 20_000), new NullSink());
                results.Add($"{w}×{h} ok");
            }
            catch (Exception e) { results.Add($"{w}×{h} FALHOU ({e.GetType().Name})"); }
            log(results[^1]);
        }
        return (results[0].EndsWith("ok"), string.Join("; ", results));
    }

    private static string Ffprobe(string file)
    {
        var psi = new ProcessStartInfo("ffprobe", $"-v error -select_streams v:0 -show_entries stream=width,height,codec_name -of compact \"{file}\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit(10_000);
        return output;
    }

    private sealed class NullSink : IEncodedFrameSink
    {
        public void OnEncodedFrame(ReadOnlySpan<byte> accessUnit, bool keyframe, ulong captureTimeUs) { }
    }

    private sealed class SizeProbe : ICaptureFrameSink
    {
        private volatile int _w, _h;
        public void OnFrame(ID3D11Texture2D bgraTexture, int width, int height, ulong captureTimeUs) { _w = width; _h = height; }

        public (int w, int h) WaitSize(TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            while (_w == 0 && sw.Elapsed < timeout) Thread.Sleep(20);
            return (_w, _h);
        }

        public (int w, int h) WaitSizeDifferentFrom((int w, int h) old, TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            while ((_w, _h) == old && sw.Elapsed < timeout) Thread.Sleep(20);
            return (_w, _h);
        }
    }
}
