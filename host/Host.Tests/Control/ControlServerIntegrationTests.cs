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
        var screens = new HeadsetSessionTests.FakeScreens();
        using var server = new ControlServer(0, conn => new HeadsetSession(conn, pairing, () => { },
            new HeadsetSessionTests.FakeApprover(true), new HeadsetSessionTests.FakeMonitors(), screens,
            "TEST-PC", () => "123456", _ => { }), _ => { });
        server.Start();

        var clientPairing = new InMemoryPairingStore("dev-it");
        using var channel = new TcpControlChannel(2000);
        var machine = new ConnectionStateMachine(
            new StaticHostDiscovery(new HostEndpoint("TEST-PC", "127.0.0.1", server.Port)),
            channel, clientPairing, new MonotonicClock(), new ConnectionOptions { DeviceName = "Quest-IT" });
        machine.Start();

        Assert.True(RunUntil(machine, () => machine.State == ConnectionState.Streaming, 5000), $"estado: {machine.State}");
        Assert.NotEqual("", clientPairing.GetToken("TEST-PC"));

        var sw = Stopwatch.StartNew();
        server.DropAllConnections();
        Assert.True(RunUntil(machine, () => machine.State == ConnectionState.Reconnecting, 2000), $"estado: {machine.State}");
        Assert.True(RunUntil(machine, () => machine.State == ConnectionState.Streaming, 5000), $"estado: {machine.State}");
        sw.Stop();
        output.WriteLine($"reconexão: {sw.ElapsedMilliseconds} ms");

        Assert.True(sw.ElapsedMilliseconds < 3000, $"reconexão levou {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void ShutdownBye_IsShownToTheHeadset()
    {
        var pairing = new PairingStore();
        using var server = new ControlServer(0, conn => new HeadsetSession(conn, pairing, () => { },
            new HeadsetSessionTests.FakeApprover(true), new HeadsetSessionTests.FakeMonitors(),
            new HeadsetSessionTests.FakeScreens(), "TEST-PC", () => "123456", _ => { }), _ => { });
        server.Start();

        using var channel = new TcpControlChannel(2000);
        var machine = new ConnectionStateMachine(
            new StaticHostDiscovery(new HostEndpoint("TEST-PC", "127.0.0.1", server.Port)),
            channel, new InMemoryPairingStore("dev-it"), new MonotonicClock(), new ConnectionOptions { DeviceName = "Quest-IT" });
        machine.Start();
        Assert.True(RunUntil(machine, () => machine.State == ConnectionState.Streaming, 5000));

        server.BroadcastBye(ByeReason.Shutdown);
        Assert.True(RunUntil(machine, () => machine.Status.Kind == ConnectionStatusKind.HostShutdown, 2000));
    }
}
