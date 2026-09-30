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
    private readonly Dictionary<(string, uint), Live> _live = new();
    private readonly object _lock = new();
    private readonly Timer _sweeper;
    private SudoVdaDriver? _driver;

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

    public ScreenAcquireResult Acquire(string deviceId, AcquireScreenMessage request)
    {
        lock (_lock)
        {
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

    public bool StartStream(string deviceId, uint screenKey, string address, ushort udpPort, ushort streamId, out int width, out int height)
    {
        width = height = 0;
        lock (_lock)
        {
            if (!_live.TryGetValue((deviceId, screenKey), out var live)) return false;
            if (live.Pipeline != null && live.Address == address && live.Port == udpPort && live.StreamId == streamId)
            {
                live.Pipeline.RequestKeyframe();
                width = live.Width;
                height = live.Height;
                return true;
            }
            live.Pipeline?.Dispose();
            live.Pipeline = null;
            try
            {
                var capture = CaptureWithRetry(live.DisplayName);
                var fit = ResolutionFit.FromCapture(capture.Width, capture.Height);
                var settings = new EncoderSettings(fit.Width, fit.Height, 60, _bitrateKbps);
                var pipeline = new StreamPipeline(_gpu, capture, settings, new IPEndPoint(IPAddress.Parse(address), udpPort), 1, null, streamId);
                pipeline.Start();
                live.Pipeline = pipeline;
                live.Address = address;
                live.Port = udpPort;
                live.StreamId = streamId;
                live.Width = width = fit.Width;
                live.Height = height = fit.Height;
                _log($"vídeo: {pipeline.Description} → {address}:{udpPort} (tela {screenKey})");
                return true;
            }
            catch (Exception e)
            {
                _log($"vídeo: falha ao iniciar a tela {screenKey} ({e.GetType().Name}: {e.Message})");
                return false;
            }
        }
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

    public void Release(string deviceId, uint screenKey, TimeSpan grace)
    {
        lock (_lock)
        {
            StopStream(deviceId, screenKey);
            _leases.Expire(deviceId, screenKey, DateTimeOffset.Now + grace);
        }
    }

    public void Disconnected(string deviceId)
    {
        lock (_lock)
        {
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
    }

    private void Sweep()
    {
        lock (_lock)
        {
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
        if (live.Monitor == null || _driver == null) return; // espelho: nada a remover no Windows
        try { _driver.Remove(live.Monitor); }
        catch (Exception e) { _log($"telas: falha ao remover {live.DisplayName}: {e.Message}"); }
    }

    /// <summary>Pedido de keyframe (KFRQ, UDP 9101) de um headset: força IDR no stream dele.</summary>
    public void RequestKeyframe(IPAddress address, ushort streamId)
    {
        lock (_lock)
        {
            foreach (var live in _live.Values)
            {
                if (live.Pipeline != null && live.StreamId == streamId && IPAddress.TryParse(live.Address, out var ip)
                    && ip.MapToIPv4().Equals(address))
                    live.Pipeline.RequestKeyframe();
            }
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
            foreach (var key in _live.Keys.ToList()) DisposeLive(key.Item1, key.Item2);
            _driver?.Dispose();
            _driver = null;
        }
    }
}
