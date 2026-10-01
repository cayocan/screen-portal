using System.Linq;
using NUnit.Framework;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Cursor;

namespace ScreenPortal.Tests
{
    public class CursorSyncTests
    {
        private sealed class FakeSource : IMouseStateSource
        {
            public int Starts;
            public bool Has;
            public MouseState State;
            public double At;
            public bool Start(int port) { Starts++; return true; }
            public void Stop() { }

            public bool TryGetLatest(out MouseState state, out double receivedAt)
            {
                state = State;
                receivedAt = At;
                return Has;
            }
        }

        private sealed class Rig
        {
            public readonly ConnectionRig C = new ConnectionRig();
            public readonly FakeSource Source = new FakeSource();
            public readonly CursorSync Sync;
            public Rig() => Sync = new CursorSync(C.Machine, Source, C.Clock);

            public void Receive(ControlMessage message)
            {
                C.Channel.Push(ChannelEventKind.Message, message);
                C.Machine.Tick();
            }
        }

        [Test]
        public void Welcome_OpensThePort_AndAsksTheHost_EveryTime()
        {
            var r = new Rig();
            r.C.GoToStreaming(ConnectionRig.Welcome);
            Assert.That(r.Source.Starts, Is.EqualTo(1));
            Assert.That(r.C.Channel.Sent.OfType<StartCursorMessage>().Single().UdpPort, Is.EqualTo((ushort)CursorSync.RxPort));
        }

        [Test]
        public void Shapes_AreKeptOnce()
        {
            var r = new Rig();
            r.C.GoToStreaming(ConnectionRig.Welcome);
            int added = 0;
            r.Sync.ShapeAdded += _ => added++;
            var shape = new CursorShapeMessage(7, 1, 1, 0, 0, 1, new byte[4]);
            r.Receive(shape);
            r.Receive(new CursorShapeMessage(7, 1, 1, 0, 0, 1, new byte[4]));
            Assert.That(added, Is.EqualTo(1));
            Assert.That(r.Sync.TryGetShape(7, out var kept) && ReferenceEquals(kept, shape), Is.True);
        }

        [Test]
        public void FreshState_IsShown_StaleOrDisconnected_HidesTheCursor()
        {
            var r = new Rig();
            r.C.GoToStreaming(ConnectionRig.Welcome);
            r.Source.Has = true;
            r.Source.State = new MouseState { Mode = MouseMode.Screen, ScreenKey = 2, X = 100, Y = 50 };
            r.Source.At = r.C.Clock.Now;
            r.Sync.Tick();
            Assert.That(r.Sync.Current.Mode, Is.EqualTo(MouseMode.Screen));
            Assert.That(r.Sync.Current.X, Is.EqualTo(100));

            r.C.Clock.Now += CursorSync.StaleSeconds + 0.1;
            r.Sync.Tick();
            Assert.That(r.Sync.Current.Mode, Is.EqualTo(MouseMode.Outside), "sem pacote recente");
        }

        [Test]
        public void BeforeAnyPacket_TheCursorIsHidden()
        {
            var r = new Rig();
            r.C.GoToStreaming(ConnectionRig.Welcome);
            r.Sync.Tick();
            Assert.That(r.Sync.Current.Mode, Is.EqualTo(MouseMode.Outside));
        }
    }
}
