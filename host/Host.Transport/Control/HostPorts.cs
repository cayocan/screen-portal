using ScreenPortal.App.Connection;

namespace ScreenPortal.Host.Transport.Control;

/// <summary>Pergunta ao usuário do PC se o headset pode ver a tela (spec 1B §2).</summary>
public interface IPairingApprover
{
    Task<bool> ApproveAsync(string deviceName, string code);
}

public interface IMonitorProvider
{
    IReadOnlyList<MonitorDescriptor> GetMonitors();
}

/// <summary>Pipelines de vídeo por headset. TryStart reaproveita o pipeline do mesmo device (reconexão).</summary>
public interface IStreamRegistry
{
    bool TryStart(string deviceId, string address, ushort udpPort, ushort streamId, MonitorDescriptor monitor, out int width, out int height);
    void Release(string deviceId);
}
