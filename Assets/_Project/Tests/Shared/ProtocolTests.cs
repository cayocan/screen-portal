using System;
using NUnit.Framework;
using ScreenPortal.App.Connection;
using ScreenPortal.Infrastructure.Protocol;

namespace ScreenPortal.Tests
{
    public class ProtocolTests
    {
        private static ControlMessage RoundTrip(ControlMessage message)
        {
            var result = ControlMessageCodec.Decode(ControlMessageCodec.Encode(message));
            Assert.That(result.Status, Is.EqualTo(DecodeStatus.Ok));
            Assert.That(result.Version, Is.EqualTo(ControlProtocol.Version));
            Assert.That(result.Message.Kind, Is.EqualTo(message.Kind));
            return result.Message;
        }

        [Test]
        public void Hello_RoundTrips()
        {
            var m = (HelloMessage)RoundTrip(new HelloMessage("dev-1", "Quest 3", "tok"));
            Assert.That(m.DeviceId, Is.EqualTo("dev-1"));
            Assert.That(m.DeviceName, Is.EqualTo("Quest 3"));
            Assert.That(m.PairingToken, Is.EqualTo("tok"));
        }

        [Test]
        public void Welcome_RoundTripsMonitorList()
        {
            var m = (WelcomeMessage)RoundTrip(new WelcomeMessage("CAYO-PC", new[]
            {
                new MonitorDescriptor(1, 2560, 1080, "DISPLAY1", true),
                new MonitorDescriptor(2, 1920, 1080, "DISPLAY2", false),
            }));
            Assert.That(m.HostName, Is.EqualTo("CAYO-PC"));
            Assert.That(m.Monitors.Count, Is.EqualTo(2));
            Assert.That(m.Monitors[0].Width, Is.EqualTo(2560u));
            Assert.That(m.Monitors[0].IsPrimary, Is.True);
            Assert.That(m.Monitors[1].Name, Is.EqualTo("DISPLAY2"));
        }

        [Test]
        public void AllOtherMessages_RoundTrip()
        {
            Assert.That(((PairingRequiredMessage)RoundTrip(new PairingRequiredMessage("482913"))).Code, Is.EqualTo("482913"));
            var pr = (PairingResultMessage)RoundTrip(new PairingResultMessage(true, "t"));
            Assert.That(pr.Accepted, Is.True);
            Assert.That(pr.PairingToken, Is.EqualTo("t"));
            var ss = (StartStreamMessage)RoundTrip(new StartStreamMessage(7, 9000, 0));
            Assert.That(ss.UdpPort, Is.EqualTo((ushort)9000));
            Assert.That(ss.ScreenKey, Is.EqualTo(7u));
            var st = (StreamStartedMessage)RoundTrip(new StreamStartedMessage(0, 2560, 1080));
            Assert.That(st.Height, Is.EqualTo(1080u));
            Assert.That(((StreamStoppedMessage)RoundTrip(new StreamStoppedMessage(0, "x"))).Reason, Is.EqualTo("x"));
            Assert.That(((PingMessage)RoundTrip(new PingMessage(123456789UL))).SentUs, Is.EqualTo(123456789UL));
            Assert.That(((PongMessage)RoundTrip(new PongMessage(42UL))).SentUs, Is.EqualTo(42UL));
            Assert.That(((ByeMessage)RoundTrip(new ByeMessage(ByeReason.PairingRejected))).Reason, Is.EqualTo(ByeReason.PairingRejected));
        }

