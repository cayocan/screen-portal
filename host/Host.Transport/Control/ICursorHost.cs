using ScreenPortal.App.Connection;

namespace ScreenPortal.Host.Transport.Control;

/// <summary>
/// Cursor no headset (spec 3B): a sessão com Welcome pede o <c>MouseState</c> (UDP) e recebe as formas novas pelo
/// controle (<paramref name="send"/>). Uma sessão substituída que para depois não derruba a nova (owner).
/// </summary>
public interface ICursorHost
{
    void Start(string deviceId, object owner, string address, ushort udpPort, Action<ControlMessage> send);
    void Stop(string deviceId, object owner);
}
