using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ScreenPortal.Host.Domain.Pairing;

public sealed record PairedDevice(string DeviceId, string DeviceName, string Token, DateTimeOffset PairedAt);

/// <summary>Headsets pareados com este PC (spec 1B §2). Thread-safe: sessões rodam em threads diferentes.</summary>
public sealed class PairingStore
{
    private readonly Dictionary<string, PairedDevice> _devices = new();
    private readonly Func<byte[]> _randomBytes;
    private readonly object _lock = new();

    public PairingStore(IEnumerable<PairedDevice>? devices = null, Func<byte[]>? randomBytes = null)
    {
        _randomBytes = randomBytes ?? (() => RandomNumberGenerator.GetBytes(32));
        if (devices != null)
            foreach (var d in devices) _devices[d.DeviceId] = d;
    }

    public IReadOnlyCollection<PairedDevice> Devices
    {
        get { lock (_lock) return _devices.Values.ToArray(); }
    }

    public bool IsPaired(string deviceId, string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        lock (_lock)
        {
            if (!_devices.TryGetValue(deviceId, out var device)) return false;
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(device.Token), Encoding.UTF8.GetBytes(token));
        }
    }

    public string Pair(string deviceId, string deviceName, DateTimeOffset when)
    {
        string token = Convert.ToBase64String(_randomBytes());
        lock (_lock) _devices[deviceId] = new PairedDevice(deviceId, deviceName, token, when);
        return token;
    }

    public string Serialize()
    {
        lock (_lock) return JsonSerializer.Serialize(_devices.Values.ToArray(), new JsonSerializerOptions { WriteIndented = true });
    }

    public static PairingStore Deserialize(string json)
    {
        try { return new PairingStore(JsonSerializer.Deserialize<PairedDevice[]>(json)); }
        catch (JsonException) { return new PairingStore(); }
    }

    public static string NewPairingCode(Func<int, int> randomBelow) => randomBelow(1_000_000).ToString("D6");
}
