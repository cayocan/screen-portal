using ScreenPortal.Host.Domain.Screens;

namespace ScreenPortal.Host.Tests.Screens;

public class DeviceOwnershipTests
{
    [Fact]
    public void Claim_MakesTheNewGenerationCurrent_AndTheOlderOneStale()
    {
        var owners = new DeviceOwnership();
        long first = owners.Claim("dev-1", null, out _);
        Assert.True(owners.IsCurrent("dev-1", first));

        long second = owners.Claim("dev-1", null, out _);
        Assert.True(second > first);
        Assert.True(owners.IsCurrent("dev-1", second));
        Assert.False(owners.IsCurrent("dev-1", first));
    }

    [Fact]
    public void Devices_AreIndependent()
    {
        var owners = new DeviceOwnership();
        long a = owners.Claim("dev-a", null, out _);
        long b = owners.Claim("dev-b", null, out _);
        Assert.True(owners.IsCurrent("dev-a", a));
        Assert.True(owners.IsCurrent("dev-b", b));
        Assert.False(owners.IsCurrent("dev-a", b));
    }

    [Fact]
    public void UnclaimedDevice_OrUnknownGeneration_IsNotCurrent()
    {
        var owners = new DeviceOwnership();
        Assert.False(owners.IsCurrent("dev-1", 0));
        Assert.False(owners.IsCurrent("dev-1", 1));
    }

    [Fact]
    public void Claim_HandsBackThePreviousOwnersReplacedCallback()
    {
        var owners = new DeviceOwnership();
        bool firstReplaced = false;
        owners.Claim("dev-1", () => firstReplaced = true, out var none);
        Assert.Null(none);

        owners.Claim("dev-1", () => { }, out var replaced);
        Assert.NotNull(replaced);
        replaced!();
        Assert.True(firstReplaced);
    }
}
