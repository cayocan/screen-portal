using System.Diagnostics;
using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Host.Domain.Pairing;
using ScreenPortal.Host.Transport.Control;
using ScreenPortal.Infrastructure.Net;
using Xunit.Abstractions;

namespace ScreenPortal.Host.Tests.Control;

/// <summary>
/// Máquina de estados REAL do headset + TcpControlChannel real contra o ControlServer real (localhost).
/// Mede a reconexão (critério do brief §4.2: &lt; 3 s).
/// </summary>
public class ControlServerIntegrationTests(ITestOutputHelper output)
{
    private sealed class OkVideo : IVideoStreamSink
    {
        public int Starts;
        public bool TryStart(ScreenId screen, ushort streamId, int udpPort, Resolution resolution) { Starts++; return true; }
        public void Stop(ushort streamId) { }
    }

    private static bool RunUntil(ConnectionStateMachine machine, Func<bool> condition, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            machine.Tick();
            if (condition()) return true;
            Thread.Sleep(5);
        }
        return false;
    }

    [Fact]
    public void PairsStreamsAndReconnectsUnderThreeSeconds()
    {
        var pairing = new PairingStore();
        var streams = new HeadsetSessionTests.FakeStreams();
        using var server = new ControlServer(0, conn => new HeadsetSession(conn, pairing, () => { },
            new HeadsetSessionTests.FakeApprover(true), new HeadsetSessionTests.FakeMonitors(), streams,
            "TEST-PC", () => "123456", _ => { }), _ => { });
        server.Start();

        var store = new WorkspaceStore();
        var video = new OkVideo();
        var clientPairing = new InMemoryPairingStore("dev-it");
        using var channel = new TcpControlChannel(2000);
        var machine = new ConnectionStateMachine(
            new StaticHostDiscovery(new HostEndpoint("TEST-PC", "127.0.0.1", server.Port)),
            channel, clientPairing, new MonotonicClock(), new CommandBus(store, Array.Empty<ICommandObserver>()),
            store, video, new ConnectionOptions { DeviceName = "Quest-IT" });
        machine.Start();

        Assert.True(RunUntil(machine, () => machine.State == ConnectionState.Streaming, 5000), $"estado: {machine.State}");
        Assert.NotEqual("", clientPairing.GetToken("TEST-PC"));
        Assert.Single(store.State.Screens);
        Assert.Equal(2560, store.State.Screens[0].Resolution.Width);
        Assert.Equal(1, streams.Starts);

        var sw = Stopwatch.StartNew();
        server.DropAllConnections();
        Assert.True(RunUntil(machine, () => machine.State == ConnectionState.Reconnecting, 2000), $"estado: {machine.State}");
        Assert.True(RunUntil(machine, () => machine.State == ConnectionState.Streaming, 5000), $"estado: {machine.State}");
        sw.Stop();
        output.WriteLine($"reconexão: {sw.ElapsedMilliseconds} ms");

        Assert.True(sw.ElapsedMilliseconds < 3000, $"reconexão levou {sw.ElapsedMilliseconds} ms");
        Assert.Single(store.State.Screens);
        Assert.Equal(1, video.Starts);
        Assert.Equal(2, streams.Starts);
    }

    [Fact]
    public void ShutdownBye_IsShownToTheHeadset()
    {
        var pairing = new PairingStore();
        using var server = new ControlServer(0, conn => new HeadsetSession(conn, pairing, () => { },
            new HeadsetSessionTests.FakeApprover(true), new HeadsetSessionTests.FakeMonitors(),
            new HeadsetSessionTests.FakeStreams(), "TEST-PC", () => "123456", _ => { }), _ => { });
        server.Start();

        var store = new WorkspaceStore();
        using var channel = new TcpControlChannel(2000);
        var machine = new ConnectionStateMachine(
            new StaticHostDiscovery(new HostEndpoint("TEST-PC", "127.0.0.1", server.Port)),
            channel, new InMemoryPairingStore("dev-it"), new MonotonicClock(),
            new CommandBus(store, Array.Empty<ICommandObserver>()), store, new OkVideo(), new ConnectionOptions());
        machine.Start();
        Assert.True(RunUntil(machine, () => machine.State == ConnectionState.Streaming, 5000));

        server.BroadcastBye(ByeReason.Shutdown);
        Assert.True(RunUntil(machine, () => machine.Status.Kind == ConnectionStatusKind.HostShutdown, 2000));
    }
}
