namespace ScreenPortal.App.Connection
{
    /// <summary>Onde o mouse está (spec 3B §2).</summary>
    public enum MouseMode : byte
    {
        /// <summary>Numa tela do Quest: o Windows recebe o mouse; o headset desenha o cursor.</summary>
        Screen = 0,
        /// <summary>Fora das telas: o host engole o mouse; o headset move o cursor 3D.</summary>
        World = 1,
        /// <summary>Modo edição: o host engole o mouse (botões e roda também); o cursor 3D é ponteiro de manipulação.</summary>
        Edit = 2,
        /// <summary>Num monitor do PC fora do Quest: cursor invisível no headset.</summary>
        Outside = 3,
    }

    /// <summary>
    /// Estado completo do mouse num datagrama (protocol/mouse-state-v1.md). Struct: lido e escrito sem alocação.
    /// </summary>
    public struct MouseState
    {
        public const byte ButtonLeft = 1, ButtonRight = 2, ButtonMiddle = 4;
        public const byte FlagShift = 1, FlagCtrlAlt = 2;

        public uint Seq;
        public MouseMode Mode;
        public byte Buttons;
        public byte Flags;
        public uint Epoch;
        public uint ScreenKey;
        public int X;
        public int Y;
        public uint ShapeId;
        public int Dx;
        public int Dy;
        public int Wheel;

        public bool Left => (Buttons & ButtonLeft) != 0;
        public bool Right => (Buttons & ButtonRight) != 0;
        public bool Shift => (Flags & FlagShift) != 0;
        public bool CtrlAlt => (Flags & FlagCtrlAlt) != 0;
    }
}
