using System;
using System.Runtime.InteropServices;

namespace ScreenPortal.Host.App.Display
{
    /// <summary>
    /// Escala de um monitor ("Alterar o tamanho do texto, dos aplicativos e de outros itens", Fase 3A). Não há API
    /// documentada: o painel Configurações usa <c>DisplayConfigGetDeviceInfo/SetDeviceInfo</c> com os tipos −3/−4, em
    /// passos relativos à escala recomendada. Sintaxe simples (também compila no Add-Type do PowerShell, para testes).
    /// </summary>
    public static class DisplayScale
    {
        /// <summary>Os passos do Windows, em %.</summary>
        public static readonly int[] Steps = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

        /// <returns>Escala atual em %, ou 0 se o monitor não foi encontrado.</returns>
        public static int Get(string gdiDeviceName)
        {
            DisplayConfigSourceDeviceName name;
            if (!FindSource(gdiDeviceName, out name)) return 0;
            var get = new GetDpi();
            get.header.type = GetDpiType;
            get.header.size = (uint)Marshal.SizeOf(typeof(GetDpi));
            get.header.adapterId = name.header.adapterId;
            get.header.id = name.header.id;
            if (DisplayConfigGetDeviceInfo(ref get) != 0) return 0;
            int recommended = -get.minScaleRel;
            int index = Math.Max(0, Math.Min(Steps.Length - 1, recommended + get.curScaleRel));
            return Steps[index];
        }

        /// <returns>0 se aplicou; senão o erro (−1 = monitor não encontrado, −2 = escala fora dos passos do monitor).</returns>
        public static int Set(string gdiDeviceName, int percent)
        {
            DisplayConfigSourceDeviceName name;
            if (!FindSource(gdiDeviceName, out name)) return -1;
            var get = new GetDpi();
            get.header.type = GetDpiType;
            get.header.size = (uint)Marshal.SizeOf(typeof(GetDpi));
            get.header.adapterId = name.header.adapterId;
            get.header.id = name.header.id;
            int err = DisplayConfigGetDeviceInfo(ref get);
            if (err != 0) return err;
            int target = Array.IndexOf(Steps, percent);
            int recommended = -get.minScaleRel;
            int relative = target - recommended;
            if (target < 0 || relative < get.minScaleRel || relative > get.maxScaleRel) return -2;
            var set = new SetDpi();
            set.header.type = SetDpiType;
            set.header.size = (uint)Marshal.SizeOf(typeof(SetDpi));
            set.header.adapterId = name.header.adapterId;
            set.header.id = name.header.id;
            set.scaleRel = relative;
            return DisplayConfigSetDeviceInfo(ref set);
        }

        private static bool FindSource(string gdiDeviceName, out DisplayConfigSourceDeviceName found)
        {
            found = new DisplayConfigSourceDeviceName();
            uint pathCount, modeCount;
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out pathCount, out modeCount) != 0) return false;
            var paths = new byte[pathCount * PathInfoSize];
            var modes = new byte[modeCount * ModeInfoSize];
            if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return false;
            for (int i = 0; i < pathCount; i++)
            {
                // DISPLAYCONFIG_PATH_INFO começa com o source: LUID (8) + id (4)
                var name = new DisplayConfigSourceDeviceName();
                name.header.type = DeviceInfoGetSourceName;
                name.header.size = (uint)Marshal.SizeOf(typeof(DisplayConfigSourceDeviceName));
                name.header.adapterId.LowPart = BitConverter.ToUInt32(paths, i * PathInfoSize);
                name.header.adapterId.HighPart = BitConverter.ToInt32(paths, i * PathInfoSize + 4);
                name.header.id = BitConverter.ToUInt32(paths, i * PathInfoSize + 8);
                if (DisplayConfigGetDeviceInfo(ref name) != 0) continue;
                if (!string.Equals(name.viewGdiDeviceName, gdiDeviceName, StringComparison.OrdinalIgnoreCase)) continue;
                found = name;
                return true;
            }
            return false;
        }

        private const uint QdcOnlyActivePaths = 0x2;
        private const int DeviceInfoGetSourceName = 1;
        private const int GetDpiType = -3, SetDpiType = -4;
        private const int PathInfoSize = 72, ModeInfoSize = 64;

        [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceInfoHeader { public int type; public uint size; public Luid adapterId; public uint id; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayConfigSourceDeviceName
        {
            public DeviceInfoHeader header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GetDpi { public DeviceInfoHeader header; public int minScaleRel; public int curScaleRel; public int maxScaleRel; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SetDpi { public DeviceInfoHeader header; public int scaleRel; }

        [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);
        [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] byte[] paths,
            ref uint modeCount, [Out] byte[] modes, IntPtr topology);
        [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName name);
        [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref GetDpi info);
        [DllImport("user32.dll")] private static extern int DisplayConfigSetDeviceInfo(ref SetDpi info);
    }
}
