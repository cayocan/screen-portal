namespace ScreenPortal.Host.Domain.Screens;

public enum ScreenLeaseKind { Virtual, Mirror }

/// <summary>Um monitor (virtual ou espelho) mantido para uma tela de um headset. ExpiresAt null = em uso.</summary>
public sealed class ScreenLease(string deviceId, uint screenKey, ScreenLeaseKind kind, int width, int height,
    uint physicalMonitorId, string displayName)
{
    public string DeviceId { get; } = deviceId;
    public uint ScreenKey { get; } = screenKey;
    public ScreenLeaseKind Kind { get; } = kind;
    /// <summary>Resolução do monitor (virtual: a pedida; espelho: a do monitor físico ajustada).</summary>
    public int Width { get; } = width;
    public int Height { get; } = height;
    public uint PhysicalMonitorId { get; } = physicalMonitorId;
    /// <summary><c>\\.\DISPLAYn</c> capturado.</summary>
    public string DisplayName { get; } = displayName;
    public DateTimeOffset? ExpiresAt { get; internal set; }
}

/// <summary>
/// Aluguéis de monitor por (headset, tela) (spec 2C §4, ADR 0009). Puro: o relógio vem de fora. Não é thread-safe
/// (o ScreenHost serializa o acesso).
/// </summary>
public sealed class ScreenLeaseTable
{
    private readonly Dictionary<(string, uint), ScreenLease> _leases = new();

    public int Count => _leases.Count;

    public bool TryGet(string deviceId, uint screenKey, out ScreenLease lease) =>
        _leases.TryGetValue((deviceId, screenKey), out lease!);

    public void Add(ScreenLease lease) => _leases[(lease.DeviceId, lease.ScreenKey)] = lease;

    public bool Remove(string deviceId, uint screenKey) => _leases.Remove((deviceId, screenKey));

    /// <summary>A tela voltou (undo ou reconexão): cancela a expiração.</summary>
    public void Renew(string deviceId, uint screenKey)
    {
        if (TryGet(deviceId, screenKey, out var lease)) lease.ExpiresAt = null;
    }

    /// <summary>Agenda a remoção; se já havia um prazo mais cedo, ele fica.</summary>
    public void Expire(string deviceId, uint screenKey, DateTimeOffset at)
    {
        if (TryGet(deviceId, screenKey, out var lease)) Schedule(lease, at);
    }

    public void ExpireAll(string deviceId, DateTimeOffset at)
    {
        foreach (var lease in _leases.Values)
            if (lease.DeviceId == deviceId) Schedule(lease, at);
    }

    public IReadOnlyList<ScreenLease> ForDevice(string deviceId) =>
        _leases.Values.Where(l => l.DeviceId == deviceId).OrderBy(l => l.ScreenKey).ToList();

    /// <summary>Remove e devolve os aluguéis vencidos.</summary>
    public IReadOnlyList<ScreenLease> TakeExpired(DateTimeOffset now)
    {
        var expired = _leases.Values.Where(l => l.ExpiresAt is { } at && at <= now).ToList();
        foreach (var lease in expired) _leases.Remove((lease.DeviceId, lease.ScreenKey));
        return expired;
    }

    private static void Schedule(ScreenLease lease, DateTimeOffset at)
    {
        if (lease.ExpiresAt is { } current && current <= at) return;
        lease.ExpiresAt = at;
    }
}
