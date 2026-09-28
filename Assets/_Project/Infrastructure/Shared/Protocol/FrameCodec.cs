using System;
using System.Buffers.Binary;

namespace ScreenPortal.Infrastructure.Protocol
{
    /// <summary>Frame = [u32 LE tamanho][payload]. TCP é um fluxo: o leitor remonta frames partidos ou colados.</summary>
    public static class FrameCodec
    {
        public const int HeaderSize = 4;

        public static byte[] Frame(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length > ControlProtocol.MaxFrameBytes)
                throw new ArgumentException("Payload acima de " + ControlProtocol.MaxFrameBytes + " bytes.", nameof(payload));
            var frame = new byte[HeaderSize + payload.Length];
            BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
            Buffer.BlockCopy(payload, 0, frame, HeaderSize, payload.Length);
            return frame;
        }
    }

    public sealed class FrameReader
    {
        private byte[] _buffer = new byte[4096];
        private int _count;

        /// <summary>Tamanho inválido recebido: a conexão deve ser encerrada.</summary>
        public bool IsCorrupt { get; private set; }

        public void Append(byte[] data, int offset, int count)
        {
            if (_count + count > _buffer.Length)
            {
                var bigger = new byte[Math.Max(_buffer.Length * 2, _count + count)];
                Buffer.BlockCopy(_buffer, 0, bigger, 0, _count);
                _buffer = bigger;
            }
            Buffer.BlockCopy(data, offset, _buffer, _count, count);
            _count += count;
        }

        public bool TryRead(out byte[] payload)
        {
            payload = null;
            if (IsCorrupt || _count < FrameCodec.HeaderSize) return false;
            int length = BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(_buffer, 0, FrameCodec.HeaderSize));
            if (length < 0 || length > ControlProtocol.MaxFrameBytes)
            {
                IsCorrupt = true;
                return false;
            }
            int total = FrameCodec.HeaderSize + length;
            if (_count < total) return false;

            payload = new byte[length];
            Buffer.BlockCopy(_buffer, FrameCodec.HeaderSize, payload, 0, length);
            Buffer.BlockCopy(_buffer, total, _buffer, 0, _count - total);
            _count -= total;
            return true;
        }
    }
}
