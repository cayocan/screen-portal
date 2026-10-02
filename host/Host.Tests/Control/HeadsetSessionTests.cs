using ScreenPortal.App.Connection;
using ScreenPortal.Host.Domain.Pairing;
using ScreenPortal.Host.Domain.Screens;
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
        public readonly DeviceOwnership Owners = new();
        public bool FailAcquire;
        public string? LastAddress;
        public ManualResetEventSlim? BlockAcquire;
        public ManualResetEventSlim? BlockRelease;
        public List<ScreenLeaseInfo> LeasesToReport = new();
        /// <summary>Geração recebida em cada chamada de tela (na ordem de <see cref="Calls"/>, sem os claims).</summary>
        public readonly List<long> Generations = new();

        public IReadOnlyList<ScreenLeaseInfo> Leases(string deviceId) => LeasesToReport;

        public long Claim(string deviceId, Action onReplaced)
        {
            long generation = Owners.Claim(deviceId, onReplaced, out var replaced);
            replaced?.Invoke();
            return generation;
        }

        public ScreenAcquireResult Acquire(string deviceId, long generation, AcquireScreenMessage request)
        {
            BlockAcquire?.Wait(5000);
            lock (Calls) { Calls.Add($"acquire {request.ScreenKey}"); Generations.Add(generation); }
            return FailAcquire ? new ScreenAcquireResult(false, 0, 0, "sem driver")
                               : new ScreenAcquireResult(true, (int)request.Width, (int)request.Height, "");
        }

        public bool StartStream(string deviceId, long generation, uint screenKey, string address, ushort udpPort, ushort streamId, out int width, out int height)
        {
            lock (Calls) { Calls.Add($"start {screenKey}:{udpPort}"); Generations.Add(generation); }
            LastAddress = address;
            width = 2560;
            height = 1440;
            return true;
        }

        public void Release(string deviceId, long generation, uint screenKey, TimeSpan grace)
        {
            BlockRelease?.Wait(5000);
            lock (Calls) { Calls.Add($"release {screenKey} {grace.TotalMilliseconds}"); Generations.Add(generation); }
        }

        public event Action<string, uint, string>? ScreenLost;
        public void Lose(string deviceId, uint key, string reason) => ScreenLost?.Invoke(deviceId, key, reason);

        public void Arrange(string deviceId, long generation, IReadOnlyList<ScreenAnglesInfo> screens)
        {
            lock (Calls) { Calls.Add($"arrange {screens.Count}"); Generations.Add(generation); }
        }

        public void SetRate(string deviceId, long generation, uint screenKey, int fps, int kbps)
        {
            lock (Calls) { Calls.Add($"rate {screenKey} {fps} {kbps}"); Generations.Add(generation); }
        }

        public void Disconnected(string deviceId, long generation)
        {
            lock (Calls) { Calls.Add($"disconnected {deviceId}"); Generations.Add(generation); }
        }
    }

    private sealed class FakeAudio : IAudioHost
    {
        public readonly List<string> Calls = new();

        public AudioStartResult Start(string deviceId, long generation, string address, ushort udpPort, bool playback, bool mic)
        {
            lock (Calls) Calls.Add($"start {address}:{udpPort} {playback} {mic}");
            return new AudioStartResult(9301, true, mic);
        }

        public void Stop(string deviceId, long generation)
        {
            lock (Calls) Calls.Add($"stop {deviceId}");
        }
    }

    [Fact]
    public async Task StartAudio_StartsTheHostAudio_RepliesAudioStarted_AndClosingStopsIt()
    {
        var conn = new FakeConnection();
        var pairing = new PairingStore();
        var audio = new FakeAudio();
        var session = new HeadsetSession(conn, pairing, () => { }, new FakeApprover(true), new FakeMonitors(), new FakeScreens(), "CAYO-PC",
            () => "482913", _ => { }, audio);
        session.OnMessage(new HelloMessage("dev-1", "Quest", ""));
        await session.Idle.WaitAsync(TimeSpan.FromSeconds(5));
        session.OnMessage(new StartAudioMessage(9300, true, true));
        await session.Idle.WaitAsync(TimeSpan.FromSeconds(5));
        var started = conn.Last<AudioStartedMessage>();
        Assert.NotNull(started);
        Assert.Equal((ushort)9301, started!.MicPort);
        Assert.True(started.Mic);
        session.OnClosed();
        await session.Idle.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "start 192.168.0.171:9300 True True", "stop dev-1" }, audio.Calls);
    }

    [Fact]
    public void ClosedWindowMirror_TellsTheHeadset_OnlyItsOwn()
    {
        var session = Welcomed(out var conn, out var screens);
        screens.Lose("dev-1", 9, "A janela foi fechada.");
        screens.Lose("outro", 3, "A janela foi fechada.");
        var failed = conn.Sent.OfType<ScreenFailedMessage>().ToList();
        Assert.Single(failed);
        Assert.Equal(9u, failed[0].ScreenKey);
        session.OnClosed();
        screens.Lose("dev-1", 10, "A janela foi fechada.");
        Assert.Single(conn.Sent.OfType<ScreenFailedMessage>());
    }

    [Fact]
    public void Welcomed_ReceivesHotkeys_UntilClosed()
    {
        var conn = new FakeConnection();
        var pairing = new PairingStore();
        var router = new HotkeyRouter();
        var session = new HeadsetSession(conn, pairing, () => { }, new FakeApprover(false), new FakeMonitors(), new FakeScreens(), "CAYO-PC",
            () => "482913", _ => { }, null, router);
        Assert.False(router.Send("dev-1", new HotkeyMessage(HotkeyAction.Undo, 0)), "antes do Welcome");
        session.OnMessage(new HelloMessage("dev-1", "Quest 3", pairing.Pair("dev-1", "Quest 3", DateTimeOffset.UnixEpoch)));
        Assert.True(router.Send("dev-1", new HotkeyMessage(HotkeyAction.MoveLeft, 7)));
        Assert.Equal(HotkeyAction.MoveLeft, conn.Last<HotkeyMessage>()!.Action);
        session.OnClosed();
        Assert.False(router.Send("dev-1", new HotkeyMessage(HotkeyAction.Undo, 0)));
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
    public async Task KnownDevice_IsWelcomedWithLeases_ThenAcquiresStreamsReleasesAndDisconnects()
    {
        var (session, conn, pairing, screens, _) = Create(approve: false);
        screens.LeasesToReport.Add(new ScreenLeaseInfo(7, ScreenKind.Virtual, 3840, 2160));
        session.OnMessage(new HelloMessage("dev-1", "Quest 3", pairing.Pair("dev-1", "Quest 3", DateTimeOffset.UnixEpoch)));
        Assert.Null(conn.Last<PairingRequiredMessage>());
        Assert.Equal(7u, conn.Last<WelcomeMessage>()!.Leases[0].ScreenKey);

        session.OnMessage(new AcquireScreenMessage(7, ScreenKind.Virtual, 3840, 2160, 0));
        session.OnMessage(new StartStreamMessage(7, 9000, 0));
        session.OnMessage(new SetStreamRateMessage(7, 15, 900000)); // kbps fora da faixa: o host limita
        session.OnMessage(new LayoutChangedMessage(new[] { new ScreenAnglesInfo(7, 0, 0, 20, 10) }));
        session.OnMessage(new ReleaseScreenMessage(7, 10000));
        await session.Idle.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2160u, conn.Last<ScreenReadyMessage>()!.Height);
        Assert.Equal(1440u, conn.Last<StreamStartedMessage>()!.Height);
        Assert.Equal("192.168.0.171", screens.LastAddress);
        Assert.Equal(new[] { "acquire 7", "start 7:9000", "rate 7 15 50000", "arrange 1", "release 7 10000" }, screens.Calls);

        session.OnMessage(new PingMessage(9));
        Assert.Equal(9UL, conn.Last<PongMessage>()!.SentUs);

        session.OnClosed();
        await session.Idle.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("disconnected dev-1", screens.Calls[^1]);
    }

    [Fact]
    public async Task AcquireFailure_ReportsScreenFailedWithTheReason()
    {
        var session = Welcomed(out var conn, out var screens);
        screens.FailAcquire = true;
        session.OnMessage(new AcquireScreenMessage(3, ScreenKind.Virtual, 2560, 1440, 0));
        await session.Idle.WaitAsync(TimeSpan.FromSeconds(5));
        var failed = conn.Last<ScreenFailedMessage>()!;
        Assert.Equal(3u, failed.ScreenKey);
        Assert.Equal("sem driver", failed.Reason);
    }

    [Fact]
    public async Task ScreenMessages_BeforeWelcome_AreIgnored()
    {
        var (session, conn, _, screens, _) = Create(approve: false);
        session.OnMessage(new AcquireScreenMessage(1, ScreenKind.Virtual, 2560, 1440, 0));
        session.OnMessage(new StartStreamMessage(1, 9000, 0));
        await session.Idle.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(screens.Calls);
        Assert.Null(conn.Last<ScreenReadyMessage>());
    }

    [Fact]
    public void ScreenMessages_BeforeWelcome_AreNotEvenEnqueued()
    {
        var (session, _, _, _, _) = Create(approve: false);
        var idle = session.Idle;
        session.OnMessage(new AcquireScreenMessage(1, ScreenKind.Virtual, 2560, 1440, 0));
        session.OnMessage(new StartStreamMessage(1, 9000, 0));
        session.OnMessage(new ReleaseScreenMessage(1, 0));
        Assert.Same(idle, session.Idle); // nada na fila: um peer não pareado não a faz crescer
    }

    [Theory]
    [InlineData(false)] // pareamento recusado
    [InlineData(null)]  // pareamento nunca concluído
    public async Task UnpairedSession_Closing_DoesNotDisconnectThatDevice(bool? answer)
    {
        var conn = new FakeConnection();
        var screens = new FakeScreens();
        IPairingApprover approver = answer.HasValue ? new FakeApprover(answer.Value) : new PendingApprover();
        var session = new HeadsetSession(conn, new PairingStore(), () => { }, approver, new FakeMonitors(), screens,
            "CAYO-PC", () => "482913", _ => { });
        session.OnMessage(new HelloMessage("victim-dev", "Quest 3", "")); // id de outro headset, sem token
        session.OnClosed();
        await session.Idle.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(screens.Calls);
    }

    [Fact]
    public async Task WelcomedSession_ClaimsTheDevice_AndPassesItsGenerationToEveryScreenCall()
    {
        var session = Welcomed(out _, out var screens);
        session.OnMessage(new AcquireScreenMessage(1, ScreenKind.Virtual, 2560, 1440, 0));
        session.OnMessage(new StartStreamMessage(1, 9000, 0));
        session.OnMessage(new ReleaseScreenMessage(1, 0));
        session.OnClosed();
        await session.Idle.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(4, screens.Generations.Count);
        long generation = screens.Generations[0];
        Assert.True(screens.Owners.IsCurrent("dev-1", generation));
        Assert.All(screens.Generations, g => Assert.Equal(generation, g));
    }

    [Fact]
    public async Task Reconnect_NewSessionReplacesTheOld_OldCloseCarriesTheStaleGeneration()
    {
        var pairing = new PairingStore();
        string token = pairing.Pair("dev-1", "Quest 3", DateTimeOffset.UnixEpoch);
        var screens = new FakeScreens();
        HeadsetSession NewSession(FakeConnection c) => new(c, pairing, () => { }, new FakeApprover(false), new FakeMonitors(),
            screens, "CAYO-PC", () => "482913", _ => { });
        var oldConn = new FakeConnection();
        var newConn = new FakeConnection();
        var oldSession = NewSession(oldConn);
        var newSession = NewSession(newConn);

        oldSession.OnMessage(new HelloMessage("dev-1", "Quest 3", token));
        newSession.OnMessage(new HelloMessage("dev-1", "Quest 3", token)); // Wi-Fi caiu e o headset reconectou
        newSession.OnMessage(new StartStreamMessage(1, 9000, 0));
        await newSession.Idle.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ByeReason.Replaced, oldConn.Last<ByeMessage>()!.Reason);
        Assert.True(oldConn.Closed);
        Assert.False(newConn.Closed);

        oldSession.OnClosed(); // o fechamento do socket antigo chega depois
        await oldSession.Idle.WaitAsync(TimeSpan.FromSeconds(5));
        long newGeneration = screens.Generations[0];
        long oldCloseGeneration = screens.Generations[^1];
        Assert.Equal("disconnected dev-1", screens.Calls[^1]);
        Assert.True(screens.Owners.IsCurrent("dev-1", newGeneration));
        Assert.False(screens.Owners.IsCurrent("dev-1", oldCloseGeneration)); // o ScreenHost ignora esse Disconnected
    }

    [Fact]
    public void SecondHello_OnAWelcomedSession_IsIgnored()
    {
        var session = Welcomed(out var conn, out _);
        int sent;
        lock (conn.Sent) sent = conn.Sent.Count;
        session.OnMessage(new HelloMessage("other-dev", "Quest 3", ""));
        lock (conn.Sent) Assert.Equal(sent, conn.Sent.Count);
        Assert.False(conn.Closed);
    }

    private sealed class PendingApprover : IPairingApprover
    {
        public Task<bool> ApproveAsync(string deviceName, string code) => new TaskCompletionSource<bool>().Task;
    }

    [Fact]
    public async Task SlowAcquire_DoesNotBlockPingOnTheReadThread()
    {
        var session = Welcomed(out var conn, out var screens);
        using var gate = new ManualResetEventSlim(false);
        screens.BlockAcquire = gate;
        session.OnMessage(new AcquireScreenMessage(1, ScreenKind.Virtual, 2560, 1440, 0)); // volta na hora
        session.OnMessage(new PingMessage(5));
        Assert.Equal(5UL, conn.Last<PongMessage>()!.SentUs);
        Assert.Null(conn.Last<ScreenReadyMessage>());
        gate.Set();
        await session.Idle.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(conn.Last<ScreenReadyMessage>());
    }

    [Fact]
    public async Task ReleaseIsFullyHandled_BeforeALaterStartStream()
    {
        var session = Welcomed(out _, out var screens);
        using var gate = new ManualResetEventSlim(false);
        screens.BlockRelease = gate;
        session.OnMessage(new ReleaseScreenMessage(1, 0));
        session.OnMessage(new StartStreamMessage(2, 9000, 0));
        await Task.Delay(200);
        lock (screens.Calls) Assert.DoesNotContain(screens.Calls, c => c.StartsWith("start 2"));
        gate.Set();
        await session.Idle.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "release 1 0", "start 2:9000" }, screens.Calls);
    }
}
