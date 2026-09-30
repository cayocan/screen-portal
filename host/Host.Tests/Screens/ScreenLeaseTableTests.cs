using ScreenPortal.Host.Domain.Screens;

namespace ScreenPortal.Host.Tests.Screens;

public class ScreenLeaseTableTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static ScreenLease Virtual(string device, uint key) =>
        new(device, key, ScreenLeaseKind.Virtual, 2560, 1440, 0, @"\\.\DISPLAY5");

    [Fact]
    public void NewLease_IsLive_AndListedForItsDevice()
    {
        var table = new ScreenLeaseTable();
        table.Add(Virtual("dev-1", 2));
        table.Add(Virtual("dev-1", 1));
        Assert.True(table.TryGet("dev-1", 2, out var lease));
        Assert.Null(lease.ExpiresAt);
        Assert.Equal(new uint[] { 1, 2 }, table.ForDevice("dev-1").Select(l => l.ScreenKey));
    }

    [Fact]
    public void Expire_OnlyTakenAfterTheDeadline()
    {
        var table = new ScreenLeaseTable();
        table.Add(Virtual("dev-1", 1));
        table.Expire("dev-1", 1, T0.AddSeconds(10));
        Assert.Empty(table.TakeExpired(T0.AddSeconds(9)));
        var expired = table.TakeExpired(T0.AddSeconds(10));
        Assert.Single(expired);
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public void Renew_CancelsTheExpiry()
    {
        var table = new ScreenLeaseTable();
        table.Add(Virtual("dev-1", 1));
        table.Expire("dev-1", 1, T0.AddSeconds(10));
        table.Renew("dev-1", 1);
        Assert.Empty(table.TakeExpired(T0.AddMinutes(10)));
    }

    [Fact]
    public void ExpireAll_KeepsTheEarlierDeadline()
    {
        var table = new ScreenLeaseTable();
        table.Add(Virtual("dev-1", 1));
        table.Add(Virtual("dev-1", 2));
        table.Expire("dev-1", 1, T0.AddSeconds(10));       // removida
        table.ExpireAll("dev-1", T0.AddMinutes(5));         // depois o headset caiu
        Assert.Equal(T0.AddSeconds(10), table.ForDevice("dev-1")[0].ExpiresAt);
        Assert.Equal(T0.AddMinutes(5), table.ForDevice("dev-1")[1].ExpiresAt);
    }

    [Fact]
    public void Devices_AreIsolated()
    {
        var table = new ScreenLeaseTable();
        table.Add(Virtual("dev-1", 1));
        table.Add(Virtual("dev-2", 1));
        table.ExpireAll("dev-1", T0);
        Assert.Single(table.TakeExpired(T0));
        Assert.True(table.TryGet("dev-2", 1, out _));
    }
}
