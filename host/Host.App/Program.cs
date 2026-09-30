using System.Diagnostics;
using System.Net;
using ScreenPortal.Host.App;
using ScreenPortal.Host.Capture;
using ScreenPortal.Host.Encode;
using ScreenPortal.Host.Transport;

// Screen Portal host.
//
//   ScreenPortalHost [serve] [--bitrate 20000] [--grace-minutes 5]   (padrão) controle tcp/9200 + mDNS + pareamento + monitores/vídeo por tela
//
// Ferramentas da Fase 0 (docs/phase0-runbook.md):
//   Host.App stream  --target <ip> [--port 9000] [--streams 1] [--fps 60] [--bitrate 20000]
//                    [--width W --height H] [--cursor] [--dump out.h264] [--seconds N]
//                    [--mix "60x1,15x3"]   (Fase 2A: grupos <fps>x<quantidade>; substitui --fps/--streams)
//   Host.App receive [--port 9000] [--out out.h264] [--seconds N]
//
// Fase 2A: ScreenPortalHost display-spike --driver sudovda|vdd [--out relatorio.md]

Console.OutputEncoding = System.Text.Encoding.UTF8;
var opts = Args.Parse(args);
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
int runSeconds = opts.Int("seconds", 0);
if (opts.Command != "stream" && runSeconds > 0) cts.CancelAfter(TimeSpan.FromSeconds(runSeconds));

switch (opts.Command)
{
    case "":
    case "serve": return RunServe(opts, cts.Token);
    case "stream": return RunStream(opts, cts.Token);
    case "receive": return RunReceive(opts, cts.Token);
    case "display-spike": return RunDisplaySpike(opts);
    default:
        Console.Error.WriteLine("Uso: Host.App [serve] | stream --target <ip> [...] | receive [...]");
        return 2;
}

static int RunServe(Args o, CancellationToken ct)
{
    // Log no console e em %APPDATA%\ScreenPortal\host.log (recriado a cada serve; leitura compartilhada).
    string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenPortal", "host.log");
    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
    var logFile = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
    var logLock = new object();
    void Log(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Console.WriteLine(line);
        lock (logLock) logFile.WriteLine(line);
    }

    string pairingPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenPortal", "paired.json");
    var pairing = File.Exists(pairingPath)
        ? ScreenPortal.Host.Domain.Pairing.PairingStore.Deserialize(File.ReadAllText(pairingPath))
        : new ScreenPortal.Host.Domain.Pairing.PairingStore();
    var saveLock = new object();
    void SavePairing()
    {
        lock (saveLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(pairingPath)!);
            File.WriteAllText(pairingPath, pairing.Serialize());
        }
    }

    using var gpu = new GpuDevice();
    using var clock = new ClockSyncServer();
    int graceMinutes = o.Int("grace-minutes", 5);
    if (graceMinutes < 1)
    {
        Log($"--grace-minutes {graceMinutes} inválido; usando 1.");
        graceMinutes = 1;
    }
    using var screens = new ScreenPortal.Host.App.Control.ScreenHost(gpu, o.Int("bitrate", 20_000),
        TimeSpan.FromMinutes(graceMinutes), Log);
    var approver = new ScreenPortal.Host.App.Control.MessageBoxPairingApprover();
    string hostName = Environment.MachineName;

    using var server = new ScreenPortal.Host.Transport.Control.ControlServer(
        ScreenPortal.Infrastructure.Protocol.ControlProtocol.Port,
        connection => new ScreenPortal.Host.Transport.Control.HeadsetSession(connection, pairing, SavePairing, approver,
            screens.PhysicalMonitors, screens, hostName, () => ScreenPortal.Host.Domain.Pairing.PairingStore.NewPairingCode(System.Security.Cryptography.RandomNumberGenerator.GetInt32), Log),
        Log);
    server.Start();
    using var keyframes = new KeyframeRequestListener(KeyframeRequestListener.DefaultPort, screens.RequestKeyframe);

    using var mdns = new ScreenPortal.Host.App.Control.MdnsAdvertiser(Log);
    mdns.Start(hostName, ScreenPortal.Infrastructure.Protocol.ControlProtocol.Port, "1");

    Log($"Screen Portal host em {hostName} (GPU: {gpu.AdapterName}). {pairing.Devices.Count} headset(s) pareado(s). Ctrl+C para sair.");
    var sw = Stopwatch.StartNew();
    while (!ct.WaitHandle.WaitOne(1000))
    {
        double s = sw.Elapsed.TotalSeconds;
        sw.Restart();
        if (server.ConnectionCount > 0) Log(screens.TakeStats(s));
    }
    return 0;
}

static int RunDisplaySpike(Args o)
{
    static void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
    string driverName = o.Str("driver", "sudovda").ToLowerInvariant();
    using var gpu = new GpuDevice();
    using ScreenPortal.Host.App.Display.IVirtualDisplayDriver driver = driverName == "vdd"
        ? ScreenPortal.Host.App.Display.VddDriver.Open(Log)
        : ScreenPortal.Host.App.Display.SudoVdaDriver.Open(Log);
    string report = ScreenPortal.Host.App.Display.DisplaySpike.Run(driver, gpu, Log, o.Int("only", 0));
    string outPath = o.Str("out", Path.Combine("docs", "measurements", $"fase2a-driver-{driverName}.md"));
    File.WriteAllText(outPath, report);
    Console.WriteLine(report);
    Log($"relatório: {outPath}");
    return 0;
}

