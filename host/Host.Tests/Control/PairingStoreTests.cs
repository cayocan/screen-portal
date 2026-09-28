using ScreenPortal.Host.Domain.Pairing;

namespace ScreenPortal.Host.Tests.Control;

public class PairingStoreTests
{
    private static byte[] FixedBytes() => Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void Pair_IssuesToken_ThatIsRecognized()
    {
        var store = new PairingStore(randomBytes: FixedBytes);
        string token = store.Pair("dev-1", "Quest 3", DateTimeOffset.UnixEpoch);

        Assert.Equal(Convert.ToBase64String(FixedBytes()), token);
        Assert.True(store.IsPaired("dev-1", token));
        Assert.False(store.IsPaired("dev-1", "outro"));
        Assert.False(store.IsPaired("dev-2", token));
        Assert.False(store.IsPaired("dev-1", ""));
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        var store = new PairingStore(randomBytes: FixedBytes);
        string token = store.Pair("dev-1", "Quest 3", DateTimeOffset.UnixEpoch);
        var restored = PairingStore.Deserialize(store.Serialize());

        Assert.True(restored.IsPaired("dev-1", token));
        Assert.Equal("Quest 3", restored.Devices.Single().DeviceName);
    }

    [Fact]
    public void Deserialize_CorruptFile_StartsEmpty() =>
        Assert.Empty(PairingStore.Deserialize("{lixo").Devices);

    [Fact]
    public void PairingCode_HasSixDigits() =>
        Assert.Equal("000042", PairingStore.NewPairingCode(_ => 42));
}
