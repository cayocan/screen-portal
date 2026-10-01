using System.Net;
using NAudio.CoreAudioApi;
using ScreenPortal.Host.Domain.Audio;
using ScreenPortal.Host.Transport.Control;

namespace ScreenPortal.Host.App.Audio;

/// <summary>
/// Áudio do headset atual (spec 2G §3, ADR 0015): som do PC pelo loopback da saída silenciosa do Steam (a saída padrão
/// do Windows troca para ela e volta ao parar) e microfone do Quest no par "Steam Streaming Microphone". Sem os drivers
/// do Steam, loopback da saída padrão e sem microfone. Um headset por vez: a sessão nova substitui a anterior.
/// </summary>
public sealed class AudioHost : IAudioHost, IDisposable
{
    public const string SteamSpeakers = "Steam Streaming Speakers";
    public const string SteamMic = "Steam Streaming Microphone";
    public const int MicPort = 9301;

    private readonly Action<string> _log;
    private readonly DefaultOutputSwitch _switch;
    private readonly object _lock = new();
    private string? _owner;
    private long _generation;
    private PcAudioStreamer? _streamer;
    private MicPlayer? _mic;
    private IPEndPoint? _target;

    public AudioHost(Action<string> log)
    {
        _log = log;
        _switch = new DefaultOutputSwitch(log);
    }

    public AudioStartResult Start(string deviceId, long generation, string address, ushort udpPort, bool playback, bool mic)
    {
        lock (_lock)
        {
            if (_owner != deviceId || _generation != generation) StopAll();
            _owner = deviceId;
            _generation = generation;
            using var enumerator = new MMDeviceEnumerator();
            var steamSpeakers = Find(enumerator, DataFlow.Render, SteamSpeakers);
            var steamMicRender = Find(enumerator, DataFlow.Render, SteamMic);
            var plan = AudioPlan.For(playback, mic, steamSpeakers != null, steamMicRender != null);
            if (playback && steamSpeakers == null) _log("áudio: drivers do Steam não encontrados; som também nas caixas do PC (ADR 0015)");
            if (mic && steamMicRender == null) _log("áudio: microfone indisponível sem o Steam Streaming Microphone (ADR 0015)");

            ApplyPlayback(plan, steamSpeakers, enumerator, new IPEndPoint(IPAddress.Parse(address), udpPort));
            ApplyMic(plan, steamMicRender);
            return new AudioStartResult((ushort)MicPort, steamSpeakers != null, plan.Mic);
        }
    }

    private void ApplyPlayback(AudioPlan plan, MMDevice? steamSpeakers, MMDeviceEnumerator enumerator, IPEndPoint target)
    {
        if (!plan.Stream)
        {
            _streamer?.Dispose();
            _streamer = null;
            _switch.Restore();
            return;
        }
        if (_streamer != null && target.Equals(_target)) return;
        _streamer?.Dispose();
        _streamer = null;
        _target = target;
        if (plan.SwitchDefaultOutput) _switch.SwitchTo(steamSpeakers!);
        var device = plan.CaptureSteamSpeakers ? steamSpeakers! : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        try
        {
            _streamer = new PcAudioStreamer(device, target, _log);
            _streamer.Start();
        }
        catch (Exception e)
        {
            _log($"áudio: falha ao capturar o som do PC: {e.Message}");
            _streamer = null;
        }
    }

    private void ApplyMic(AudioPlan plan, MMDevice? steamMicRender)
    {
        if (!plan.Mic)
        {
            _mic?.Dispose();
            _mic = null;
            return;
        }
        if (_mic != null) return;
        try { _mic = new MicPlayer(steamMicRender!, MicPort, _log); }
        catch (Exception e) { _log($"áudio: falha ao abrir o microfone virtual: {e.Message}"); }
    }

    public void Stop(string deviceId, long generation)
    {
        lock (_lock)
        {
            if (_owner != deviceId || _generation != generation) return;
            StopAll();
            _owner = null;
        }
    }

    public string TakeStats()
    {
        lock (_lock)
        {
            if (_streamer == null && _mic == null) return string.Empty;
            return $"áudio: PC→Quest {_streamer?.TakePackets() ?? 0} pkt/s · mic {_mic?.TakePackets() ?? 0} pkt/s";
        }
    }

    private void StopAll()
    {
        _streamer?.Dispose();
        _streamer = null;
        _target = null;
        _mic?.Dispose();
        _mic = null;
        _switch.Restore();
    }

    private static MMDevice? Find(MMDeviceEnumerator enumerator, DataFlow flow, string name)
    {
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            if (device.FriendlyName.Contains(name, StringComparison.OrdinalIgnoreCase)) return device;
        return null;
    }

    public void Dispose()
    {
        lock (_lock) StopAll();
    }
}
