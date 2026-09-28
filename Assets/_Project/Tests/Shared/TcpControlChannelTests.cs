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
    public class TcpControlChannelTests
    {
        private static ChannelEventKind WaitEvent(TcpControlChannel channel, out ControlMessage message, int timeoutMs = 3000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                var kind = channel.Poll(out message);
                if (kind != ChannelEventKind.None) return kind;
                Thread.Sleep(5);
            }
            message = null;
            return ChannelEventKind.None;
        }

        [Test]
        public void ConnectsReceivesFramedMessagesAndSeesDisconnect()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using (var channel = new TcpControlChannel(2000))
            {
                channel.Connect(new HostEndpoint("test", "127.0.0.1", port));
                using (var server = listener.AcceptTcpClient())
                {
                    Assert.That(WaitEvent(channel, out _), Is.EqualTo(ChannelEventKind.Connected));

                    var frame = FrameCodec.Frame(ControlMessageCodec.Encode(new PairingRequiredMessage("123456")));
                    server.GetStream().Write(frame, 0, frame.Length);
                    Assert.That(WaitEvent(channel, out var message), Is.EqualTo(ChannelEventKind.Message));
                    Assert.That(((PairingRequiredMessage)message).Code, Is.EqualTo("123456"));

                    channel.Send(new PingMessage(5));
                    var reader = new FrameReader();
                    var buffer = new byte[256];
                    byte[] payload;
                    do
                    {
                        int n = server.GetStream().Read(buffer, 0, buffer.Length);
                        reader.Append(buffer, 0, n);
                    } while (!reader.TryRead(out payload));
                    Assert.That(((PingMessage)ControlMessageCodec.Decode(payload).Message).SentUs, Is.EqualTo(5UL));
                }
                Assert.That(WaitEvent(channel, out _), Is.EqualTo(ChannelEventKind.Disconnected));
            }
            listener.Stop();
        }

        [Test]
        public void ConnectToClosedPort_ReportsConnectFailed()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            using (var channel = new TcpControlChannel(1000))
            {
                channel.Connect(new HostEndpoint("test", "127.0.0.1", port));
                Assert.That(WaitEvent(channel, out _), Is.EqualTo(ChannelEventKind.ConnectFailed));
            }
        }

        [Test]
        public void Close_DiscardsEventsFromOldConnection()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using (var channel = new TcpControlChannel(2000))
            {
                channel.Connect(new HostEndpoint("test", "127.0.0.1", port));
                using (listener.AcceptTcpClient())
                {
                    Assert.That(WaitEvent(channel, out _), Is.EqualTo(ChannelEventKind.Connected));
                    channel.Close();
                }
                Assert.That(WaitEvent(channel, out _, 300), Is.EqualTo(ChannelEventKind.None), "Disconnected da conexão fechada por nós é descartado");
            }
            listener.Stop();
        }

        [Test]
        public void InMemoryPairingStore_KeepsTokensAndLastHost()
        {
            var store = new InMemoryPairingStore("dev");
            store.SaveToken("PC", "t");
            store.SaveLastHost(new HostEndpoint("PC", "1.2.3.4", 9200));
            Assert.That(store.GetToken("PC"), Is.EqualTo("t"));
            Assert.That(store.GetToken("outro"), Is.EqualTo(""));
            Assert.That(store.TryGetLastHost(out var host), Is.True);
            Assert.That(host.Address, Is.EqualTo("1.2.3.4"));
        }
    }
}
