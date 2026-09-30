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

    /// <summary>
    /// Decoder de vídeo de uma tela (nativo no Quest; no-op no Editor). TryOpen devolve false enquanto a Surface da
    /// layer não existe. HasFirstFrame é consultado ~1×/s pelo HostSync até o primeiro quadro decodificado.
    /// </summary>
    public interface IScreenVideo
    {
        bool TryOpen(ScreenId screen, ushort streamId, int udpPort, Resolution resolution);
        void Close(ScreenId screen);
        bool HasFirstFrame(ScreenId screen);
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
