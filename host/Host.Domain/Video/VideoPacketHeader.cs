using System.Buffers.Binary;

namespace ScreenPortal.Host.Domain.Video;

/// <summary>
/// Header fixo de 24 bytes dos pacotes UDP de vídeo. Espelha protocol/video-packet-v1.md
/// e headset-native/.../cpp/packet.h — manter os três em sincronia.
/// </summary>
public readonly record struct VideoPacketHeader(
    byte Flags,
    ushort StreamId,
    ushort FragIndex,
    ushort FragCount,
    ushort PayloadSize,
    uint FrameId,
    ulong CaptureTimeUs)
{
    public const int Size = 24;
    public const int MaxPayload = 1400;
    public const int MaxPacket = Size + MaxPayload;
    public const ushort Magic = 0x5053;
    public const byte Version = 1;

    public const byte FlagKeyframe = 1 << 0;
    public const byte FlagConfig = 1 << 1;

    public bool IsKeyframe => (Flags & FlagKeyframe) != 0;

    public void Write(Span<byte> dst)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(dst, Magic);
        dst[2] = Version;
        dst[3] = Flags;
        BinaryPrimitives.WriteUInt16LittleEndian(dst[4..], StreamId);
        BinaryPrimitives.WriteUInt16LittleEndian(dst[6..], FragIndex);
        BinaryPrimitives.WriteUInt16LittleEndian(dst[8..], FragCount);
        BinaryPrimitives.WriteUInt16LittleEndian(dst[10..], PayloadSize);
        BinaryPrimitives.WriteUInt32LittleEndian(dst[12..], FrameId);
        BinaryPrimitives.WriteUInt64LittleEndian(dst[16..], CaptureTimeUs);
    }

    public static bool TryRead(ReadOnlySpan<byte> src, out VideoPacketHeader header)
    {
        header = default;
        if (src.Length < Size) return false;
        if (BinaryPrimitives.ReadUInt16LittleEndian(src) != Magic || src[2] != Version) return false;

        header = new VideoPacketHeader(
            Flags: src[3],
            StreamId: BinaryPrimitives.ReadUInt16LittleEndian(src[4..]),
            FragIndex: BinaryPrimitives.ReadUInt16LittleEndian(src[6..]),
            FragCount: BinaryPrimitives.ReadUInt16LittleEndian(src[8..]),
            PayloadSize: BinaryPrimitives.ReadUInt16LittleEndian(src[10..]),
            FrameId: BinaryPrimitives.ReadUInt32LittleEndian(src[12..]),
            CaptureTimeUs: BinaryPrimitives.ReadUInt64LittleEndian(src[16..]));

        return header.FragCount > 0
            && header.FragIndex < header.FragCount
            && header.PayloadSize <= MaxPayload
            && src.Length >= Size + header.PayloadSize;
    }
}
