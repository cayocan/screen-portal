using ScreenPortal.App.Connection;

namespace ScreenPortal.Host.Transport.Control;

/// <summary>
/// Atalhos (spec 3A §5): a sessão atual de cada headset (a que recebeu Welcome por último) recebe o
/// <see cref="HotkeyMessage"/>. Uma sessão substituída que se desregistra depois não apaga a nova.
/// </summary>
public sealed class HotkeyRouter
{
    private readonly Dictionary<string, (object Owner, Action<ControlMessage> Send)> _sessions = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public void Register(string deviceId, object owner, Action<ControlMessage> send)
    {
        lock (_lock) _sessions[deviceId] = (owner, send);
    }

    public void Unregister(string deviceId, object owner)
    {
        lock (_lock)
            if (_sessions.TryGetValue(deviceId, out var s) && ReferenceEquals(s.Owner, owner)) _sessions.Remove(deviceId);
    }

    public IReadOnlyList<string> Devices
    {
        get { lock (_lock) return _sessions.Keys.ToList(); }
    }

    /// <returns>false se o headset não está conectado.</returns>
    public bool Send(string deviceId, ControlMessage message)
    {
        Action<ControlMessage> send;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(deviceId, out var s)) return false;
            send = s.Send;
        }
        send(message);
        return true;
    }
}