static int RunStream(Args o, CancellationToken ct)
{
    var target = new IPEndPoint(IPAddress.Parse(o.Str("target", "127.0.0.1")), o.Int("port", 9000));

    // --mix "60x1,15x3": um pipeline (captura WGC própria) por grupo de fps; stream ids e portas consecutivos.
    var groups = o.StrOrNull("mix") is { } mix
        ? ScreenPortal.Host.Domain.Video.MixSpec.Parse(mix)
        : new[] { new ScreenPortal.Host.Domain.Video.MixGroup(o.Int("fps", 60), o.Int("streams", 1)) };

    using var gpu = new GpuDevice();
    using var clock = new ClockSyncServer();
    var pipelines = new List<StreamPipeline>();
    int nextStream = 0;
    foreach (var g in groups)
    {
        var capture = WgcMonitorCapture.ForPrimaryMonitor(gpu, captureCursor: o.Flag("cursor"));
        // NV12 exige dimensões pares.
        int width = o.Int("width", capture.Width) & ~1;
        int height = o.Int("height", capture.Height) & ~1;
        var settings = new EncoderSettings(width, height, g.Fps, o.Int("bitrate", 20_000));
        var groupTarget = new IPEndPoint(target.Address, target.Port + nextStream);
        pipelines.Add(new StreamPipeline(gpu, capture, settings, groupTarget, g.Count, o.StrOrNull("dump"), (ushort)nextStream));
        nextStream += g.Count;
    }

    using var keyframes = new KeyframeRequestListener(KeyframeRequestListener.DefaultPort, (_, streamId) =>
    {
        int first = 0;
        for (int i = 0; i < groups.Count; i++)
        {
            if (streamId >= first && streamId < first + groups[i].Count) { pipelines[i].RequestKeyframe(); return; }
            first += groups[i].Count;
        }
    });

    Console.WriteLine($"GPU: {gpu.AdapterName}");
    foreach (var p in pipelines) Console.WriteLine(p.Description);
    Console.WriteLine($"clock sync: udp/{ClockSyncServer.DefaultPort} · keyframe requests: udp/{KeyframeRequestListener.DefaultPort}. Ctrl+C para parar.");
    foreach (var p in pipelines) p.Start();
    // O tempo de --seconds conta a partir do início do stream (a inicialização do MFT leva ~1-3 s).
    using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
    if (o.Int("seconds", 0) > 0) timer.CancelAfter(TimeSpan.FromSeconds(o.Int("seconds", 0)));
    ct = timer.Token;

    var sw = Stopwatch.StartNew();
    while (!ct.WaitHandle.WaitOne(1000))
    {
        double s = sw.Elapsed.TotalSeconds;
        sw.Restart();
        long clk = Interlocked.Exchange(ref clock.Requests, 0);
        for (int i = 0; i < pipelines.Count; i++)
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {groups[i].Fps}fps×{groups[i].Count}: {pipelines[i].TakeStats(s)} | clk req {clk}");
    }
    foreach (var p in pipelines) p.Dispose();
    return 0;
}

static int RunReceive(Args o, CancellationToken ct)
{
    int port = o.Int("port", 9000);
    var outPath = o.StrOrNull("out");
    using var receiver = new UdpVideoReceiver(port, outPath is null ? null : File.Create(outPath));
    Console.WriteLine($"Recebendo udp/{port}{(outPath is null ? "" : $" → {outPath}")}. Ctrl+C para parar.");

    var thread = new Thread(() => receiver.Run(ct)) { IsBackground = true };
    thread.Start();
    long lastFrames = 0, lastSum = 0;
    while (!ct.WaitHandle.WaitOne(1000))
    {
        long frames = receiver.Frames, sum = receiver.LatencySumUs;
        long df = frames - lastFrames;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {df} fps | total {frames} (idr {receiver.Keyframes}, drop {receiver.Reassembler.FramesDropped}) " +
                          $"| cap→rx avg {(df > 0 ? (sum - lastSum) / 1000.0 / df : 0):F1} ms max {Interlocked.Exchange(ref receiver.LatencyMaxUs, 0) / 1000.0:F1} ms");
        lastFrames = frames; lastSum = sum;
    }
    thread.Join(500);
    return 0;
}

internal sealed class Args
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);
    public string Command { get; private init; } = "";

    public static Args Parse(string[] argv)
    {
        var a = new Args { Command = argv.Length > 0 ? argv[0].ToLowerInvariant() : "" };
        for (int i = 1; i < argv.Length; i++)
        {
            if (!argv[i].StartsWith("--")) continue;
            string key = argv[i][2..];
            string? value = i + 1 < argv.Length && !argv[i + 1].StartsWith("--") ? argv[++i] : null;
            a._values[key] = value;
        }
        return a;
    }

    public bool Flag(string key) => _values.ContainsKey(key);
    public string? StrOrNull(string key) => _values.GetValueOrDefault(key);
    public string Str(string key, string fallback) => _values.GetValueOrDefault(key) ?? fallback;
    public int Int(string key, int fallback) => int.TryParse(_values.GetValueOrDefault(key), out var v) ? v : fallback;
}
