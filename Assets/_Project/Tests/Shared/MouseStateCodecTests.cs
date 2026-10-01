using System;
using NUnit.Framework;
using ScreenPortal.App.Connection;
using ScreenPortal.Infrastructure.Protocol;

namespace ScreenPortal.Tests
{
    public class MouseStateCodecTests
    {
        [Test]
        public void RoundTrip_KeepsEveryField()
        {
            var s = new MouseState
            {
                Seq = 77, Mode = MouseMode.World, Buttons = MouseState.ButtonLeft | MouseState.ButtonMiddle,
                Flags = MouseState.FlagShift, Epoch = 9, ScreenKey = 3, X = 2559, Y = -1, ShapeId = 0xCAFEBABE,
                Dx = -1234, Dy = 56, Wheel = -240,
            };
            var buffer = new byte[MouseStateCodec.Size];
            MouseStateCodec.Write(in s, buffer);
            Assert.That(MouseStateCodec.TryRead(buffer, out var back), Is.True);
            Assert.That(back, Is.EqualTo(s));
            Assert.That(back.Left && !back.Right && back.Shift && !back.CtrlAlt, Is.True);
        }

        [Test]
        public void Garbage_IsRejected()
        {
            var buffer = new byte[MouseStateCodec.Size];
            Assert.That(MouseStateCodec.TryRead(buffer, out _), Is.False, "sem magic");
            MouseStateCodec.Write(new MouseState { Mode = MouseMode.Screen }, buffer);
            Assert.That(MouseStateCodec.TryRead(buffer.AsSpan(0, 40), out _), Is.False, "curto");
            buffer[8] = 9;
            Assert.That(MouseStateCodec.TryRead(buffer, out _), Is.False, "modo inválido");
        }

        [Test]
        public void ReadAndWrite_DoNotAllocate()
        {
            var buffer = new byte[MouseStateCodec.Size];
            var s = new MouseState { Seq = 1 };
            MouseStateCodec.Write(in s, buffer);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                MouseStateCodec.Write(in s, buffer);
                MouseStateCodec.TryRead(buffer, out s);
            }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
        }
    }
}