        [Test]
        public void ScreenMessages_RoundTrip()
        {
            var a = (AcquireScreenMessage)RoundTrip(new AcquireScreenMessage(3, ScreenKind.Mirror, 2560, 1080, 2));
            Assert.That(a.ScreenKey, Is.EqualTo(3u));
            Assert.That(a.ScreenKind, Is.EqualTo(ScreenKind.Mirror));
            Assert.That(a.Width, Is.EqualTo(2560u));
            Assert.That(a.Height, Is.EqualTo(1080u));
            Assert.That(a.PhysicalMonitorId, Is.EqualTo(2u));

            var ready = (ScreenReadyMessage)RoundTrip(new ScreenReadyMessage(3, 3840, 2160));
            Assert.That(ready.ScreenKey, Is.EqualTo(3u));
            Assert.That(ready.Height, Is.EqualTo(2160u));

            var failed = (ScreenFailedMessage)RoundTrip(new ScreenFailedMessage(4, "SudoVDA não disponível"));
            Assert.That(failed.ScreenKey, Is.EqualTo(4u));
            Assert.That(failed.Reason, Is.EqualTo("SudoVDA não disponível"));

            var release = (ReleaseScreenMessage)RoundTrip(new ReleaseScreenMessage(5, 10000));
            Assert.That(release.ScreenKey, Is.EqualTo(5u));
            Assert.That(release.GraceMs, Is.EqualTo(10000u));

            var layout = (LayoutChangedMessage)RoundTrip(new LayoutChangedMessage(new[]
            {
                new ScreenAnglesInfo(1, -10.5f, 2f, 20f, 9f), new ScreenAnglesInfo(4, 30f, -5f, 15f, 12f),
            }));
            Assert.That(layout.Screens.Count, Is.EqualTo(2));
            Assert.That(layout.Screens[1].ScreenKey, Is.EqualTo(4u));
            Assert.That(layout.Screens[0].AzimuthDeg, Is.EqualTo(-10.5f));
            Assert.That(layout.Screens[1].HalfHeightDeg, Is.EqualTo(12f));
            var hotkey = (HotkeyMessage)RoundTrip(new HotkeyMessage(HotkeyAction.CurvatureMore, 3));
            Assert.That(hotkey.Action, Is.EqualTo(HotkeyAction.CurvatureMore));
            Assert.That(hotkey.ScreenKey, Is.EqualTo(3u));

            var audio = (StartAudioMessage)RoundTrip(new StartAudioMessage(9300, true, false));
            Assert.That(audio.UdpPort, Is.EqualTo((ushort)9300));
            Assert.That(audio.Playback, Is.True);
            Assert.That(audio.Mic, Is.False);
            var started = (AudioStartedMessage)RoundTrip(new AudioStartedMessage(9301, true, true));
            Assert.That(started.MicPort, Is.EqualTo((ushort)9301));
            Assert.That(started.Speakers && started.Mic, Is.True);

            var rate = (SetStreamRateMessage)RoundTrip(new SetStreamRateMessage(4, 15, 5000));
            Assert.That(rate.ScreenKey, Is.EqualTo(4u));
            Assert.That(rate.Fps, Is.EqualTo((byte)15));
            Assert.That(rate.Kbps, Is.EqualTo(5000u));
        }

        [Test]
        public void Welcome_RoundTripsLeases_AndDefaultsToEmpty()
        {
            var monitors = new[] { new MonitorDescriptor(1, 2560, 1080, "DISPLAY1", true) };
            var m = (WelcomeMessage)RoundTrip(new WelcomeMessage("CAYO-PC", monitors, new[]
            {
                new ScreenLeaseInfo(2, ScreenKind.Virtual, 3840, 2160),
                new ScreenLeaseInfo(5, ScreenKind.Mirror, 2560, 1080),
            }));
            Assert.That(m.Leases.Count, Is.EqualTo(2));
            Assert.That(m.Leases[0].ScreenKey, Is.EqualTo(2u));
            Assert.That(m.Leases[0].Width, Is.EqualTo(3840u));
            Assert.That(m.Leases[1].Kind, Is.EqualTo(ScreenKind.Mirror));

            var empty = (WelcomeMessage)RoundTrip(new WelcomeMessage("CAYO-PC", monitors));
            Assert.That(empty.Leases.Count, Is.EqualTo(0));
        }

        [Test]
        public void ProtocolVersion_Is2()
        {
            Assert.That(ControlProtocol.Version, Is.EqualTo((ushort)2));
        }

