using ScreenPortal.Host.Domain.Video;

namespace ScreenPortal.Host.Tests.Video;

public class PacketizationTests
{
    private sealed class ListSink : IPacketSink
    {
        public readonly List<byte[]> Packets = new();
        public void OnPacket(ReadOnlySpan<byte> packet) => Packets.Add(packet.ToArray());
    }

    private static byte[] Payload(int size, int seed = 1)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        return data;
    }

    [Fact]
    public void Header_RoundTrips()
    {
        var h = new VideoPacketHeader(VideoPacketHeader.FlagKeyframe, 3, 2, 5, 1400, 0xDEADBEEF, 123_456_789_012UL);
        var buf = new byte[VideoPacketHeader.MaxPacket];
        h.Write(buf);

        Assert.True(VideoPacketHeader.TryRead(buf, out var read));
        Assert.Equal(h, read);
        Assert.True(read.IsKeyframe);
    }

    [Fact]
    public void Header_RejectsBadMagicAndTruncatedPayload()
    {
        var buf = new byte[VideoPacketHeader.Size + 10];
        new VideoPacketHeader(0, 0, 0, 1, 10, 1, 1).Write(buf);
        Assert.True(VideoPacketHeader.TryRead(buf, out _));

        Assert.False(VideoPacketHeader.TryRead(buf.AsSpan(0, VideoPacketHeader.Size + 5), out _));
        buf[0] = 0;
        Assert.False(VideoPacketHeader.TryRead(buf, out _));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1400, 1)]
    [InlineData(1401, 2)]
    [InlineData(100_000, 72)]
    public void Packetizer_SplitsIntoExpectedFragments(int size, int expected)
    {
        var sink = new ListSink();
        new VideoPacketizer(7).Packetize(Payload(size), keyframe: false, captureTimeUs: 42, sink);

        Assert.Equal(expected, sink.Packets.Count);
        Assert.All(sink.Packets, p => Assert.True(p.Length <= VideoPacketHeader.MaxPacket));
        for (int i = 0; i < sink.Packets.Count; i++)
        {
            Assert.True(VideoPacketHeader.TryRead(sink.Packets[i], out var h));
            Assert.Equal(7, h.StreamId);
            Assert.Equal(i, h.FragIndex);
            Assert.Equal(expected, h.FragCount);
            Assert.Equal(42UL, h.CaptureTimeUs);
        }
    }

    [Fact]
    public void Reassembler_RebuildsFramesEvenWithReorderedFragments()
    {
        var sink = new ListSink();
        var packetizer = new VideoPacketizer(0);
        var key = Payload(5000, 1);
        var delta = Payload(3000, 2);
        packetizer.Packetize(key, keyframe: true, 10, sink);
        packetizer.Packetize(delta, keyframe: false, 20, sink);

        // Inverte a ordem dos fragmentos do primeiro frame (4 fragmentos).
        var ordered = new List<byte[]>(sink.Packets);
        ordered.Reverse(0, 4);

        var frames = new List<(byte[] data, ulong ts)>();
        var reassembler = new FrameReassembler();
        foreach (var p in ordered)
            reassembler.Push(p, (au, in h) => frames.Add((au.ToArray(), h.CaptureTimeUs)));

        Assert.Equal(2, frames.Count);
        Assert.Equal(key, frames[0].data);
        Assert.Equal(10UL, frames[0].ts);
        Assert.Equal(delta, frames[1].data);
    }

    [Fact]
    public void Reassembler_DropsIncompleteFrameAndWaitsForNextKeyframe()
    {
        var sink = new ListSink();
        var packetizer = new VideoPacketizer(0);
        packetizer.Packetize(Payload(3000, 1), keyframe: true, 1, sink);   // frame 0: 3 pacotes
        packetizer.Packetize(Payload(3000, 2), keyframe: false, 2, sink);  // frame 1: 3 pacotes (perde 1)
        packetizer.Packetize(Payload(3000, 3), keyframe: false, 3, sink);  // frame 2: delta sem referência
        packetizer.Packetize(Payload(3000, 4), keyframe: true, 4, sink);   // frame 3: keyframe

        sink.Packets.RemoveAt(4); // fragmento do meio do frame 1

        var timestamps = new List<ulong>();
        var reassembler = new FrameReassembler();
        foreach (var p in sink.Packets)
            reassembler.Push(p, (_, in h) => timestamps.Add(h.CaptureTimeUs));

        Assert.Equal(new ulong[] { 1, 4 }, timestamps);
        Assert.Equal(2, reassembler.FramesDropped);
    }

    [Fact]
    public void Reassembler_IgnoresLateDuplicateOfCompletedFrame()
    {
        var sink = new ListSink();
        new VideoPacketizer(0).Packetize(Payload(2000), keyframe: true, 1, sink);

        int count = 0;
        var reassembler = new FrameReassembler();
        foreach (var p in sink.Packets) reassembler.Push(p, (_, in _) => count++);
        reassembler.Push(sink.Packets[0], (_, in _) => count++);

        Assert.Equal(1, count);
        Assert.Equal(0, reassembler.FramesDropped);
    }

    [Theory]
    [InlineData(0u, 1u, true)]
    [InlineData(1u, 0u, false)]
    [InlineData(uint.MaxValue, 0u, true)]
    [InlineData(5u, 5u, false)]
    public void IsNewer_HandlesWraparound(uint current, uint candidate, bool expected) =>
        Assert.Equal(expected, FrameReassembler.IsNewer(current, candidate));
}
