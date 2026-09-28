namespace ScreenPortal.Host.Domain.Video;

/// <summary>
/// Implementação de referência da remontagem (o headset faz o mesmo em C++, receiver.cpp).
/// Mantém só o frame mais recente em montagem: se chega fragmento de um frame mais novo,
/// o frame incompleto é descartado ("descartar frame velho é melhor que enfileirar latência").
/// Usada pelo modo <c>receive</c> do host e pelos testes.
/// </summary>
public sealed class FrameReassembler
{
    public delegate void FrameHandler(ReadOnlySpan<byte> accessUnit, in VideoPacketHeader header);

    private readonly byte[] _frame;
    private readonly bool[] _received = new bool[ushort.MaxValue + 1];
    private uint _frameId;
    private bool _active;
    private bool _hasFrame;
    private int _fragCount;
    private int _receivedCount;
    private int _lastFragSize;
    private VideoPacketHeader _first;
    private bool _waitingForKeyframe = true;

    public FrameReassembler(int maxFrameBytes = 4 * 1024 * 1024) => _frame = new byte[maxFrameBytes];

    public long FramesCompleted { get; private set; }
    public long FramesDropped { get; private set; }

    public void Push(ReadOnlySpan<byte> packet, FrameHandler onFrame)
    {
        if (!VideoPacketHeader.TryRead(packet, out var h)) return;

        if (!_active || h.FrameId != _frameId)
        {
            // Fragmento atrasado de um frame em montagem ou já concluído/descartado.
            if (_hasFrame && !IsNewer(_frameId, h.FrameId)) return;
            if (_active) { FramesDropped++; _waitingForKeyframe = true; }
            Begin(h);
        }

        if (h.FragCount != _fragCount || _received[h.FragIndex]) return;
        int offset = h.FragIndex * VideoPacketHeader.MaxPayload;
        if (offset + h.PayloadSize > _frame.Length) { _active = false; FramesDropped++; return; }

        packet.Slice(VideoPacketHeader.Size, h.PayloadSize).CopyTo(_frame.AsSpan(offset));
        _received[h.FragIndex] = true;
        _receivedCount++;
        if (h.FragIndex == h.FragCount - 1) _lastFragSize = h.PayloadSize;

        if (_receivedCount == _fragCount)
        {
            _active = false;
            if (_waitingForKeyframe && !_first.IsKeyframe) { FramesDropped++; return; }
            _waitingForKeyframe = false;
            FramesCompleted++;
            int size = (_fragCount - 1) * VideoPacketHeader.MaxPayload + _lastFragSize;
            onFrame(_frame.AsSpan(0, size), _first);
        }
    }

    private void Begin(in VideoPacketHeader h)
    {
        Array.Clear(_received, 0, _fragCount); // só índices < _fragCount anterior foram marcados
        _hasFrame = true;
        _active = true;
        _frameId = h.FrameId;
        _fragCount = h.FragCount;
        _receivedCount = 0;
        _lastFragSize = 0;
        _first = h;
    }

    /// <summary>true se <paramref name="candidate"/> é mais novo que <paramref name="current"/> (com wrap de u32).</summary>
    public static bool IsNewer(uint current, uint candidate) => (int)(candidate - current) > 0;
}
