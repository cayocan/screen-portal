using NUnit.Framework;
using ScreenPortal.App.Connection;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class ConnectionStateMachineTests
    {
        [Test]
        public void Start_SearchesAndConnectsToDiscoveredHost()
        {
            var r = new ConnectionRig();
            r.Machine.Start();
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Discovering));
            Assert.That(r.Machine.Status.Kind, Is.EqualTo(ConnectionStatusKind.Searching));
            Assert.That(r.Discovery.Started, Is.True);

            r.Discovery.Host = ConnectionRig.Pc;
            r.Machine.Tick();
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Connecting));
            Assert.That(r.Channel.Connects, Is.EqualTo(new[] { ConnectionRig.Pc }));
            Assert.That(r.Machine.Status.HostName, Is.EqualTo("CAYO-PC"));
        }

        [Test]
        public void Connected_SendsHelloWithStoredToken()
        {
            var r = new ConnectionRig();
            r.Pairing.Tokens["CAYO-PC"] = "tok";
            r.Machine.Start();
            r.Discovery.Host = ConnectionRig.Pc;
            r.Machine.Tick();
            r.Channel.Push(ChannelEventKind.Connected);
            r.Machine.Tick();

            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Negotiating));
            var hello = r.Channel.Last<HelloMessage>();
            Assert.That(hello.DeviceId, Is.EqualTo("dev-1"));
            Assert.That(hello.PairingToken, Is.EqualTo("tok"));
        }

        [Test]
        public void Pairing_ShowsCode_NeverTimesOut_AndSavesToken()
        {
            var r = new ConnectionRig();
            r.Machine.Start();
            r.Discovery.Host = ConnectionRig.Pc;
            r.Machine.Tick();
            r.Channel.Push(ChannelEventKind.Connected);
            r.Channel.Push(ChannelEventKind.Message, new PairingRequiredMessage("482913"));
            r.Machine.Tick();

            Assert.That(r.Machine.Status.Kind, Is.EqualTo(ConnectionStatusKind.ConfirmOnPc));
            Assert.That(r.Machine.Status.PairingCode, Is.EqualTo("482913"));

            r.Advance(60);
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Negotiating), "sem timeout enquanto o usuário decide");

            r.Channel.Push(ChannelEventKind.Message, new PairingResultMessage(true, "novo"));
            r.Machine.Tick();
            Assert.That(r.Pairing.Tokens["CAYO-PC"], Is.EqualTo("novo"));
        }

        /// <summary>
        /// Regressão (Quest, 28/09/2026): o usuário levou ~7 s para clicar em Permitir; o Welcome chegou num frame
        /// depois do PairingResult e o timeout de negociação (contado desde o Hello) derrubou a conexão.
        /// </summary>
        [Test]
        public void SlowPairingApproval_DoesNotTimeOutBeforeWelcome()
        {
            var r = new ConnectionRig();
            r.Machine.Start();
            r.Discovery.Host = ConnectionRig.Pc;
            r.Machine.Tick();
            r.Channel.Push(ChannelEventKind.Connected);
            r.Channel.Push(ChannelEventKind.Message, new PairingRequiredMessage("245993"));
            r.Machine.Tick();
            r.Advance(7);

            r.Channel.Push(ChannelEventKind.Message, new PairingResultMessage(true, "tok"));
            r.Advance(0.1);          // Welcome ainda não chegou
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Negotiating));
            Assert.That(r.Channel.Closes, Is.EqualTo(0));

            r.Channel.Push(ChannelEventKind.Message, ConnectionRig.Welcome);
            r.Channel.Push(ChannelEventKind.Message, new StreamStartedMessage(0, 2560, 1080));
            r.Machine.Tick();
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Streaming));
        }

        [Test]
        public void Welcome_CreatesScreenForPrimaryMonitor_AndRequestsStream()
        {
            var r = new ConnectionRig();
            r.GoToStreaming();

            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Streaming));
            Assert.That(r.Machine.Status.Kind, Is.EqualTo(ConnectionStatusKind.Connected));
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(1));
            var screen = r.Store.State.Screens[0];
            Assert.That(screen.Label, Is.EqualTo("monitor:1"));
            Assert.That(screen.Resolution.Width, Is.EqualTo(2560));
            Assert.That(r.Video.Starts, Is.EqualTo(1));
            var start = r.Channel.Last<StartStreamMessage>();
            // Task 5 reescreve: hoje o monitor id vai como screen key
            Assert.That(start.ScreenKey, Is.EqualTo(1u));
            Assert.That(start.UdpPort, Is.EqualTo((ushort)9000));
            Assert.That(r.Pairing.LastHost, Is.EqualTo(ConnectionRig.Pc));
        }

        [Test]
        public void SurfaceNotReady_RetriesStartStreamOnLaterTick()
        {
            var r = new ConnectionRig();
            r.Video.Ready = false;
            r.Pairing.Tokens["CAYO-PC"] = "tok";
            r.Machine.Start();
            r.Discovery.Host = ConnectionRig.Pc;
            r.Machine.Tick();
            r.Channel.Push(ChannelEventKind.Connected);
            r.Channel.Push(ChannelEventKind.Message, ConnectionRig.Welcome);
            r.Machine.Tick();
            Assert.That(r.Channel.Count<StartStreamMessage>(), Is.EqualTo(0));

            r.Video.Ready = true;
            r.Machine.Tick();
            Assert.That(r.Channel.Count<StartStreamMessage>(), Is.EqualTo(1));
        }

        [Test]
        public void MissingPongs_Degrade_ThenRecover_ThenReconnect()
        {
            var r = new ConnectionRig();
            r.GoToStreaming();

            r.Advance(1.6);
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Degraded));
            Assert.That(r.Machine.Status.Kind, Is.EqualTo(ConnectionStatusKind.Unstable));
            Assert.That(r.Channel.Count<PingMessage>(), Is.GreaterThanOrEqualTo(3));

            r.Channel.Push(ChannelEventKind.Message, new PongMessage(0));
            r.Machine.Tick();
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Streaming));

            int closes = r.Channel.Closes;
            r.Advance(4.6);
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Reconnecting));
            Assert.That(r.Channel.Closes, Is.GreaterThan(closes));
        }

        [Test]
        public void Reconnect_GoesStraightToLastHost_AndReusesScreenAndDecoder()
        {
            var r = new ConnectionRig();
            r.GoToStreaming();
            r.Store.Dispatch(new MoveScreenOnSurface(r.Store.State.Screens[0].Id, -30f, 10f));
            r.Discovery.Host = null; // prova que não depende do mDNS

            r.Channel.Push(ChannelEventKind.Disconnected);
            r.Machine.Tick();
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Reconnecting));
            r.Machine.Tick();
            Assert.That(r.Channel.Connects[r.Channel.Connects.Count - 1], Is.EqualTo(ConnectionRig.Pc));

            r.Channel.Push(ChannelEventKind.Connected);
            r.Channel.Push(ChannelEventKind.Message, ConnectionRig.Welcome);
            r.Machine.Tick();
            r.Channel.Push(ChannelEventKind.Message, new StreamStartedMessage(0, 2560, 1080));
            r.Machine.Tick();

            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Streaming));
            Assert.That(r.Store.State.Screens.Count, Is.EqualTo(1), "não duplica a tela");
            Assert.That(r.Store.State.Screens[0].Placement.AzimuthDeg, Is.EqualTo(-30f), "layout intacto");
            Assert.That(r.Video.Starts, Is.EqualTo(1), "decoder continua o mesmo");
            Assert.That(r.Channel.Count<StartStreamMessage>(), Is.EqualTo(2));
        }

        [Test]
        public void Reconnect_RetriesEvery250ms_AndGivesUpAfter10s()
        {
            var r = new ConnectionRig();
            r.GoToStreaming();
            r.Channel.Push(ChannelEventKind.Disconnected);
            r.Machine.Tick();
            r.Machine.Tick();
            int attempts = r.Channel.Connects.Count;

            r.Channel.Push(ChannelEventKind.ConnectFailed);
            r.Advance(0.1, 0.1);
            Assert.That(r.Channel.Connects.Count, Is.EqualTo(attempts), "ainda não passou 250 ms");
            r.Advance(0.2, 0.2);
            Assert.That(r.Channel.Connects.Count, Is.EqualTo(attempts + 1));

            r.Discovery.Host = null; // senão, ao desistir, a descoberta reconectaria na hora
            for (int i = 0; i < 60; i++)
            {
                r.Channel.Push(ChannelEventKind.ConnectFailed);
                r.Advance(0.25, 0.25);
            }
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Discovering));
            Assert.That(r.Video.Stops, Is.EqualTo(1));
        }

        [Test]
        public void Rejected_BlocksHostDuringCooldown()
        {
            var r = new ConnectionRig();
            r.Machine.Start();
            r.Discovery.Host = ConnectionRig.Pc;
            r.Machine.Tick();
            r.Channel.Push(ChannelEventKind.Connected);
            r.Channel.Push(ChannelEventKind.Message, new PairingResultMessage(false, ""));
            r.Machine.Tick();

            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Discovering));
            Assert.That(r.Machine.Status.Kind, Is.EqualTo(ConnectionStatusKind.Rejected));
            int connects = r.Channel.Connects.Count;
            r.Advance(9);
            Assert.That(r.Channel.Connects.Count, Is.EqualTo(connects));
            r.Advance(1.5);
            Assert.That(r.Channel.Connects.Count, Is.EqualTo(connects + 1));
        }

        [Test]
        public void ByeMessages_ShowTheRightStatus()
        {
            var r = new ConnectionRig();
            r.GoToStreaming();
            r.Channel.Push(ChannelEventKind.Message, new ByeMessage(ByeReason.Shutdown));
            r.Machine.Tick();
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Discovering));
            Assert.That(r.Machine.Status.Kind, Is.EqualTo(ConnectionStatusKind.HostShutdown));

            var r2 = new ConnectionRig();
            r2.GoToStreaming();
            r2.Channel.Push(ChannelEventKind.Message, new ByeMessage(ByeReason.IncompatibleVersion));
            r2.Machine.Tick();
            Assert.That(r2.Machine.Status.Kind, Is.EqualTo(ConnectionStatusKind.IncompatibleVersion));
        }

        [Test]
        public void Timeouts_ConnectAndNegotiate()
        {
            var r = new ConnectionRig();
            r.Machine.Start();
            r.Discovery.Host = ConnectionRig.Pc;
            r.Machine.Tick();
            r.Discovery.Host = null;
            r.Advance(2.1);
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Discovering), "connect > 2 s");

            r.Discovery.Host = ConnectionRig.Pc;
            r.Machine.Tick();
            r.Channel.Push(ChannelEventKind.Connected);
            r.Machine.Tick();
            r.Discovery.Host = null;
            r.Advance(5.1);
            Assert.That(r.Machine.State, Is.EqualTo(ConnectionState.Discovering), "negociação > 5 s sem pareamento");
        }

        [Test]
        public void InstallHint_OnlyWhenNeverConnected()
        {
            var r = new ConnectionRig();
            r.Machine.Start();
            r.Advance(10.1);
            Assert.That(r.Machine.Status.Kind, Is.EqualTo(ConnectionStatusKind.InstallHost));

            var r2 = new ConnectionRig();
            r2.Pairing.LastHost = ConnectionRig.Pc;
            r2.Machine.Start();
            r2.Advance(10.1);
            Assert.That(r2.Machine.Status.Kind, Is.EqualTo(ConnectionStatusKind.Searching));
        }

        [Test]
        public void HostPing_IsAnsweredWithPong()
        {
            var r = new ConnectionRig();
            r.GoToStreaming();
            r.Channel.Push(ChannelEventKind.Message, new PingMessage(77));
            r.Machine.Tick();
            Assert.That(r.Channel.Last<PongMessage>().SentUs, Is.EqualTo(77UL));
        }
    }
}
