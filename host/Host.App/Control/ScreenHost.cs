using System.Diagnostics;
using System.Net;
using ScreenPortal.App.Connection;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Host.App.Display;
using ScreenPortal.Host.Capture;
using ScreenPortal.Host.Domain.Screens;
using ScreenPortal.Host.Encode;
using ScreenPortal.Host.Transport.Control;

namespace ScreenPortal.Host.App.Control;

/// <summary>
/// Monitores e streams por headset (spec 2C §4, ADR 0009): aluguéis na ScreenLeaseTable, monitores SudoVDA com a
/// resolução exata, um StreamPipeline por tela. Prazos: o que o headset mandar ao remover (10 s) e
/// <c>disconnectGrace</c> ao cair. Se o host morrer, o watchdog do SudoVDA remove os monitores sozinho (sem ping).
/// </summary>
public sealed class ScreenHost : IScreenHost, IDisposable
{
    private static readonly TimeSpan CaptureSettle = TimeSpan.FromSeconds(3); // ADR 0007: o WGC recusa o HMONITOR por ~1 s

    private sealed class Live
    {
        public VirtualMonitor? Monitor;   // só Virtual
        public required string DisplayName;
        public StreamPipeline? Pipeline;
        public string Address = "";
        public ushort Port;
        public ushort StreamId;
        public int Width;
        public int Height;
    }

    private readonly GpuDevice _gpu;
    private readonly int _bitrateKbps;
    private readonly TimeSpan _disconnectGrace;
    private readonly Action<string> _log;
    private readonly ScreenLeaseTable _leases = new();
    private readonly DeviceOwnership _owners = new(); // lock próprio: Claim não espera _lock (criar monitor leva ~3 s)
    private readonly Dictionary<(string, uint), Live> _live = new();
    private readonly object _lock = new();
    private readonly Timer _sweeper;
    private SudoVdaDriver? _driver;
    private bool _disposed;
    // Cópia imutável (copy-on-write sob _lock) lida sem lock: o IDR não espera a criação/remoção de monitores.
    private volatile (string Address, ushort StreamId, StreamPipeline Pipeline)[] _keyframeTargets = [];

    public IMonitorProvider PhysicalMonitors { get; }

    public ScreenHost(GpuDevice gpu, int bitrateKbps, TimeSpan disconnectGrace, Action<string> log)
    {
        _gpu = gpu;
        _bitrateKbps = bitrateKbps;
        _disconnectGrace = disconnectGrace;
        _log = log;
        PhysicalMonitors = new WindowsMonitorProvider(IsVirtualDisplay);
        _sweeper = new Timer(_ => Sweep(), null, 1000, 1000);
    }

    public IReadOnlyList<ScreenLeaseInfo> Leases(string deviceId)
    {
        lock (_lock)
            return _leases.ForDevice(deviceId)
                .Select(l => new ScreenLeaseInfo(l.ScreenKey, l.Kind == ScreenLeaseKind.Mirror ? ScreenKind.Mirror : ScreenKind.Virtual,
                    (uint)l.Width, (uint)l.Height))
                .ToList();
    }

    public long Claim(string deviceId, Action onReplaced)
    {
        long generation = _owners.Claim(deviceId, onReplaced, out var replaced);
        if (replaced != null)
        {
            _log($"telas: {deviceId} reconectou; sessão anterior substituída");
            try { replaced(); }
            catch (Exception e) { _log($"telas: falha ao encerrar a sessão anterior de {deviceId}: {e.Message}"); }
        }
        return generation;
    }

    /// <summary>Trabalho de uma sessão que já não é a dona do device (chamar com _lock).</summary>
    private bool Stale(string deviceId, long generation, string what)
    {
        if (_owners.IsCurrent(deviceId, generation)) return false;
        _log($"telas: {what} de {deviceId} ignorado (sessão antiga)");
        return true;
    }

    public ScreenAcquireResult Acquire(string deviceId, long generation, AcquireScreenMessage request)
    {
        lock (_lock)
        {
            if (_disposed) return new ScreenAcquireResult(false, 0, 0, "Host encerrando.");
            if (Stale(deviceId, generation, "AcquireScreen")) return new ScreenAcquireResult(false, 0, 0, "Sessão substituída.");
            if (_leases.TryGet(deviceId, request.ScreenKey, out var lease))
            {
                bool same = request.ScreenKind == ScreenKind.Mirror
                    ? lease.Kind == ScreenLeaseKind.Mirror && lease.PhysicalMonitorId == request.PhysicalMonitorId
                    : lease.Kind == ScreenLeaseKind.Virtual && lease.Width == request.Width && lease.Height == request.Height;
                if (same)
                {
                    _leases.Renew(deviceId, request.ScreenKey);
                    _log($"telas: {deviceId}/{request.ScreenKey} reatado ({lease.DisplayName})");
                    return new ScreenAcquireResult(true, lease.Width, lease.Height, "");
                }
                Destroy(deviceId, request.ScreenKey); // RecreateScreen: troca o monitor
            }
            return request.ScreenKind == ScreenKind.Mirror ? CreateMirror(deviceId, request) : CreateVirtual(deviceId, request);
        }
    }

