using ScreenPortal.Host.Domain.Audio;

namespace ScreenPortal.Host.Tests.Audio;

public class AudioPacketTests
{
    [Fact]
    public void RoundTrip()
    {
        var buffer = new byte[AudioPacketHeader.Size + AudioPacketHeader.MaxPayload];
        byte[] opus = [1, 2, 3, 4, 5];
        int n = new AudioPacketHeader(2, 77, 123_456_789UL).Write(buffer, opus);

        Assert.True(AudioPacketHeader.TryRead(buffer.AsSpan(0, n), out var header, out var payload));
        Assert.Equal(new AudioPacketHeader(2, 77, 123_456_789UL), header);
        Assert.Equal(opus, payload.ToArray());
    }

    [Fact]
    public void RejectsWrongMagic_Version_AndEmptyPayload()
    {
        var buffer = new byte[64];
        int n = new AudioPacketHeader(1, 1, 1).Write(buffer, [9]);
        Assert.False(AudioPacketHeader.TryRead(buffer.AsSpan(0, AudioPacketHeader.Size), out _, out _), "sem payload");
        buffer[4] = 2;
        Assert.False(AudioPacketHeader.TryRead(buffer.AsSpan(0, n), out _, out _), "versão");
        buffer[4] = AudioPacketHeader.Version;
        buffer[0] = (byte)'X';
        Assert.False(AudioPacketHeader.TryRead(buffer.AsSpan(0, n), out _, out _), "magic");
    }
}
