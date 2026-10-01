using System.Buffers.Binary;

namespace ScreenPortal.Host.Domain.Audio;

/// <summary>
/// Pacote de áudio (protocol/audio-wire.md): "SPAU" · versão · canais · seq · capture_us · payload Opus. O mesmo layout no
/// C++ do <c>.aar</c>. Sem alocação: escreve e lê sobre spans.
/// </summary>
public readonly record struct AudioPacketHeader(byte Channels, uint Seq, ulong CaptureUs)
{
    public const int Size = 18;
    public const byte Version = 1;
    public const int MaxPayload = 1200;
    private static ReadOnlySpan<byte> Magic => "SPAU"u8;

    /// <returns>Tamanho total escrito.</returns>
    public int Write(Span<byte> destination, ReadOnlySpan<byte> payload)
    {
        Magic.CopyTo(destination);
        destination[4] = Version;
        destination[5] = Channels;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[6..], Seq);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[10..], CaptureUs);
        payload.CopyTo(destination[Size..]);
        return Size + payload.Length;
    }

    public static bool TryRead(ReadOnlySpan<byte> packet, out AudioPacketHeader header, out ReadOnlySpan<byte> payload)
    {
        header = default;
        payload = default;
        if (packet.Length <= Size || !packet[..4].SequenceEqual(Magic) || packet[4] != Version) return false;
        header = new AudioPacketHeader(packet[5], BinaryPrimitives.ReadUInt32LittleEndian(packet[6..]),
            BinaryPrimitives.ReadUInt64LittleEndian(packet[10..]));
        payload = packet[Size..];
        return true;
    }
}
