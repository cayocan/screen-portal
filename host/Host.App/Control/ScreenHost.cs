using System.Diagnostics;
using System.Net;
using ScreenPortal.App.Connection;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Host.App.Display;
using ScreenPortal.Host.Capture;
using ScreenPortal.Host.Domain.Display;
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
        public DisplayInfo? Captured;
        public int Fps = 60;              // 2F: fps pedido pelo headset
        public int BaseKbps;              // 2F: bitrate do encoder a 60 fps (0 = padrão do host)     // diagnóstico (bug 2): o display como estava ao criar a captura
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
    // 3A §4: escala do Windows por headset/tela, lida a cada 3 s e reaplicada ao criar/recriar o monitor.
    private static readonly TimeSpan ScalePoll = TimeSpan.FromSeconds(3);
    private const int ScaleRestoreTries = 3;
    private readonly ScaleMemory _scales;
    private readonly string? _scalesPath;
    private readonly Dictionary<(string, uint), (int Percent, int Tries)> _scaleRestores = new(); // sob _lock
    private readonly Timer _scaleKeeper;
    private SudoVdaDriver? _driver;
    private bool _disposed;
    // Cópia imutável (copy-on-write sob _lock) lida sem lock: o IDR não espera a criação/remoção de monitores.
    private volatile (string Address, ushort StreamId, StreamPipeline Pipeline)[] _keyframeTargets = [];

    public IMonitorProvider PhysicalMonitors { get; }

    /// <param name="scalesPath">scales.json (escala por tela); null = não guarda.</param>
    public ScreenHost(GpuDevice gpu, int bitrateKbps, TimeSpan disconnectGrace, Action<string> log, string? scalesPath = null)
    {
        _gpu = gpu;
        _bitrateKbps = bitrateKbps;
        _disconnectGrace = disconnectGrace;
        _log = log;
        _scalesPath = scalesPath;
        _scales = ScaleMemory.FromJson(scalesPath != null && File.Exists(scalesPath) ? File.ReadAllText(scalesPath) : null);
        PhysicalMonitors = new WindowsMonitorProvider(IsVirtualDisplay);
        _sweeper = new Timer(_ => Sweep(), null, 1000, 1000);
        _scaleKeeper = new Timer(_ => KeepScales(), null, ScalePoll, ScalePoll);
    }

    /// <summary>Monitor (re)criado: volta a escala guardada da tela, se houver (chamar com _lock).</summary>
    private void RestoreScale(string deviceId, uint screenKey, string displayName)
    {
        if (!_scales.TryGet(deviceId, screenKey, out var percent)) return;
        int err = DisplayScale.Set(displayName, percent);
        if (err == -2)
        {
            _log($"escala: {percent}% não vale para {displayName} na resolução nova; fica a do Windows");
            return;
        }
        // O Windows pode reaplicar a escala padrão enquanto o monitor novo sobe: KeepScales confere e insiste.
        _scaleRestores[(deviceId, screenKey)] = (percent, 0);
        if (err == 0) _log($"escala: {displayName} de volta a {percent}%");
    }

    /// <summary>A cada 3 s: guarda a escala de cada monitor virtual vivo (e termina as restaurações pendentes).</summary>
    private void KeepScales()
    {
        bool changed = false;
        lock (_lock)
        {
            if (_disposed) return;
            foreach (var ((deviceId, screenKey), live) in _live)
            {
                if (live.Monitor == null) continue; // espelho: a escala do monitor físico é do usuário, não da tela
                int now = DisplayScale.Get(live.DisplayName);
                if (_scaleRestores.TryGetValue((deviceId, screenKey), out var restore))
                {
                    if (now == restore.Percent) _scaleRestores.Remove((deviceId, screenKey));
                    else if (restore.Tries < ScaleRestoreTries)
                    {
                        DisplayScale.Set(live.DisplayName, restore.Percent);
                        _scaleRestores[(deviceId, screenKey)] = (restore.Percent, restore.Tries + 1);
                        continue;
                    }
                    else
                    {
                        _log($"escala: o Windows não manteve {restore.Percent}% em {live.DisplayName}");
                        _scaleRestores.Remove((deviceId, screenKey));
                    }
                }
                changed |= _scales.Observe(deviceId, screenKey, now);
            }
        }
        if (!changed || _scalesPath == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_scalesPath)!);
            File.WriteAllText(_scalesPath, _scales.ToJson());
        }
        catch (Exception e) { _log($"escala: falha ao salvar {_scalesPath}: {e.Message}"); }
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
                if (request.ScreenKind == ScreenKind.Virtual && lease.Kind == ScreenLeaseKind.Virtual
                    && _live.TryGetValue((deviceId, request.ScreenKey), out var old) && old.Monitor != null)
                    return Recreate(deviceId, request, lease, old.Monitor); // RecreateScreen (spec 2D §4)
                Destroy(deviceId, request.ScreenKey); // outro tipo na mesma chave: troca o monitor
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
            VirtualMonitor monitor;
            try
            {
                _driver ??= SudoVdaDriver.Open(_log);
                monitor = _driver.Create((int)request.Width, (int)request.Height, 60);
            }
            catch (Exception first)
            {
                // Driver caído (código 43) ou handle velho: reinicia o dispositivo e tenta uma vez de novo.
                _log($"telas: SudoVDA falhou ({first.Message}); reiniciando o dispositivo");
                _driver?.Dispose();
                _driver = null;
                if (!SudoVdaRecovery.TryRestartDevice(_log))
                    return new ScreenAcquireResult(false, 0, 0, "SudoVDA não disponível: " + first.Message);
                _driver = SudoVdaDriver.Open(_log);
                monitor = _driver.Create((int)request.Width, (int)request.Height, 60);
            }
            string name = monitor.Display.DeviceName;
            _leases.Add(new ScreenLease(deviceId, request.ScreenKey, ScreenLeaseKind.Virtual, (int)request.Width, (int)request.Height, 0, name));
            _live[(deviceId, request.ScreenKey)] = new Live { Monitor = monitor, DisplayName = name };
            _log($"telas: monitor virtual {request.Width}×{request.Height} criado ({name}) para {deviceId}/{request.ScreenKey}");
            RestoreScale(deviceId, request.ScreenKey, name);
            return new ScreenAcquireResult(true, (int)request.Width, (int)request.Height, "");
        }
        catch (Exception e)
        {
            return new ScreenAcquireResult(false, 0, 0, "Falha ao criar monitor: " + e.Message);
        }
    }

    /// <summary>
    /// Recriar (spec 2D §4): cria o novo → move as janelas → para o stream do antigo e o remove → o aluguel passa ao
    /// novo. Se criar falhar, o antigo fica intacto (com o stream) e a resposta é de falha. Chamar com _lock.
    /// </summary>
    private ScreenAcquireResult Recreate(string deviceId, AcquireScreenMessage request, ScreenLease lease, VirtualMonitor oldMonitor)
    {
        if (!ScreenAcquireResult.TryValidateVirtualSize(request.Width, request.Height, out var invalid)) return invalid;
        int width = (int)request.Width, height = (int)request.Height;
        var recreator = new MonitorRecreator<VirtualMonitor>(new VirtualDisplayMonitors(_driver!, 60), new Win32WindowMover(_log), _log);
        var result = recreator.Recreate(_leases, lease, oldMonitor, width, height, () => StopStream(deviceId, request.ScreenKey));
        if (!result.Ok)
        {
            _log($"telas: recriar {deviceId}/{request.ScreenKey} em {width}×{height} falhou; {lease.DisplayName} mantido ({result.Error})");
            return new ScreenAcquireResult(false, 0, 0, result.Error);
        }
        string name = result.Monitor!.Display.DeviceName;
        _live[(deviceId, request.ScreenKey)] = new Live { Monitor = result.Monitor, DisplayName = name };
        _log($"telas: {deviceId}/{request.ScreenKey} recriado {lease.Width}×{lease.Height} → {width}×{height} " +
             $"({lease.DisplayName} → {name}, {result.WindowsMoved} janela(s) movida(s))");
        RestoreScale(deviceId, request.ScreenKey, name);
        return new ScreenAcquireResult(true, width, height, "");
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
            if (!StartPipeline(live, screenKey, address, udpPort, streamId)) return false;
            width = live.Width;
            height = live.Height;
            return true;
        }
    }

    /// <summary>(Re)cria a captura e o pipeline de uma tela (chamar com _lock). O pipeline novo começa com IDR.</summary>
    private bool StartPipeline(Live live, uint screenKey, string address, ushort udpPort, ushort streamId)
    {
        live.Pipeline?.Dispose();
        // O headset descarta frameIds que não sejam mais novos que o último: o pipeline novo segue a numeração.
        uint firstFrameId = live.Pipeline?.NextFrameId ?? 0;
        live.Pipeline = null;
        RebuildKeyframeTargets();
        WgcMonitorCapture? capture = null;
        StreamPipeline? pipeline = null;
        try
        {
            capture = CaptureWithRetry(live.DisplayName, out var captured);
            live.Captured = captured;
            string display = live.DisplayName;
            capture.Diagnostic += message => _log($"vídeo: tela {screenKey} ({display}): {message}");
            _log($"vídeo: tela {screenKey} captura {capture.Width}×{capture.Height} de {display}");
            var fit = ResolutionFit.FromCapture(capture.Width, capture.Height);
            var settings = new EncoderSettings(fit.Width, fit.Height, 60, live.BaseKbps > 0 ? live.BaseKbps : _bitrateKbps);
            pipeline = new StreamPipeline(_gpu, capture, settings, new IPEndPoint(IPAddress.Parse(address), udpPort), 1, null, streamId, firstFrameId);
            pipeline.Start();
            live.Pipeline = pipeline;
            if (live.Fps != 60) pipeline.SetFps(live.Fps); // pipeline recriado: mantém o fps da 2F
            live.Address = address;
            live.Port = udpPort;
            live.StreamId = streamId;
            live.Width = fit.Width;
            live.Height = fit.Height;
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

    /// <summary>Diagnóstico de falha de pipeline: motivo de remoção do dispositivo D3D e memória livre do sistema.</summary>
    private void LogDeviceState()
    {
        var reason = _gpu.Device.DeviceRemovedReason;
        var memory = GC.GetGCMemoryInfo();
        _log($"vídeo: GPU DeviceRemovedReason=0x{reason.Code:X8} ({reason.Description}); memória: carga {memory.MemoryLoadBytes / (1 << 20)} MiB de {memory.TotalAvailableMemoryBytes / (1 << 20)} MiB");
    }

    /// <summary>Logo após criar um monitor, o WGC rejeita o HMONITOR por ~1 s (ADR 0007): resolve e tenta de novo.</summary>
    private WgcMonitorCapture CaptureWithRetry(string displayName, out DisplayInfo display)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                display = Win32Displays.Current(displayName);
                // 3B: o cursor é desenhado pelo headset (CursorFeed), sem o atraso do vídeo
                return WgcMonitorCapture.ForMonitor(_gpu, display.HMonitor, captureCursor: false);
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

    /// <summary>
    /// 3A: o arranjo do Windows segue a disposição das telas no Quest (spec 3A §2): plano puro (DisplayArrangement) e um
    /// único SetDisplayConfig com os monitores que mudaram. Telas fora da mensagem (soltas) e monitores fora do Quest
    /// ficam onde estão. Cada monitor movido tem a captura recriada (FollowDisplayChanges).
    /// </summary>
    public void Arrange(string deviceId, long generation, IReadOnlyList<ScreenAnglesInfo> angles)
    {
        lock (_lock)
        {
            if (_disposed || Stale(deviceId, generation, "LayoutChanged")) return;
            var windows = Win32Displays.Snapshot();
            var current = windows.ToDictionary(d => d.DeviceName, d => d.Bounds, StringComparer.OrdinalIgnoreCase);
            var screens = new List<ArrangedScreen>();
            foreach (var a in angles)
            {
                if (!_live.TryGetValue((deviceId, a.ScreenKey), out var live) || !current.TryGetValue(live.DisplayName, out var bounds)) continue;
                if (screens.Exists(s => s.Monitor.Equals(live.DisplayName, StringComparison.OrdinalIgnoreCase))) continue; // dois espelhos do mesmo monitor
                screens.Add(new ArrangedScreen(live.DisplayName, bounds.Width, bounds.Height, a.AzimuthDeg, a.ElevationDeg, a.HalfWidthDeg, a.HalfHeightDeg));
            }
            if (screens.Count == 0) return;
            var primary = windows.FirstOrDefault(d => d.IsPrimary)?.DeviceName ?? "";
            var plan = DisplayArrangement.Plan(screens,
                windows.Select(d => new WindowsMonitor(d.DeviceName, d.Bounds, d.IsPrimary)).ToList(), primary);
            var changed = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, rect) in plan)
                if (!name.Equals(primary, StringComparison.OrdinalIgnoreCase) && current[name] != rect)
                    changed[name] = [rect.X, rect.Y];
            if (changed.Count == 0) return;
            int err = DisplayConfigPosition.SetPositions(changed);
            _log(err == 0
                ? $"arranjo: {changed.Count} monitor(es) reposicionado(s) como no Quest ({string.Join(", ", changed.Select(c => $"{c.Key} {c.Value[0]},{c.Value[1]}"))})"
                : $"arranjo: o Windows recusou o arranjo ({err})");
        }
    }

    /// <summary>2F: fps e bitrate de uma tela com stream, ao vivo. Loga só quando muda.</summary>
    public void SetRate(string deviceId, long generation, uint screenKey, int fps, int kbps)
    {
        lock (_lock)
        {
            if (_disposed || Stale(deviceId, generation, "SetStreamRate")) return;
            if (!_live.TryGetValue((deviceId, screenKey), out var live) || live.Pipeline == null) return;
            int current = live.BaseKbps > 0 ? live.BaseKbps : _bitrateKbps;
            int target = ScreenPortal.Host.Domain.Video.StreamRateLimits.BaseKbpsAt60(fps, kbps);
            bool newEncoder = ScreenPortal.Host.Domain.Video.StreamRateLimits.NeedsNewEncoder(current, target);
            if (live.Fps == fps && !newEncoder) return;
            live.Fps = fps;
            if (newEncoder)
            {
                live.BaseKbps = target;
                _log($"vídeo: tela {screenKey} → {fps} fps, encoder novo a {target} kbps (base a 60 fps)");
                StartPipeline(live, screenKey, live.Address, live.Port, live.StreamId); // mantém os frameIds; aplica o fps
                return;
            }
            live.Pipeline.SetFps(fps);
            _log($"vídeo: tela {screenKey} → {fps} fps");
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
            FollowDisplayChanges();
        }
    }

    /// <summary>
    /// Bug 2 da 2C: depois que o monitor muda de posição no desktop (o usuário reorganiza os monitores no Windows ou
    /// outro monitor é removido), a sessão WGC antiga continua mandando quadros, mas pretos ou deslocados. Quando a
    /// posição de uma tela com stream muda, a captura é recriada no mesmo destino (porta e stream id).
    /// </summary>
    private void FollowDisplayChanges() // chamar com _lock
    {
        if (!_live.Values.Any(l => l.Pipeline != null && l.Captured != null)) return;
        var now = Win32Displays.Snapshot();
        foreach (var (key, live) in _live)
        {
            if (live.Pipeline == null || live.Captured is not { } before) continue;
            var after = now.FirstOrDefault(d => d.DeviceName.Equals(live.DisplayName, StringComparison.OrdinalIgnoreCase));
            if (after == null || !CaptureRebind.Needed(before.Bounds, after.Bounds))
            {
                if (after != null) live.Captured = after; // só o HMONITOR mudou: a captura segue válida
                continue;
            }
            _log($"vídeo: tela {key.Item2} ({live.DisplayName}) mudou de {before.Bounds.X},{before.Bounds.Y} para {after.Bounds.X},{after.Bounds.Y}; recriando a captura");
            StartPipeline(live, key.Item2, live.Address, live.Port, live.StreamId);
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
        _scaleRestores.Remove((deviceId, screenKey));
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

    /// <summary>3A (atalhos): a tela de headset mostrada neste monitor (virtual ou espelhado), se houver.</summary>
    public (string DeviceId, uint ScreenKey)? ScreenAt(string deviceName)
    {
        lock (_lock)
            foreach (var (key, live) in _live)
                if (live.DisplayName.Equals(deviceName, StringComparison.OrdinalIgnoreCase)) return key;
        return null;
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
        _scaleKeeper.Dispose();
        lock (_lock)
        {
            _disposed = true;
            foreach (var key in _live.Keys.ToList()) DisposeLive(key.Item1, key.Item2);
            _driver?.Dispose();
            _driver = null;
        }
    }
}
