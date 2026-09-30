using ScreenPortal.App.Connection;
using ScreenPortal.Host.Domain.Pairing;
using ScreenPortal.Host.Transport.Control;

namespace ScreenPortal.Host.Tests.Control;

public class HeadsetSessionTests
{
    internal sealed class FakeConnection : IControlConnection
    {
        public readonly List<ControlMessage> Sent = new();
        public bool Closed;
        public string RemoteAddress => "192.168.0.171";
        public void Send(ControlMessage message) { lock (Sent) Sent.Add(message); }
        public void Close() => Closed = true;
        public T? Last<T>() where T : ControlMessage { lock (Sent) return Sent.OfType<T>().LastOrDefault(); }
    }

    internal sealed class FakeApprover(bool answer) : IPairingApprover
    {
        public string? LastCode;
        public Task<bool> ApproveAsync(string deviceName, string code) { LastCode = code; return Task.FromResult(answer); }
    }

    internal sealed class FakeMonitors : IMonitorProvider
    {
        public IReadOnlyList<MonitorDescriptor> GetMonitors() => new[] { new MonitorDescriptor(1, 2560, 1080, "DISPLAY1", true) };
    }

    internal sealed class FakeScreens : IScreenHost
    {
        public readonly List<string> Calls = new();
        public bool FailAcquire;
        public string? LastAddress;
        public ManualResetEventSlim? BlockAcquire;
        public List<ScreenLeaseInfo> LeasesToReport = new();

        public IReadOnlyList<ScreenLeaseInfo> Leases(string deviceId) => LeasesToReport;

        public ScreenAcquireResult Acquire(string deviceId, AcquireScreenMessage request)
        {
            BlockAcquire?.Wait(5000);
            lock (Calls) Calls.Add($"acquire {request.ScreenKey}");
            return FailAcquire ? new ScreenAcquireResult(false, 0, 0, "sem driver")
                               : new ScreenAcquireResult(true, (int)request.Width, (int)request.Height, "");
        }

        public bool StartStream(string deviceId, uint screenKey, string address, ushort udpPort, ushort streamId, out int width, out int height)
        {
            lock (Calls) Calls.Add($"start {screenKey}:{udpPort}");
            LastAddress = address;
            width = 2560;
            height = 1440;
            return true;
        }

        public void Release(string deviceId, uint screenKey, TimeSpan grace)
        {
            lock (Calls) Calls.Add($"release {screenKey} {grace.TotalMilliseconds}");
        }

        public void Disconnected(string deviceId)
        {
            lock (Calls) Calls.Add($"disconnected {deviceId}");
        }
    }

    private static (HeadsetSession session, FakeConnection conn, PairingStore pairing, FakeScreens screens, FakeApprover approver) Create(bool approve)
    {
        var conn = new FakeConnection();
        var pairing = new PairingStore();
        var screens = new FakeScreens();
        var approver = new FakeApprover(approve);
        var session = new HeadsetSession(conn, pairing, () => { }, approver, new FakeMonitors(), screens, "CAYO-PC", () => "482913", _ => { });
        return (session, conn, pairing, screens, approver);
    }

    private static HeadsetSession Welcomed(out FakeConnection conn, out FakeScreens screens)
    {
        var (session, c, pairing, s, _) = Create(approve: false);
        session.OnMessage(new HelloMessage("dev-1", "Quest 3", pairing.Pair("dev-1", "Quest 3", DateTimeOffset.UnixEpoch)));
        conn = c;
        screens = s;
        return session;
    }

    [Fact]
    public void UnknownDevice_IsAskedToPair_ThenWelcomedWithToken()
    {
        var (session, conn, pairing, _, approver) = Create(approve: true);
        session.OnMessage(new HelloMessage("dev-1", "Quest 3", ""));

        Assert.Equal("482913", conn.Last<PairingRequiredMessage>()!.Code);
        Assert.Equal("482913", approver.LastCode);
        var result = conn.Last<PairingResultMessage>()!;
        Assert.True(result.Accepted);
        Assert.True(pairing.IsPaired("dev-1", result.PairingToken));
        Assert.Equal("CAYO-PC", conn.Last<WelcomeMessage>()!.HostName);
    }

