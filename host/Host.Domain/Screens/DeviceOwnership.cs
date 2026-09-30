namespace ScreenPortal.Host.Domain.Screens;

/// <summary>
/// Qual sessão de controle fala por cada headset (spec 2C §4, ADR 0009). Cada sessão recebida com Welcome reivindica o
/// device id e ganha uma geração nova; trabalho de uma geração antiga (o fechamento atrasado do socket antigo depois de
/// uma reconexão, um job preso na fila antiga) é ignorado. Thread-safe.
/// </summary>
public sealed class DeviceOwnership
{
    private readonly Dictionary<string, (long Generation, Action? OnReplaced)> _owners = new();
    private readonly object _lock = new();
    private long _next;

    /// <summary>
    /// Torna a sessão chamadora a dona de <paramref name="deviceId"/>. <paramref name="replaced"/> recebe o callback da
    /// dona anterior (se houver) para o chamador invocar fora de qualquer lock.
    /// </summary>
    public long Claim(string deviceId, Action? onReplaced, out Action? replaced)
    {
        lock (_lock)
        {
            replaced = _owners.TryGetValue(deviceId, out var previous) ? previous.OnReplaced : null;
            long generation = ++_next;
            _owners[deviceId] = (generation, onReplaced);
            return generation;
        }
    }

    public bool IsCurrent(string deviceId, long generation)
    {
        lock (_lock)
            return _owners.TryGetValue(deviceId, out var owner) && owner.Generation == generation;
    }
}
