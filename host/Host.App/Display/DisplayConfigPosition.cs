using System;
using System.Runtime.InteropServices;

namespace ScreenPortal.Host.App.Display
{
    /// <summary>
    /// Move um monitor no arranjo do Windows pela API de topologia (QueryDisplayConfig/SetDisplayConfig, a mesma do
    /// Configurações de vídeo). Usado ao recriar (spec 2D): o monitor novo ocupa o lugar do antigo, e o mouse mantém a
    /// continuidade entre as telas. Sintaxe simples de propósito (também compila no Add-Type do PowerShell, para testes).
    /// </summary>
    public static class DisplayConfigPosition
    {
        /// <returns>0 se aplicou; senão o código de erro do Win32 (ou -1 se o monitor não foi encontrado).</returns>
        public static int SetPosition(string gdiDeviceName, int x, int y)
        {
            var one = new System.Collections.Generic.Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
            one[gdiDeviceName] = new[] { x, y };
            return SetPositions(one);
        }

        /// <summary>Vários monitores num único SetDisplayConfig (3A: o arranjo inteiro de uma vez, uma só reconfiguração).</summary>
        /// <param name="positions">nome GDI → { x, y }.</param>
        /// <returns>0 se aplicou; senão o erro do Win32 (−1 = algum monitor não foi encontrado).</returns>
        public static int SetPositions(System.Collections.Generic.IDictionary<string, int[]> positions)
        {
            uint pathCount, modeCount;
            int err = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out pathCount, out modeCount);
            if (err != 0) return err;
            var paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];
            err = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (err != 0) return err;

            int found = 0;
            for (int i = 0; i < pathCount; i++)
            {
                var source = paths[i].sourceInfo;
                var name = new DisplayConfigSourceDeviceName();
                name.header.type = DeviceInfoGetSourceName;
                name.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigSourceDeviceName));
                name.header.adapterId = source.adapterId;
                name.header.id = source.id;
                if (DisplayConfigGetDeviceInfo(ref name) != 0) continue;
                int[] position;
                if (name.viewGdiDeviceName == null || !positions.TryGetValue(name.viewGdiDeviceName, out position)) continue;

                uint modeIndex = source.modeInfoIdx;
                if (modeIndex >= modeCount || modes[modeIndex].infoType != ModeInfoTypeSource) return -1;
                modes[modeIndex].sourceMode.position.x = position[0];
                modes[modeIndex].sourceMode.position.y = position[1];
                found++;
            }
            if (found < positions.Count) return -1;
            return SetDisplayConfig(pathCount, paths, modeCount, modes, SdcApply | SdcUseSuppliedDisplayConfig | SdcSaveToDatabase);
        }

        private const uint QdcOnlyActivePaths = 0x2;
        private const uint SdcApply = 0x80, SdcUseSuppliedDisplayConfig = 0x20, SdcSaveToDatabase = 0x200;
        private const int DeviceInfoGetSourceName = 1;
        private const uint ModeInfoTypeSource = 1;

        [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigPathSourceInfo { public Luid adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigRational { public uint Numerator; public uint Denominator; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigPathTargetInfo
        {
            public Luid adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation;
            public uint scaling; public DisplayConfigRational refreshRate; public uint scanLineOrdering; public int targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigPathInfo { public DisplayConfigPathSourceInfo sourceInfo; public DisplayConfigPathTargetInfo targetInfo; public uint flags; }

        [StructLayout(LayoutKind.Sequential)] private struct PointL { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigSourceMode { public uint width; public uint height; public uint pixelFormat; public PointL position; }

        // DISPLAYCONFIG_MODE_INFO: union de 48 bytes (target mode é o maior); só o source mode é lido/escrito aqui.
        [StructLayout(LayoutKind.Explicit, Size = 64)]
        private struct DisplayConfigModeInfo
        {
            [FieldOffset(0)] public uint infoType;
            [FieldOffset(4)] public uint id;
            [FieldOffset(8)] public Luid adapterId;
            [FieldOffset(16)] public DisplayConfigSourceMode sourceMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigDeviceInfoHeader { public int type; public uint size; public Luid adapterId; public uint id; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayConfigSourceDeviceName
        {
            public DisplayConfigDeviceInfoHeader header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
        }

        [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);
        [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DisplayConfigPathInfo[] paths,
            ref uint modeCount, [Out] DisplayConfigModeInfo[] modes, IntPtr topology);
        [DllImport("user32.dll")] private static extern int SetDisplayConfig(uint pathCount, [In] DisplayConfigPathInfo[] paths,
            uint modeCount, [In] DisplayConfigModeInfo[] modes, uint flags);
        [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName name);
    }
}
