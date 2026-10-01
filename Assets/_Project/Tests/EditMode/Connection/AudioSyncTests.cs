using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ScreenPortal.App.Audio;
using ScreenPortal.App.Connection;

namespace ScreenPortal.Tests
{
    public class AudioSyncTests
    {
        private sealed class FakeLink : IAudioLink
        {
            public readonly List<string> Calls = new List<string>();
            public bool Start(string host, int rxPort, int txPort, bool playback, bool mic)
            {
                Calls.Add($"start {host} {rxPort}->{txPort} {playback} {mic}");
                return true;
            }
            public void Stop() => Calls.Add("stop");
        }

        private sealed class Rig
        {
            public readonly ConnectionRig C = new ConnectionRig();
            public readonly FakeLink Link = new FakeLink();
            public readonly AudioSettings Settings = new AudioSettings();
            public readonly AudioSync Sync;

            public Rig() => Sync = new AudioSync(C.Machine, Link, Settings);

            public void Receive(ControlMessage message)
            {
                C.Channel.Push(ChannelEventKind.Message, message);
                C.Machine.Tick();
            }

            public StartAudioMessage LastStart() => C.Channel.Sent.OfType<StartAudioMessage>().LastOrDefault();
        }

        [Test]
        public void Welcome_AsksForAudio_AndAudioStarted_StartsTheNativeSession()
        {
            var r = new Rig();
            r.C.GoToStreaming(ConnectionRig.Welcome);
            var start = r.LastStart();
            Assert.That(start, Is.Not.Null);
            Assert.That(start.UdpPort, Is.EqualTo((ushort)AudioSync.RxPort));
            Assert.That(start.Playback && start.Mic, Is.True);

            r.Receive(new AudioStartedMessage(9301, true, true));
            Assert.That(r.Link.Calls.Last(), Is.EqualTo($"start {ConnectionRig.Pc.Address} {AudioSync.RxPort}->9301 True True"));
            Assert.That(r.Sync.Status, Is.EqualTo("Som do PC: ligado · Microfone: ligado"));
        }

        [Test]
        public void MutingTheMic_AsksAgain_AndRestartsWithoutMic()
        {
            var r = new Rig();
            r.C.GoToStreaming(ConnectionRig.Welcome);
            r.Receive(new AudioStartedMessage(9301, true, true));
            r.Settings.ToggleMic();
            Assert.That(r.LastStart().Mic, Is.False);
            r.Receive(new AudioStartedMessage(9301, true, false));
            Assert.That(r.Link.Calls.Last(), Does.EndWith("True False"));
            Assert.That(r.Sync.Status, Is.EqualTo("Som do PC: ligado · Microfone: mudo"));
        }

        [Test]
        public void HostWithoutSteamDrivers_IsShownInTheStatus()
        {
            var r = new Rig();
            r.C.GoToStreaming(ConnectionRig.Welcome);
            r.Receive(new AudioStartedMessage(9301, false, false));
            Assert.That(r.Link.Calls.Last(), Does.EndWith("True False"), "sem microfone virtual no PC: o Quest não grava");
            Assert.That(r.Sync.Status, Is.EqualTo("Som também nas caixas do PC · Microfone indisponível (Steam)"));
        }

        [Test]
        public void ConnectionLost_StopsTheNativeSession()
        {
            var r = new Rig();
            r.C.GoToStreaming(ConnectionRig.Welcome);
            r.Receive(new AudioStartedMessage(9301, true, true));
            r.C.Channel.Push(ChannelEventKind.Disconnected);
            r.C.Machine.Tick();
            Assert.That(r.Link.Calls.Last(), Is.EqualTo("stop"));
        }
    }
}
