using ScreenPortal.Host.Domain.Screens;

namespace ScreenPortal.Host.Tests.Screens;

public class MonitorRecreationTests
{
    private sealed record Monitor(string Name);

    private sealed class Fakes : IVirtualMonitors<Monitor>, IWindowMover<Monitor>
    {
        public readonly List<string> Calls = new();
        public bool FailCreate, FailMove, FailRemove;

        public Monitor Create(int width, int height)
        {
            Calls.Add($"create {width}×{height}");
            if (FailCreate) throw new InvalidOperationException("IOCTL falhou");
            return new Monitor(@"\\.\DISPLAY9");
        }

        public void Remove(Monitor monitor)
        {
            Calls.Add("remove " + monitor.Name);
            if (FailRemove) throw new InvalidOperationException("não saiu");
        }

        public string DisplayName(Monitor monitor) => monitor.Name;

        public int MoveWindows(Monitor from, Monitor to)
        {
            Calls.Add($"move {from.Name} → {to.Name}");
            if (FailMove) throw new InvalidOperationException("acesso negado");
            return 2;
        }
    }

    private static readonly Monitor Old = new(@"\\.\DISPLAY5");

    private static (ScreenLeaseTable, ScreenLease) Leased()
    {
        var leases = new ScreenLeaseTable();
        var lease = new ScreenLease("dev-1", 3, ScreenLeaseKind.Virtual, 2560, 1440, 0, Old.Name);
        leases.Add(lease);
        return (leases, lease);
    }

    private static (MonitorRecreator<Monitor>, List<string> log) Recreator(Fakes fakes)
    {
        var log = new List<string>();
        return (new MonitorRecreator<Monitor>(fakes, fakes, log.Add), log);
    }

    [Fact]
    public void Recreate_CreatesTheNewFirst_MovesTheWindows_ThenStopsAndRemovesTheOld()
    {
        var fakes = new Fakes();
        var (recreator, _) = Recreator(fakes);
        var (leases, lease) = Leased();

        var result = recreator.Recreate(leases, lease, Old, 1920, 1080, () => fakes.Calls.Add("stop"));

        Assert.True(result.Ok);
        Assert.Equal(@"\\.\DISPLAY9", result.Monitor!.Name);
        Assert.Equal(2, result.WindowsMoved);
        Assert.Equal(new[] { "create 1920×1080", @"move \\.\DISPLAY5 → \\.\DISPLAY9", "stop", @"remove \\.\DISPLAY5" }, fakes.Calls);
    }

    [Fact]
    public void Recreate_TheLeaseKeepsTheKey_WithTheNewSizeAndDisplay_AndIsLive()
    {
        var fakes = new Fakes();
        var (recreator, _) = Recreator(fakes);
        var (leases, lease) = Leased();
        leases.Expire("dev-1", 3, DateTimeOffset.UnixEpoch.AddSeconds(10)); // estava removida (undo dentro do prazo)

        recreator.Recreate(leases, lease, Old, 1080, 1920, () => { });

        Assert.Equal(1, leases.Count);
        Assert.True(leases.TryGet("dev-1", 3, out var now));
        Assert.Equal(ScreenLeaseKind.Virtual, now.Kind);
        Assert.Equal((1080, 1920), (now.Width, now.Height));
        Assert.Equal(@"\\.\DISPLAY9", now.DisplayName);
        Assert.Null(now.ExpiresAt);
    }

    [Fact]
    public void CreateFails_KeepsTheOldMonitorAndLease_AndReportsTheFailure()
    {
        var fakes = new Fakes { FailCreate = true };
        var (recreator, _) = Recreator(fakes);
        var (leases, lease) = Leased();
        bool stopped = false;

        var result = recreator.Recreate(leases, lease, Old, 1920, 1080, () => stopped = true);

        Assert.False(result.Ok);
        Assert.Null(result.Monitor);
        Assert.Contains("IOCTL falhou", result.Error);
        Assert.Equal(new[] { "create 1920×1080" }, fakes.Calls);
        Assert.False(stopped);
        Assert.True(leases.TryGet("dev-1", 3, out var kept));
        Assert.Same(lease, kept);
    }

    [Fact]
    public void MoveOrRemoveFailing_StillSwapsToTheNewMonitor_AndLogs()
    {
        var fakes = new Fakes { FailMove = true, FailRemove = true };
        var (recreator, log) = Recreator(fakes);
        var (leases, lease) = Leased();

        var result = recreator.Recreate(leases, lease, Old, 1920, 1080, () => fakes.Calls.Add("stop"));

        Assert.True(result.Ok);
        Assert.Equal(0, result.WindowsMoved);
        Assert.Contains("stop", fakes.Calls);
        Assert.Contains(@"remove \\.\DISPLAY5", fakes.Calls);
        Assert.True(leases.TryGet("dev-1", 3, out var now));
        Assert.Equal(@"\\.\DISPLAY9", now.DisplayName);
        Assert.Equal(2, log.Count);
        Assert.Contains(log, l => l.Contains("acesso negado"));
        Assert.Contains(log, l => l.Contains("não saiu"));
    }
}
