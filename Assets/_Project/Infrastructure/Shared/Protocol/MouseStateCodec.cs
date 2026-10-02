using System;
using ScreenPortal.App.Connection;

namespace ScreenPortal.Infrastructure.Protocol
{
    /// <summary>MouseState v1: 44 bytes little-endian (protocol/mouse-state-v1.md). Sem alocação.</summary>
    public static class MouseStateCodec
    {
        public const int Size = 44;
        private const uint Magic = 0x534D5053; // "SPMS" em little-endian

        public static void Write(in MouseState s, Span<byte> buffer)
        {
            if (buffer.Length < Size) throw new ArgumentException("Buffer menor que " + Size + " bytes.", nameof(buffer));
            U32(buffer, 0, Magic);
            U32(buffer, 4, s.Seq);
            buffer[8] = (byte)s.Mode;
            buffer[9] = s.Buttons;
            buffer[10] = s.Flags;
            buffer[11] = 0;
            U32(buffer, 12, s.Epoch);
            U32(buffer, 16, s.ScreenKey);
            U32(buffer, 20, (uint)s.X);
            U32(buffer, 24, (uint)s.Y);
            U32(buffer, 28, s.ShapeId);
            U32(buffer, 32, (uint)s.Dx);
            U32(buffer, 36, (uint)s.Dy);
            U32(buffer, 40, (uint)s.Wheel);
        }

        /// <returns>false se não é um MouseState v1 (tamanho, magic ou modo inválido).</returns>
        public static bool TryRead(ReadOnlySpan<byte> buffer, out MouseState s)
        {
            s = default;
            if (buffer.Length < Size || U32(buffer, 0) != Magic || buffer[8] > (byte)MouseMode.Outside) return false;
            s.Seq = U32(buffer, 4);
            s.Mode = (MouseMode)buffer[8];
            s.Buttons = buffer[9];
            s.Flags = buffer[10];
            s.Epoch = U32(buffer, 12);
            s.ScreenKey = U32(buffer, 16);
            s.X = (int)U32(buffer, 20);
            s.Y = (int)U32(buffer, 24);
            s.ShapeId = U32(buffer, 28);
            s.Dx = (int)U32(buffer, 32);
            s.Dy = (int)U32(buffer, 36);
            s.Wheel = (int)U32(buffer, 40);
            return true;
        }

        private static void U32(Span<byte> b, int at, uint v)
        {
            b[at] = (byte)v;
            b[at + 1] = (byte)(v >> 8);
            b[at + 2] = (byte)(v >> 16);
            b[at + 3] = (byte)(v >> 24);
        }

        private static uint U32(ReadOnlySpan<byte> b, int at) =>
            b[at] | (uint)b[at + 1] << 8 | (uint)b[at + 2] << 16 | (uint)b[at + 3] << 24;
    }
}