    private ScreenAcquireResult CreateVirtual(string deviceId, AcquireScreenMessage request)
    {
        // Tamanho vem da rede: validar antes do driver (e antes do cast uint → int).
        if (!ScreenAcquireResult.TryValidateVirtualSize(request.Width, request.Height, out var invalid)) return invalid;
        try
        {
            _driver ??= SudoVdaDriver.Open(_log);
        }
        catch (Exception e)
        {
            return new ScreenAcquireResult(false, 0, 0, "SudoVDA não disponível: " + e.Message);
        }
        try
        {
            var monitor = _driver.Create((int)request.Width, (int)request.Height, 60);
            string name = monitor.Display.DeviceName;
            _leases.Add(new ScreenLease(deviceId, request.ScreenKey, ScreenLeaseKind.Virtual, (int)request.Width, (int)request.Height, 0, name));
            _live[(deviceId, request.ScreenKey)] = new Live { Monitor = monitor, DisplayName = name };
            _log($"telas: monitor virtual {request.Width}×{request.Height} criado ({name}) para {deviceId}/{request.ScreenKey}");
            return new ScreenAcquireResult(true, (int)request.Width, (int)request.Height, "");
        }
        catch (Exception e)
        {
            return new ScreenAcquireResult(false, 0, 0, "Falha ao criar monitor: " + e.Message);
        }
    }

    private ScreenAcquireResult CreateMirror(string deviceId, AcquireScreenMessage request)
    {
        var monitor = PhysicalMonitors.GetMonitors().FirstOrDefault(m => m.Id == request.PhysicalMonitorId);
        if (monitor == null) return new ScreenAcquireResult(false, 0, 0, "Monitor físico não encontrado.");
        var fit = ResolutionFit.FromCapture((int)monitor.Width, (int)monitor.Height);
        _leases.Add(new ScreenLease(deviceId, request.ScreenKey, ScreenLeaseKind.Mirror, fit.Width, fit.Height, monitor.Id, monitor.Name));
        _live[(deviceId, request.ScreenKey)] = new Live { DisplayName = monitor.Name };
        _log($"telas: espelho de {monitor.Name} para {deviceId}/{request.ScreenKey}");
        return new ScreenAcquireResult(true, fit.Width, fit.Height, "");
    }

    public bool StartStream(string deviceId, long generation, uint screenKey, string address, ushort udpPort, ushort streamId, out int width, out int height)
    {
        width = height = 0;
        lock (_lock)
        {
            if (_disposed || Stale(deviceId, generation, "StartStream") || !_live.TryGetValue((deviceId, screenKey), out var live)) return false;
            if (live.Pipeline != null && live.Address == address && live.Port == udpPort && live.StreamId == streamId)
            {
                live.Pipeline.RequestKeyframe();
                width = live.Width;
                height = live.Height;
                return true;
            }
            live.Pipeline?.Dispose();
            live.Pipeline = null;
            RebuildKeyframeTargets();
            WgcMonitorCapture? capture = null;
            StreamPipeline? pipeline = null;
            try
            {
                capture = CaptureWithRetry(live.DisplayName);
                var fit = ResolutionFit.FromCapture(capture.Width, capture.Height);
                var settings = new EncoderSettings(fit.Width, fit.Height, 60, _bitrateKbps);
                pipeline = new StreamPipeline(_gpu, capture, settings, new IPEndPoint(IPAddress.Parse(address), udpPort), 1, null, streamId);
                pipeline.Start();
                live.Pipeline = pipeline;
                live.Address = address;
                live.Port = udpPort;
                live.StreamId = streamId;
                live.Width = width = fit.Width;
                live.Height = height = fit.Height;
                RebuildKeyframeTargets();
                _log($"vídeo: {pipeline.Description} → {address}:{udpPort} (tela {screenKey})");
                return true;
            }
            catch (Exception e)
            {
                if (pipeline != null) pipeline.Dispose(); else capture?.Dispose(); // o pipeline descarta a captura
                _log($"vídeo: falha ao iniciar a tela {screenKey} ({e.GetType().Name}: {e.Message})");
                LogDeviceState();
                return false;
            }
        }
    }

    /// <summary>Diagnóstico de falha de pipeline: motivo de remoção do dispositivo D3D e memória livre do sistema.</summary>
    private void LogDeviceState()
    {
        var reason = _gpu.Device.DeviceRemovedReason;
        var memory = GC.GetGCMemoryInfo();
        _log($"vídeo: GPU DeviceRemovedReason=0x{reason.Code:X8} ({reason.Description}); memória: carga {memory.MemoryLoadBytes / (1 << 20)} MiB de {memory.TotalAvailableMemoryBytes / (1 << 20)} MiB");
    }

