namespace ScreenPortal.Host.Domain.Video;

/// <summary>Recebe cada pacote pronto (header + payload). O buffer só é válido durante a chamada.</summary>
public interface IPacketSink
{
    void OnPacket(ReadOnlySpan<byte> packet);
}

/// <summary>
/// Fatia um access unit H.264 (Annex-B) em pacotes de até <see cref="VideoPacketHeader.MaxPacket"/> bytes.
/// Reutiliza um único buffer interno: zero alocação por frame.
/// </summary>
public sealed class VideoPacketizer
{
    private readonly byte[] _scratch = new byte[VideoPacketHeader.MaxPacket];
    private readonly ushort _streamId;
    private uint _nextFrameId;

    /// <param name="firstFrameId">
    /// Continuação de um stream já recebido (captura recriada no mesmo destino): o receptor descarta frameIds que não
    /// sejam mais novos que o último visto, então o pipeline novo segue a numeração do anterior.
    /// </param>
    public VideoPacketizer(ushort streamId, uint firstFrameId = 0)
    {
        _streamId = streamId;
        _nextFrameId = firstFrameId;
    }

    public uint NextFrameId => _nextFrameId;

    public static int FragmentCount(int accessUnitSize) =>
        Math.Max(1, (accessUnitSize + VideoPacketHeader.MaxPayload - 1) / VideoPacketHeader.MaxPayload);

    /// <returns>O frameId atribuído ao access unit.</returns>
    public uint Packetize(ReadOnlySpan<byte> accessUnit, bool keyframe, ulong captureTimeUs, IPacketSink sink)
    {
        int fragCount = FragmentCount(accessUnit.Length);
        if (fragCount > ushort.MaxValue)
            throw new ArgumentException("Access unit grande demais para um frame.", nameof(accessUnit));

        uint frameId = _nextFrameId++;
        byte flags = keyframe ? (byte)(VideoPacketHeader.FlagKeyframe | VideoPacketHeader.FlagConfig) : (byte)0;
        var buffer = _scratch.AsSpan();

        for (int i = 0; i < fragCount; i++)
        {
            int offset = i * VideoPacketHeader.MaxPayload;
            int size = Math.Min(VideoPacketHeader.MaxPayload, accessUnit.Length - offset);

            new VideoPacketHeader(flags, _streamId, (ushort)i, (ushort)fragCount, (ushort)size, frameId, captureTimeUs)
                .Write(buffer);
            accessUnit.Slice(offset, size).CopyTo(buffer[VideoPacketHeader.Size..]);
            sink.OnPacket(buffer[..(VideoPacketHeader.Size + size)]);
        }

        return frameId;
    }
}