        [Test]
        public void Decode_CorruptedVectorLength_IsInvalid()
        {
            var monitors = new[] { new MonitorDescriptor(1, 2560, 1080, "DISPLAY1", true) };
            var bytes = ControlMessageCodec.Encode(new WelcomeMessage("PC", monitors));
            bool patched = false;
            // o vetor tem length == 1 (uint32 LE) seguido do offset do elemento; troca por um tamanho absurdo
            for (int i = 8; i + 4 <= bytes.Length && !patched; i += 4)
            {
                if (bytes[i] == 1 && bytes[i + 1] == 0 && bytes[i + 2] == 0 && bytes[i + 3] == 0 && i + 8 <= bytes.Length)
                {
                    bytes[i + 3] = 0x7F;
                    bytes[i + 2] = 0xFF;
                    if (ControlMessageCodec.Decode(bytes).Status == DecodeStatus.Invalid) patched = true;
                    else { bytes[i + 3] = 0; bytes[i + 2] = 0; }
                }
            }
            Assert.That(patched, Is.True, "nenhum tamanho de vetor corrompido foi rejeitado");
        }

        [Test]
        public void Decode_ValidPairingResultAndWelcome_AreOk()
        {
            var pr = ControlMessageCodec.Decode(ControlMessageCodec.Encode(new PairingResultMessage(true, "t")));
            Assert.That(pr.Status, Is.EqualTo(DecodeStatus.Ok));
            var w = ControlMessageCodec.Decode(ControlMessageCodec.Encode(new WelcomeMessage("PC",
                new[] { new MonitorDescriptor(1, 2560, 1080, "DISPLAY1", true) },
                new[] { new ScreenLeaseInfo(2, ScreenKind.Virtual, 3840, 2160) })));
            Assert.That(w.Status, Is.EqualTo(DecodeStatus.Ok));
        }

        [Test]
        public void Decode_Garbage_IsInvalid()
        {
            Assert.That(ControlMessageCodec.Decode(new byte[] { 1, 2, 3 }).Status, Is.EqualTo(DecodeStatus.Invalid));
            Assert.That(ControlMessageCodec.Decode(new byte[64]).Status, Is.EqualTo(DecodeStatus.Invalid));
        }

        [Test]
        public void Decode_OtherVersion_IsIncompatible()
        {
            var bytes = ControlMessageCodec.Encode(new PingMessage(1), protocolVersion: 1);
            var result = ControlMessageCodec.Decode(bytes);
            Assert.That(result.Status, Is.EqualTo(DecodeStatus.IncompatibleVersion));
            Assert.That(result.Version, Is.EqualTo((ushort)1));
        }

        [Test]
        public void FrameReader_HandlesSplitAndCoalescedFrames()
        {
            var a = FrameCodec.Frame(new byte[] { 1, 2, 3 });
            var b = FrameCodec.Frame(new byte[] { 9 });
            var stream = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, stream, 0, a.Length);
            Buffer.BlockCopy(b, 0, stream, a.Length, b.Length);

            var reader = new FrameReader();
            reader.Append(stream, 0, 2);                        // metade do header
            Assert.That(reader.TryRead(out _), Is.False);
            reader.Append(stream, 2, stream.Length - 2);        // resto + frame inteiro colado
            Assert.That(reader.TryRead(out var p1), Is.True);
            Assert.That(p1, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(reader.TryRead(out var p2), Is.True);
            Assert.That(p2, Is.EqualTo(new byte[] { 9 }));
            Assert.That(reader.TryRead(out _), Is.False);
        }

        [Test]
        public void FrameReader_RejectsOversizedFrame()
        {
            var reader = new FrameReader();
            var header = BitConverter.GetBytes(ControlProtocol.MaxFrameBytes + 1);
            reader.Append(header, 0, 4);
            Assert.That(reader.TryRead(out _), Is.False);
            Assert.That(reader.IsCorrupt, Is.True);
        }

        [Test]
        public void Frame_RejectsOversizedPayload() =>
            Assert.Throws<ArgumentException>(() => FrameCodec.Frame(new byte[ControlProtocol.MaxFrameBytes + 1]));
    }
}