    /// <summary>Logo após criar um monitor, o WGC rejeita o HMONITOR por ~1 s (ADR 0007): resolve e tenta de novo.</summary>
    private WgcMonitorCapture CaptureWithRetry(string displayName)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return WgcMonitorCapture.ForMonitor(_gpu, Win32Displays.Current(displayName).HMonitor, captureCursor: true);
            }
            catch (Exception e) when ((e is ArgumentException || e is InvalidOperationException) && sw.Elapsed < CaptureSettle)
            {
                Thread.Sleep(100);
            }
        }
    }

    public void Release(string deviceId, long generation, uint screenKey, TimeSpan grace)
    {
        lock (_lock)
        {
            if (Stale(deviceId, generation, "ReleaseScreen")) return;
            StopStream(deviceId, screenKey);
            _leases.Expire(deviceId, screenKey, DateTimeOffset.Now + grace);
        }
    }

    public void Disconnected(string deviceId, long generation)
    {
        lock (_lock)
        {
            // Depois de uma reconexão, o fechamento do socket antigo não pode parar os streams da sessão nova.
            if (Stale(deviceId, generation, "Disconnected")) return;
            foreach (var lease in _leases.ForDevice(deviceId)) StopStream(deviceId, lease.ScreenKey);
            _leases.ExpireAll(deviceId, DateTimeOffset.Now + _disconnectGrace);
            _log($"telas: {deviceId} desconectou; monitores mantidos por {_disconnectGrace.TotalMinutes:0.#} min");
        }
    }

    private void StopStream(string deviceId, uint screenKey)
    {
        if (!_live.TryGetValue((deviceId, screenKey), out var live) || live.Pipeline == null) return;
        live.Pipeline.Dispose(); // descarta também a captura
        live.Pipeline = null;
        RebuildKeyframeTargets();
    }

    private void Sweep()
    {
        lock (_lock)
        {
            if (_disposed) return;
            foreach (var lease in _leases.TakeExpired(DateTimeOffset.Now))
            {
                DisposeLive(lease.DeviceId, lease.ScreenKey);
                _log($"telas: {lease.DeviceId}/{lease.ScreenKey} expirou ({lease.DisplayName} {(lease.Kind == ScreenLeaseKind.Virtual ? "removido" : "stream encerrado")})");
            }
        }
    }

    private void Destroy(string deviceId, uint screenKey)
    {
        _leases.Remove(deviceId, screenKey);
        DisposeLive(deviceId, screenKey);
    }

    private void DisposeLive(string deviceId, uint screenKey)
    {
        if (!_live.Remove((deviceId, screenKey), out var live)) return;
        live.Pipeline?.Dispose();
        live.Pipeline = null;
        RebuildKeyframeTargets();
        if (live.Monitor == null || _driver == null) return; // espelho: nada a remover no Windows
        try { _driver.Remove(live.Monitor); }
        catch (Exception e) { _log($"telas: falha ao remover {live.DisplayName}: {e.Message}"); }
    }

    private void RebuildKeyframeTargets() // chamar com _lock
    {
        _keyframeTargets = _live.Values.Where(l => l.Pipeline != null)
            .Select(l => (l.Address, l.StreamId, l.Pipeline!)).ToArray();
    }

    /// <summary>Pedido de keyframe (KFRQ, UDP 9101) de um headset: força IDR no stream dele. Sem lock (só seta um flag).</summary>
    public void RequestKeyframe(IPAddress address, ushort streamId)
    {
        foreach (var t in _keyframeTargets)
        {
            if (t.StreamId == streamId && IPAddress.TryParse(t.Address, out var ip) && ip.MapToIPv4().Equals(address))
                t.Pipeline.RequestKeyframe();
        }
    }

    public bool IsVirtualDisplay(string deviceName)
    {
        lock (_lock)
            return _live.Values.Any(l => l.Monitor != null && l.DisplayName.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
    }

    public string TakeStats(double seconds)
    {
        lock (_lock)
        {
            var streams = _live.Where(p => p.Value.Pipeline != null).ToList();
            if (streams.Count == 0) return $"sem stream ({_leases.Count} monitor(es) alugado(s))";
            return string.Join(" || ", streams.Select(p => $"{p.Key.Item2}: {p.Value.Pipeline!.TakeStats(seconds)}"));
        }
    }

    public void Dispose()
    {
        _sweeper.Dispose();
        lock (_lock)
        {
            _disposed = true;
            foreach (var key in _live.Keys.ToList()) DisposeLive(key.Item1, key.Item2);
            _driver?.Dispose();
            _driver = null;
        }
    }
}