    [Fact]
    public void Rejected_SendsByeAndCloses()
    {
        var (session, conn, _, _, _) = Create(approve: false);
        session.OnMessage(new HelloMessage("dev-1", "Quest 3", ""));

        Assert.False(conn.Last<PairingResultMessage>()!.Accepted);
        Assert.Equal(ByeReason.PairingRejected, conn.Last<ByeMessage>()!.Reason);
        Assert.Null(conn.Last<WelcomeMessage>());
        Assert.True(conn.Closed);
    }


    [Fact]
    public void KnownDevice_IsWelcomedWithLeases_ThenAcquiresStreamsReleasesAndDisconnects()
    {
        var (session, conn, pairing, screens, _) = Create(approve: false);
        screens.LeasesToReport.Add(new ScreenLeaseInfo(7, ScreenKind.Virtual, 3840, 2160));
        session.OnMessage(new HelloMessage("dev-1", "Quest 3", pairing.Pair("dev-1", "Quest 3", DateTimeOffset.UnixEpoch)));
        Assert.Null(conn.Last<PairingRequiredMessage>());
        Assert.Equal(7u, conn.Last<WelcomeMessage>()!.Leases[0].ScreenKey);

        session.OnMessage(new AcquireScreenMessage(7, ScreenKind.Virtual, 3840, 2160, 0));
        session.OnMessage(new StartStreamMessage(7, 9000, 0));
        session.OnMessage(new ReleaseScreenMessage(7, 10000));
        session.Idle.Wait();
        Assert.Equal(2160u, conn.Last<ScreenReadyMessage>()!.Height);
        Assert.Equal(1440u, conn.Last<StreamStartedMessage>()!.Height);
        Assert.Equal("192.168.0.171", screens.LastAddress);
        Assert.Equal(new[] { "acquire 7", "start 7:9000", "release 7 10000" }, screens.Calls);

        session.OnMessage(new PingMessage(9));
        Assert.Equal(9UL, conn.Last<PongMessage>()!.SentUs);

        session.OnClosed();
        session.Idle.Wait();
        Assert.Equal("disconnected dev-1", screens.Calls[^1]);
    }

    [Fact]
    public void AcquireFailure_ReportsScreenFailedWithTheReason()
    {
        var session = Welcomed(out var conn, out var screens);
        screens.FailAcquire = true;
        session.OnMessage(new AcquireScreenMessage(3, ScreenKind.Virtual, 2560, 1440, 0));
        session.Idle.Wait();
        var failed = conn.Last<ScreenFailedMessage>()!;
        Assert.Equal(3u, failed.ScreenKey);
        Assert.Equal("sem driver", failed.Reason);
    }

    [Fact]
    public void ScreenMessages_BeforeWelcome_AreIgnored()
    {
        var (session, conn, _, screens, _) = Create(approve: false);
        session.OnMessage(new AcquireScreenMessage(1, ScreenKind.Virtual, 2560, 1440, 0));
        session.OnMessage(new StartStreamMessage(1, 9000, 0));
        session.Idle.Wait();
        Assert.Empty(screens.Calls);
        Assert.Null(conn.Last<ScreenReadyMessage>());
    }

    [Fact]
    public void SlowAcquire_DoesNotBlockPingOnTheReadThread()
    {
        var session = Welcomed(out var conn, out var screens);
        screens.BlockAcquire = new ManualResetEventSlim(false);
        session.OnMessage(new AcquireScreenMessage(1, ScreenKind.Virtual, 2560, 1440, 0)); // volta na hora
        session.OnMessage(new PingMessage(5));
        Assert.Equal(5UL, conn.Last<PongMessage>()!.SentUs);
        Assert.Null(conn.Last<ScreenReadyMessage>());
        screens.BlockAcquire.Set();
        session.Idle.Wait();
        Assert.NotNull(conn.Last<ScreenReadyMessage>());
    }

    [Fact]
    public void ReleaseIsFullyHandled_BeforeALaterStartStream()
    {
        var session = Welcomed(out _, out var screens);
        session.OnMessage(new ReleaseScreenMessage(1, 0));
        session.OnMessage(new StartStreamMessage(2, 9000, 0));
        session.Idle.Wait();
        Assert.Equal(new[] { "release 1 0", "start 2:9000" }, screens.Calls);
    }
}
