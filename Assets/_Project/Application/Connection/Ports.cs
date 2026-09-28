using System;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Connection
{
    public enum ChannelEventKind
    {
        None,
        Connected,
        ConnectFailed,
        Message,
        Disconnected,
    }

    /// <summary>Canal de controle (TCP). Eventos são consumidos por Poll na thread principal.</summary>
    public interface IControlChannel
    {
        void Connect(HostEndpoint host);
        void Send(ControlMessage message);
        void Close();
        ChannelEventKind Poll(out ControlMessage message);
    }

    /// <summary>Descoberta do host na rede (mDNS no Quest).</summary>
    public interface IHostDiscovery
    {
        void Start();
        void Stop();
        bool TryGetHost(out HostEndpoint host);
    }

    /// <summary>Identidade do headset e tokens de pareamento por host.</summary>
    public interface IPairingStore
    {
        string DeviceId { get; }
        string GetToken(string hostName);
        void SaveToken(string hostName, string token);
        bool TryGetLastHost(out HostEndpoint host);
        void SaveLastHost(HostEndpoint host);
    }

    /// <summary>Decoder de vídeo de uma tela. TryStart devolve false enquanto a Surface da layer não existe.</summary>
    public interface IVideoStreamSink
    {
        bool TryStart(ScreenId screen, ushort streamId, int udpPort, Resolution resolution);
        void Stop(ushort streamId);
    }

    /// <summary>Implementado pela Presentation: a Surface (jobject) da compositor layer de uma tela.</summary>
    public interface IScreenSurfaceProvider
    {
        bool TryGetSurface(ScreenId screen, out IntPtr surface);
    }

    public interface IClock
    {
        double NowSeconds { get; }
    }
}
