using System.Buffers.Binary;

namespace ScreenPortal.Host.Domain.Timing;

/// <summary>Mensagem de sincronização de relógio (protocol/video-packet-v1.md, seção "Sincronização").</summary>
public readonly record struct ClockSyncMessage(uint Seq, ulong ClientSendUs, ulong HostUs)
{
    public const int Size = 24;
    public const uint Magic = 0x434B4C43; // "CLKC"

    public void Write(Span<byte> dst)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(dst, Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(dst[4..], Seq);
        BinaryPrimitives.WriteUInt64LittleEndian(dst[8..], ClientSendUs);
        BinaryPrimitives.WriteUInt64LittleEndian(dst[16..], HostUs);
    }

    public static bool TryRead(ReadOnlySpan<byte> src, out ClockSyncMessage msg)
    {
        msg = default;
        if (src.Length < Size || BinaryPrimitives.ReadUInt32LittleEndian(src) != Magic) return false;
        msg = new ClockSyncMessage(
            BinaryPrimitives.ReadUInt32LittleEndian(src[4..]),
            BinaryPrimitives.ReadUInt64LittleEndian(src[8..]),
            BinaryPrimitives.ReadUInt64LittleEndian(src[16..]));
        return true;
    }
}

/// <summary>Relógio monotônico em µs baseado no QPC — mesmo domínio do SystemRelativeTime do WGC.</summary>
public static class HostClock
{
    private static readonly double TicksToUs = 1_000_000.0 / System.Diagnostics.Stopwatch.Frequency;

    public static ulong NowUs() => (ulong)(System.Diagnostics.Stopwatch.GetTimestamp() * TicksToUs);
}
