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
/// Posse por headset: a sessão que recebe Welcome chama <see cref="Claim"/> e passa a geração em toda chamada de tela;
/// o host ignora chamadas de uma geração antiga (a sessão anterior do mesmo device depois de uma reconexão).
/// </summary>
public interface IScreenHost
{
    IReadOnlyList<ScreenLeaseInfo> Leases(string deviceId);
    /// <summary>
    /// Torna a sessão chamadora a dona do device e devolve a geração dela. Se havia outra dona, o
    /// <paramref name="onReplaced"/> dela é chamado (fora de locks). Não espera o trabalho de telas (não bloqueia a
    /// thread de leitura).
    /// </summary>
    long Claim(string deviceId, Action onReplaced);
    ScreenAcquireResult Acquire(string deviceId, long generation, AcquireScreenMessage request);
    bool StartStream(string deviceId, long generation, uint screenKey, string address, ushort udpPort, ushort streamId, out int width, out int height);
    void Release(string deviceId, long generation, uint screenKey, TimeSpan grace);
    void Disconnected(string deviceId, long generation);
}
