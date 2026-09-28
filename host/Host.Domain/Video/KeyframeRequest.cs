using System.Buffers.Binary;

namespace ScreenPortal.Host.Domain.Video;

/// <summary>
/// Pedido de keyframe do decoder do headset (UDP 9101, protocol/video-packet-v1.md). Mesmo papel do PLI no
/// WebRTC: após perder um frame, o decoder pede um IDR em vez de esperar o periódico. Espelho em packet.h.
/// </summary>
public readonly record struct KeyframeRequest(ushort StreamId)
{
    public const int Size = 8;
    public const uint Magic = 0x5152464B; // "KFRQ"

    public void Write(Span<byte> dst)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(dst, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(dst[4..], StreamId);
        BinaryPrimitives.WriteUInt16LittleEndian(dst[6..], 0);
    }

    public static bool TryRead(ReadOnlySpan<byte> src, out KeyframeRequest request)
    {
        request = default;
        if (src.Length < Size || BinaryPrimitives.ReadUInt32LittleEndian(src) != Magic) return false;
        request = new KeyframeRequest(BinaryPrimitives.ReadUInt16LittleEndian(src[4..]));
        return true;
    }
}
