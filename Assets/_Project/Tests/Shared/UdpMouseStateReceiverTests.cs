using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NUnit.Framework;
using ScreenPortal.App.Connection;
using ScreenPortal.Infrastructure.Net;
using ScreenPortal.Infrastructure.Protocol;

namespace ScreenPortal.Tests
{
    public class UdpMouseStateReceiverTests
    {
        private const int Port = 19310;

        private static void Send(UdpClient udp, uint seq, int x)
        {
            var buffer = new byte[MouseStateCodec.Size];
            MouseStateCodec.Write(new MouseState { Seq = seq, Mode = MouseMode.Screen, X = x }, buffer);
            udp.Send(buffer, buffer.Length, new IPEndPoint(IPAddress.Loopback, Port));
        }

        private static MouseState WaitFor(UdpMouseStateReceiver r, uint seq)
        {
            var sw = Stopwatch.StartNew();
            MouseState s = default;
            while (sw.ElapsedMilliseconds < 2000)
            {
                if (r.TryGetLatest(out s, out _) && s.Seq == seq) return s;
                Thread.Sleep(5);
            }
            return s;
        }

        [Test]
        public void KeepsTheNewest_IgnoresLateOnes_AcceptsAHostRestart()
        {
            var receiver = new UdpMouseStateReceiver(new MonotonicClock());
            Assert.That(receiver.Start(Port), Is.True);
            try
            {
                using (var udp = new UdpClient())
                {
                    Send(udp, 5, 50);
                    Assert.That(WaitFor(receiver, 5).X, Is.EqualTo(50));
                    Send(udp, 3, 30); // atrasado: fica o 5
                    Send(udp, 6, 60);
                    Assert.That(WaitFor(receiver, 6).X, Is.EqualTo(60));
                    receiver.TryGetLatest(out var latest, out _);
                    Assert.That(latest.Seq, Is.EqualTo(6u));

                    var restarted = new MouseState { Seq = 200_000, Mode = MouseMode.Screen };
                    var buffer = new byte[MouseStateCodec.Size];
                    MouseStateCodec.Write(in restarted, buffer);
                    udp.Send(buffer, buffer.Length, new IPEndPoint(IPAddress.Loopback, Port));
                    WaitFor(receiver, 200_000);
                    Send(udp, 1, 10); // host novo começou do zero
                    Assert.That(WaitFor(receiver, 1).X, Is.EqualTo(10));
                }
            }
            finally
            {
                receiver.Stop();
            }
        }
    }
}
