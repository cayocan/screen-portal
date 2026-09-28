using System.Diagnostics;
using System.Net;
using ScreenPortal.Host.App;
using ScreenPortal.Host.Capture;
using ScreenPortal.Host.Encode;
using ScreenPortal.Host.Transport;

// Screen Portal host.
//
//   ScreenPortalHost [serve] [--bitrate 20000]   (padrão) controle tcp/9200 + mDNS + pareamento + vídeo por headset
//
// Ferramentas da Fase 0 (docs/phase0-runbook.md):
//   Host.App stream  --target <ip> [--port 9000] [--streams 1] [--fps 60] [--bitrate 20000]
//                    [--width W --height H] [--cursor] [--dump out.h264] [--seconds N]
//   Host.App receive [--port 9000] [--out out.h264] [--seconds N]

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
    default:
        Console.Error.WriteLine("Uso: Host.App [serve] | stream --target <ip> [...] | receive [...]");
        return 2;
}

static int RunServe(Args o, CancellationToken ct)
{
    static void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");

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
    using var streams = new ScreenPortal.Host.App.Control.StreamRegistry(gpu, o.Int("bitrate", 20_000), Log);
    var monitors = new ScreenPortal.Host.App.Control.WindowsMonitorProvider();
    var approver = new ScreenPortal.Host.App.Control.MessageBoxPairingApprover();
    string hostName = Environment.MachineName;

    using var server = new ScreenPortal.Host.Transport.Control.ControlServer(
        ScreenPortal.Infrastructure.Protocol.ControlProtocol.Port,
        connection => new ScreenPortal.Host.Transport.Control.HeadsetSession(connection, pairing, SavePairing, approver, monitors, streams,
            hostName, () => ScreenPortal.Host.Domain.Pairing.PairingStore.NewPairingCode(System.Security.Cryptography.RandomNumberGenerator.GetInt32), Log),
        Log);
    server.Start();

    using var mdns = new ScreenPortal.Host.App.Control.MdnsAdvertiser(Log);
    mdns.Start(hostName, ScreenPortal.Infrastructure.Protocol.ControlProtocol.Port, "1");

    Log($"Screen Portal host em {hostName} (GPU: {gpu.AdapterName}). {pairing.Devices.Count} headset(s) pareado(s). Ctrl+C para sair.");
    var sw = Stopwatch.StartNew();
    while (!ct.WaitHandle.WaitOne(1000))
    {
        double s = sw.Elapsed.TotalSeconds;
        sw.Restart();
        if (server.ConnectionCount > 0) Log(streams.TakeStats(s));
    }
    return 0;
}

static int RunStream(Args o, CancellationToken ct)
{
    var target = new IPEndPoint(IPAddress.Parse(o.Str("target", "127.0.0.1")), o.Int("port", 9000));

    using var gpu = new GpuDevice();
    using var capture = WgcMonitorCapture.ForPrimaryMonitor(gpu, captureCursor: o.Flag("cursor"));
    // NV12 exige dimensões pares.
    int width = o.Int("width", capture.Width) & ~1;
    int height = o.Int("height", capture.Height) & ~1;
    var settings = new EncoderSettings(width, height, o.Int("fps", 60), o.Int("bitrate", 20_000));

    using var clock = new ClockSyncServer();
    using var pipeline = new StreamPipeline(gpu, capture, settings, target, o.Int("streams", 1), o.StrOrNull("dump"));
    Console.WriteLine($"GPU: {gpu.AdapterName}");
    Console.WriteLine(pipeline.Description);
    Console.WriteLine($"clock sync: udp/{ClockSyncServer.DefaultPort}. Ctrl+C para parar.");
    pipeline.Start();
    // O tempo de --seconds conta a partir do início do stream (a inicialização do MFT leva ~1-3 s).
    using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
    if (o.Int("seconds", 0) > 0) timer.CancelAfter(TimeSpan.FromSeconds(o.Int("seconds", 0)));
    ct = timer.Token;

    var sw = Stopwatch.StartNew();
    while (!ct.WaitHandle.WaitOne(1000))
    {
        double s = sw.Elapsed.TotalSeconds;
        sw.Restart();
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {pipeline.TakeStats(s)} | clk req {Interlocked.Exchange(ref clock.Requests, 0)}");
    }
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
