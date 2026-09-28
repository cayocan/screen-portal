using ScreenPortal.App.Connection;

namespace ScreenPortal.Host.Transport.Control;

/// <summary>Conexão de controle com um headset (lado do host). Send é thread-safe.</summary>
public interface IControlConnection
{
    string RemoteAddress { get; }
    void Send(ControlMessage message);
    void Close();
}
