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

    internal sealed class FakeStreams : IStreamRegistry
    {
        public int Starts, Releases;
        public string? LastAddress;
        public bool TryStart(string deviceId, string address, ushort udpPort, ushort streamId, MonitorDescriptor monitor, out int width, out int height)
        {
            Starts++;
            LastAddress = address;
            width = 2560;
            height = 1080;
            return true;
        }
        public void Release(string deviceId) => Releases++;
    }

    private static (HeadsetSession session, FakeConnection conn, PairingStore pairing, FakeStreams streams, FakeApprover approver) Create(bool approve)
    {
        var conn = new FakeConnection();
        var pairing = new PairingStore();
        var streams = new FakeStreams();
        var approver = new FakeApprover(approve);
        var session = new HeadsetSession(conn, pairing, () => { }, approver, new FakeMonitors(), streams, "CAYO-PC", () => "482913", _ => { });
        return (session, conn, pairing, streams, approver);
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
    public void KnownDevice_IsWelcomedDirectly_AndCanStream()
    {
        var (session, conn, pairing, streams, _) = Create(approve: false);
        string token = pairing.Pair("dev-1", "Quest 3", DateTimeOffset.UnixEpoch);
        session.OnMessage(new HelloMessage("dev-1", "Quest 3", token));

        Assert.Null(conn.Last<PairingRequiredMessage>());
        Assert.NotNull(conn.Last<WelcomeMessage>());

        session.OnMessage(new StartStreamMessage(1, 9000, 0));
        Assert.Equal(1, streams.Starts);
        Assert.Equal("192.168.0.171", streams.LastAddress);
        Assert.Equal(1080u, conn.Last<StreamStartedMessage>()!.Height);

        session.OnMessage(new PingMessage(9));
        Assert.Equal(9UL, conn.Last<PongMessage>()!.SentUs);

        session.OnClosed();
        Assert.Equal(1, streams.Releases);
    }

    [Fact]
    public void StartStream_BeforeWelcome_IsIgnored()
    {
        var (session, conn, _, streams, _) = Create(approve: false);
        session.OnMessage(new StartStreamMessage(1, 9000, 0));
        Assert.Equal(0, streams.Starts);
        Assert.Null(conn.Last<StreamStartedMessage>());
    }

    [Fact]
    public void UnknownMonitor_ReportsStreamStopped()
    {
        var (session, conn, pairing, _, _) = Create(approve: false);
        session.OnMessage(new HelloMessage("dev-1", "Quest", pairing.Pair("dev-1", "Quest", DateTimeOffset.UnixEpoch)));
        session.OnMessage(new StartStreamMessage(99, 9000, 0));
        Assert.NotNull(conn.Last<StreamStoppedMessage>());
    }
}
