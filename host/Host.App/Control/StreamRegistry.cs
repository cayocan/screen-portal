using System.Net;
using ScreenPortal.App.Connection;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Host.Capture;
using ScreenPortal.Host.Encode;
using ScreenPortal.Host.Transport.Control;

namespace ScreenPortal.Host.App.Control;

/// <summary>
/// Um StreamPipeline por headset. Na reconexão do mesmo device (mesmo destino) o pipeline é reaproveitado
/// com um IDR forçado; sem reconexão em 10 s, é encerrado (spec 1B §3).
/// Fase 1B: só o monitor primário (a captura WGC da Fase 0).
/// </summary>
public sealed class StreamRegistry(GpuDevice gpu, int bitrateKbps, Action<string> log) : IStreamRegistry, IDisposable
{
    private static readonly TimeSpan ReleaseGrace = TimeSpan.FromSeconds(10);
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly object _lock = new();

    private sealed class Entry
    {
        public required StreamPipeline Pipeline;
        public required ICaptureSource Capture;
        public required string Address;
        public required ushort Port;
        public required ushort StreamId;
        public required int Width;
        public required int Height;
        public CancellationTokenSource? ReleaseTimer;
    }

    public bool TryStart(string deviceId, string address, ushort udpPort, ushort streamId, MonitorDescriptor monitor, out int width, out int height)
    {
        width = height = 0;
        if (!monitor.IsPrimary) return false;
        lock (_lock)
        {
            if (_entries.TryGetValue(deviceId, out var existing))
            {
                if (existing.Address == address && existing.Port == udpPort && existing.StreamId == streamId)
                {
                    existing.ReleaseTimer?.Cancel();
                    existing.ReleaseTimer = null;
                    existing.Pipeline.RequestKeyframe();
                    width = existing.Width;
                    height = existing.Height;
                    log($"vídeo: reaproveitando o stream de {deviceId} (IDR forçado)");
                    return true;
                }
                Stop(existing);
                _entries.Remove(deviceId);
            }

            try
            {
                var capture = WgcMonitorCapture.ForPrimaryMonitor(gpu, captureCursor: true);
                var fit = ResolutionFit.FromCapture(capture.Width, capture.Height);
                var settings = new EncoderSettings(fit.Width, fit.Height, 60, bitrateKbps);
                var pipeline = new StreamPipeline(gpu, capture, settings, new IPEndPoint(IPAddress.Parse(address), udpPort), 1, null, streamId);
                pipeline.Start();
                _entries[deviceId] = new Entry
                {
                    Pipeline = pipeline, Capture = capture, Address = address, Port = udpPort,
                    StreamId = streamId, Width = fit.Width, Height = fit.Height,
                };
                width = fit.Width;
                height = fit.Height;
                log($"vídeo: {pipeline.Description}");
                return true;
            }
            catch (Exception e)
            {
                log($"vídeo: falha ao iniciar ({e.GetType().Name}: {e.Message})");
                return false;
            }
        }
    }

    public void Release(string deviceId)
    {
        CancellationTokenSource cts;
        lock (_lock)
        {
            if (!_entries.TryGetValue(deviceId, out var entry)) return;
            entry.ReleaseTimer?.Cancel();
            cts = entry.ReleaseTimer = new CancellationTokenSource();
        }
        Task.Delay(ReleaseGrace, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            lock (_lock)
            {
                if (_entries.TryGetValue(deviceId, out var entry) && entry.ReleaseTimer == cts)
                {
                    Stop(entry);
                    _entries.Remove(deviceId);
                    log($"vídeo: stream de {deviceId} encerrado (sem reconexão em {ReleaseGrace.TotalSeconds:F0} s)");
                }
            }
        });
    }

    public string TakeStats(double seconds)
    {
        lock (_lock)
        {
            if (_entries.Count == 0) return "sem stream";
            return string.Join(" || ", _entries.Select(e => $"{e.Key}: {e.Value.Pipeline.TakeStats(seconds)}"));
        }
    }

    private static void Stop(Entry entry)
    {
        entry.ReleaseTimer?.Cancel();
        entry.Pipeline.Dispose(); // descarta também a captura
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var entry in _entries.Values) Stop(entry);
            _entries.Clear();
        }
    }
}
