using NUnit.Framework;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Menu;
using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Tests
{
    public class HostSyncTests
    {
        private static readonly WelcomeMessage NoMonitors = new WelcomeMessage("CAYO-PC", new MonitorDescriptor[0]);

        private sealed class Rig
        {
            public readonly ConnectionRig C = new ConnectionRig();
            public readonly FakeScreenVideo Video = new FakeScreenVideo();
            public readonly HostSync Sync;

            public Rig()
            {
                Sync = new HostSync(C.Store, C.Machine, Video, C.Bus, C.Clock, new ConnectionOptions());
                Sync.Start();
            }

            public void Tick()
            {
                C.Machine.Tick();
                Sync.Tick();
            }

            public void Link(WelcomeMessage welcome = null)
            {
                C.GoToStreaming(welcome ?? NoMonitors);
                Sync.Tick();
            }

            public void Advance(double seconds)
            {
                double end = C.Clock.Now + seconds;
                while (C.Clock.Now < end - 1e-9)
                {
                    C.Clock.Now += 0.05;
                    C.Channel.Push(ChannelEventKind.Message, new PongMessage(0)); // mantém a conexão viva
                    Tick();
                }
            }

            public ScreenId AddVirtual(int w = 2560, int h = 1440)
            {
                var r = Resolution.Create(w, h);
                C.Bus.Send(new CreateScreen(r, null, ScreenSizing.InitialWidthMeters(r), 0f, r.ToString(), ScreenSource.Virtual));
                return C.Store.State.FocusedScreenId;
            }

            public void Push(ControlMessage m)
            {
                C.Channel.Push(ChannelEventKind.Message, m);
                Tick();
            }

            public ScreenLinkState State(ScreenId id)
            {
                Assert.That(Sync.TryGetLink(id, out var state, out _), Is.True);
                return state;
            }
        }

        [Test]
        public void FirstConnection_EmptyWorkspace_CreatesPrimaryMirror_AndAcquiresIt()
        {
            var r = new Rig();
            r.Link(ConnectionRig.Welcome); // DISPLAY1 2560×1080 primário

            Assert.That(r.C.Store.State.Screens.Count, Is.EqualTo(1));
            var screen = r.C.Store.State.Screens[0];
            Assert.That(screen.Source, Is.EqualTo(ScreenSource.Mirror(1)));
            Assert.That(screen.Label, Is.EqualTo("DISPLAY1"));
            var acquire = r.C.Channel.Last<AcquireScreenMessage>();
            Assert.That(acquire.ScreenKey, Is.EqualTo((uint)screen.Id.Value));
            Assert.That(acquire.ScreenKind, Is.EqualTo(ScreenKind.Mirror));
            Assert.That(acquire.PhysicalMonitorId, Is.EqualTo(1u));
            Assert.That(r.State(screen.Id), Is.EqualTo(ScreenLinkState.Creating));
        }

        [Test]
        public void AutoMirror_OnlyOnTheFirstConnection_AndNotWithScreens()
        {
            var r = new Rig();
            r.AddVirtual();
            r.Link(ConnectionRig.Welcome);
            Assert.That(r.C.Store.State.Screens.Count, Is.EqualTo(1), "já havia tela: sem espelho automático");
        }

        [Test]
        public void Ready_OpensVideo_ThenStartsStream_ThenLiveOnFirstFrame()
        {
            var r = new Rig();
            var id = r.AddVirtual();
            r.Link();
            var acquire = r.C.Channel.Last<AcquireScreenMessage>();
            Assert.That(acquire.ScreenKind, Is.EqualTo(ScreenKind.Virtual));
            Assert.That(acquire.Width, Is.EqualTo(2560u));

            r.Push(new ScreenReadyMessage((uint)id.Value, 2560, 1440));
            Assert.That(r.Video.Opens, Is.EqualTo(1));
            Assert.That(r.Video.LastPort, Is.EqualTo(9000));
            Assert.That(r.Video.LastStreamId, Is.EqualTo((ushort)0));
            var start = r.C.Channel.Last<StartStreamMessage>();
            Assert.That(start.ScreenKey, Is.EqualTo((uint)id.Value));
            Assert.That(start.UdpPort, Is.EqualTo((ushort)9000));
            Assert.That(r.State(id), Is.EqualTo(ScreenLinkState.Connecting));

            r.Push(new StreamStartedMessage(0, 2560, 1440));
            r.Video.FirstFrame = true;
            r.Advance(1.1);
            Assert.That(r.State(id), Is.EqualTo(ScreenLinkState.Live));
        }

        [Test]
        public void SurfaceNotReady_RetriesOpen_BeforeStartStream()
        {
            var r = new Rig();
            var id = r.AddVirtual();
            r.Link();
            r.Video.Ready = false;
            r.Push(new ScreenReadyMessage((uint)id.Value, 2560, 1440));
            Assert.That(r.C.Channel.Count<StartStreamMessage>(), Is.EqualTo(0));
            r.Video.Ready = true;
            r.Tick();
            Assert.That(r.C.Channel.Count<StartStreamMessage>(), Is.EqualTo(1));
        }

        [Test]
        public void Remove_ReleasesWith10sGrace_ClosesVideo_AndUndoAcquiresTheSameKey()
        {
            var r = new Rig();
            var id = r.AddVirtual();
            r.Link();
            r.Push(new ScreenReadyMessage((uint)id.Value, 2560, 1440));

            r.C.Bus.Send(new RemoveScreen(id));
            var release = r.C.Channel.Last<ReleaseScreenMessage>();
            Assert.That(release.ScreenKey, Is.EqualTo((uint)id.Value));
            Assert.That(release.GraceMs, Is.EqualTo(HostSync.RemoveGraceMs));
            Assert.That(r.Video.Open.Contains(id), Is.False);

            r.C.Bus.Undo();
            r.Tick();
            Assert.That(r.C.Channel.Count<AcquireScreenMessage>(), Is.EqualTo(2));
            Assert.That(r.C.Channel.Last<AcquireScreenMessage>().ScreenKey, Is.EqualTo((uint)id.Value));
        }

        [Test]
        public void Lost_ShowsOffline_KeepsTheDecoder_ReconnectAcquiresAgain()
        {
            var r = new Rig();
            var id = r.AddVirtual();
            r.Link();
            r.Push(new ScreenReadyMessage((uint)id.Value, 2560, 1440));
            r.Push(new StreamStartedMessage(0, 2560, 1440));

            r.C.Channel.Push(ChannelEventKind.Disconnected);
            r.Tick();
            Assert.That(r.State(id), Is.EqualTo(ScreenLinkState.Offline));
            Assert.That(r.Video.Closes, Is.EqualTo(0), "o decoder fica aberto para a reconexão");

            r.Tick(); // tentativa de reconexão
            r.C.Channel.Push(ChannelEventKind.Connected);
            r.C.Channel.Push(ChannelEventKind.Message, NoMonitors);
            r.Tick();
            Assert.That(r.C.Channel.Count<AcquireScreenMessage>(), Is.EqualTo(2));
            r.Push(new ScreenReadyMessage((uint)id.Value, 2560, 1440));
            Assert.That(r.C.Channel.Count<StartStreamMessage>(), Is.EqualTo(2));
            Assert.That(r.Video.Opens, Is.EqualTo(1), "mesma sessão de decoder");
        }

        [Test]
        public void Welcome_ReleasesLeasesTheWorkspaceNoLongerHas()
        {
            var r = new Rig();
            var id = r.AddVirtual();
            r.Link(new WelcomeMessage("CAYO-PC", new MonitorDescriptor[0], new[]
            {
                new ScreenLeaseInfo((uint)id.Value, ScreenKind.Virtual, 2560, 1440),
                new ScreenLeaseInfo(42, ScreenKind.Virtual, 1920, 1080),
            }));
            var release = r.C.Channel.Last<ReleaseScreenMessage>();
            Assert.That(release.ScreenKey, Is.EqualTo(42u));
            Assert.That(r.C.Channel.Count<ReleaseScreenMessage>(), Is.EqualTo(1));
        }

        [Test]
        public void ScreenFailed_ShowsTheReason_WithoutRemovingTheScreen()
        {
            var r = new Rig();
            var id = r.AddVirtual();
            r.Link();
            r.Push(new ScreenFailedMessage((uint)id.Value, "SudoVDA não disponível"));
            Assert.That(r.Sync.TryGetLink(id, out var state, out var detail), Is.True);
            Assert.That(state, Is.EqualTo(ScreenLinkState.Failed));
            Assert.That(detail, Is.EqualTo("SudoVDA não disponível"));
            Assert.That(r.C.Store.State.Screens.Count, Is.EqualTo(1));
        }

        [Test]
        public void StreamStopped_RetriesStartStreamAfterOneSecond()
        {
            var r = new Rig();
            var id = r.AddVirtual();
            r.Link();
            r.Push(new ScreenReadyMessage((uint)id.Value, 2560, 1440));
            r.Push(new StreamStoppedMessage(0, "falha ao iniciar o stream"));
            r.Advance(0.5);
            Assert.That(r.C.Channel.Count<StartStreamMessage>(), Is.EqualTo(1));
            r.Advance(0.6);
            Assert.That(r.C.Channel.Count<StartStreamMessage>(), Is.EqualTo(2));
        }

        [Test]
        public void TestScreens_AreIgnored()
        {
            var r = new Rig();
            r.C.Bus.Send(TestScreens.Next(0));
            r.Link();
            Assert.That(r.C.Channel.Count<AcquireScreenMessage>(), Is.EqualTo(0));
        }

        [Test]
        public void Ports_ArePooled_LowestFreeFirst()
        {
            var r = new Rig();
            var a = r.AddVirtual();
            var b = r.AddVirtual(1920, 1080);
            r.Link();
            r.Push(new ScreenReadyMessage((uint)a.Value, 2560, 1440));
            r.Push(new ScreenReadyMessage((uint)b.Value, 1920, 1080));
            Assert.That(r.Video.LastPort, Is.EqualTo(9001));

            r.C.Bus.Send(new RemoveScreen(a));
            var c = r.AddVirtual(3840, 2160);
            r.Tick();
            r.Push(new ScreenReadyMessage((uint)c.Value, 3840, 2160));
            Assert.That(r.Video.LastPort, Is.EqualTo(9000), "reusa a porta liberada");
        }

        [Test]
        public void Catalog_ExposesConnectionAndPhysicalMonitors()
        {
            var r = new Rig();
            Assert.That(r.Sync.IsConnected, Is.False);
            r.AddVirtual();
            r.Link(ConnectionRig.Welcome);
            Assert.That(r.Sync.IsConnected, Is.True);
            Assert.That(r.Sync.PhysicalMonitors.Count, Is.EqualTo(1));
        }
    }
}
