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

public readonly record struct ScreenAcquireResult(bool Ok, int Width, int Height, string Error);

/// <summary>
/// Monitores e streams por headset (spec 2C §4, ADR 0009). Acquire/StartStream podem levar até ~3 s (criar monitor,
/// esperar a captura): a HeadsetSession chama fora da thread de leitura.
/// </summary>
public interface IScreenHost
{
    IReadOnlyList<ScreenLeaseInfo> Leases(string deviceId);
    ScreenAcquireResult Acquire(string deviceId, AcquireScreenMessage request);
    bool StartStream(string deviceId, uint screenKey, string address, ushort udpPort, ushort streamId, out int width, out int height);
    void Release(string deviceId, uint screenKey, TimeSpan grace);
    void Disconnected(string deviceId);
}
