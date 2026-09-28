using System.Net;
using System.Net.Sockets;
using ScreenPortal.Host.Domain.Video;
using ScreenPortal.Host.Transport;

namespace ScreenPortal.Host.Tests.Spike;

public class KeyframeRequestTests
{
    [Fact]
    public void RoundTrips()
    {
        var buffer = new byte[KeyframeRequest.Size];
        new KeyframeRequest(3).Write(buffer);
        Assert.True(KeyframeRequest.TryRead(buffer, out var read));
        Assert.Equal((ushort)3, read.StreamId);
        buffer[0] = 0;
        Assert.False(KeyframeRequest.TryRead(buffer, out _));
    }

    [Fact]
    public void Listener_ReportsSenderAndStream()
    {
        IPAddress? from = null;
        ushort stream = 0;
        using var got = new ManualResetEventSlim();
        using var listener = new KeyframeRequestListener(0, (a, s) => { from = a; stream = s; got.Set(); });

        using var client = new UdpClient();
        var packet = new byte[KeyframeRequest.Size];
        new KeyframeRequest(2).Write(packet);
        client.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, listener.Port));

        Assert.True(got.Wait(2000));
        Assert.Equal(IPAddress.Loopback, from);
        Assert.Equal((ushort)2, stream);
    }
}
