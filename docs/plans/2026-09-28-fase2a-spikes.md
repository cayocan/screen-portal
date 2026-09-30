# Fase 2A — Spikes: driver de monitor virtual e capacidade do decoder — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Medir, com ferramentas repetíveis, (A) qual driver de monitor virtual (VDD ou SudoVDA) atende aos requisitos do brief e (B) quantas telas o decoder do Quest sustenta de forma estável no cenário real (1@60 + 3@15), e registrar as decisões nos ADRs 0007 e 0008.

**Architecture:** Parte A: `IVirtualDisplayDriver` com duas implementações (SudoVDA por IOCTL; VDD por XML + named pipe + `ChangeDisplaySettingsEx`), um enumerador de displays Win32 e o comando `ScreenPortalHost display-spike --driver <x>`, que executa 6 testes e grava um relatório. Parte B: correção da política de descarte no decoder nativo + pedido de keyframe por **UDP nativo** (headset → host, porta 9101, padrão PLI do WebRTC), cenários mistos no host (`stream --mix`) e script de medição com repetições. Spec: `docs/specs/2026-09-28-fase2a-spikes-design.md`.

**Tech Stack:** .NET 10 + Win32 P/Invoke (SetupAPI, DeviceIoControl, EnumDisplayDevices, ChangeDisplaySettingsEx, EnumWindows), Windows Graphics Capture, C++/NDK, PowerShell, xUnit.

## Global Constraints

- Drivers testados: **VDD** (VirtualDrivers, `winget install --id=VirtualDrivers.Virtual-Display-Driver -e`) e **SudoVDA** (já instalado). Parsec VDD fora.
- SudoVDA: interface `{e5bcc234-1e0c-418a-a0d4-ef8b7501414d}`; IOCTLs `ADD=0x222000`, `REMOVE=0x222004`, `GET_WATCHDOG=0x22200C`, `PING=0x222220`, `GET_PROTOCOL_VERSION=0x2223FC`; ping periódico obrigatório (watchdog).
- VDD: pipe `\\.\pipe\MTTVirtualDisplayPipe` (UTF-16), comandos `SETDISPLAYCOUNT n`, `RELOAD_DRIVER`, `GETSETTINGS`, `PING`; resolução exata = entrada em `<resolutions>` do `vdd_settings.xml` + `ChangeDisplaySettingsEx`.
- Resoluções de teste: 2560×1440, 3440×1440, 1920×1080; retrato 1440×2560; limite do encoder: larguras 4096 / 5120 / 7680.
- Keyframe request: UDP **9101**, 8 bytes LE: `magic u32 = 0x5152464B ("KFRQ")`, `stream_id u16`, `reserved u16`; o headset limita a **1 pedido a cada 100 ms** por stream.
- Espera por input buffer no decoder: **16 ms**. IDR periódico do host: **5 s** (era 1 s).
- Critério do MVP (decoder): cenário **1@60 + 3@15** a 2560×1080, 3 repetições de 60 s: foco ≥ 58 fps, demais ≥ 14 fps, latência média < 50 ms, 0 descartes do decoder.
- Condições: conteúdo fixo (`tools/text-test.html` em tela cheia), headset com bateria ≥ 80 % e no cabo.
- Branch `phase2a/spikes`. Context-mode para saídas grandes. Commits pequenos.

---

## File Structure

```
host/Host.Domain/Video/KeyframeRequest.cs        pacote KFRQ (puro)
host/Host.Domain/Video/MixSpec.cs                "60x1,15x3" → grupos (puro)
host/Host.Domain/Display/WindowRelocation.cs     reposicionar janela entre monitores (puro)
host/Host.Domain/Display/VddSettingsXml.cs       garantir resolução no vdd_settings.xml (puro)
host/Host.Transport/KeyframeRequestListener.cs   UDP 9101 → callback
host/Host.Capture/WgcMonitorCapture.cs           + ForMonitor(hmonitor), ForWindow(hwnd)
host/Host.Encode/IVideoEncoder.cs                IDR periódico 5 s
host/Host.App/Display/Win32Displays.cs           snapshot de displays, modo, HMONITOR
host/Host.App/Display/WindowOps.cs               janelas: listar por monitor, mover, redimensionar
host/Host.App/Display/IVirtualDisplayDriver.cs   contrato + VirtualMonitor
host/Host.App/Display/SudoVdaDriver.cs           IOCTL + watchdog
host/Host.App/Display/VddDriver.cs               XML + pipe + modo
host/Host.App/Display/DisplaySpike.cs            os 6 testes + relatório
host/Host.App/Program.cs                         comandos display-spike e --mix; KFRQ no stream/serve
host/Host.App/Control/StreamRegistry.cs          RequestKeyframe(address, streamId)
host/Host.Tests/Spike/*.cs                       testes dos puros
headset-native/.../packet.h · stream_session.*   KFRQ + espera de 16 ms
protocol/video-packet-v1.md                      seção KFRQ
tools/phase0-streams-test.ps1                    -Mix, -Runs, avaliação do critério
docs/measurements/fase2a-*.md · docs/adr/0007-*.md · docs/adr/0008-*.md
```

---

## Parte A — Driver de monitor virtual

### Task 1: Puros — relocação de janela e XML do VDD

**Files:**
- Create: `host/Host.Domain/Display/WindowRelocation.cs`, `host/Host.Domain/Display/VddSettingsXml.cs`
- Test: `host/Host.Tests/Spike/DisplayPureTests.cs`

**Interfaces:**
- Produces: `record struct RectI(int X, int Y, int Width, int Height)`; `WindowRelocation.Map(RectI window, RectI from, RectI to) → RectI` (mantém a posição relativa do centro e o tamanho, limitado ao monitor de destino); `VddSettingsXml.EnsureResolution(string xml, int width, int height, int refreshRate) → string` (idempotente); `VddSettingsXml.ReadMonitorCount(string xml) → int`.

- [ ] **Step 1: Branch e testes que falham**

```powershell
git switch -c phase2a/spikes
```

`host/Host.Tests/Spike/DisplayPureTests.cs`:
```csharp
using ScreenPortal.Host.Domain.Display;

namespace ScreenPortal.Host.Tests.Spike;

public class DisplayPureTests
{
    [Fact]
    public void Map_KeepsRelativeCenterAndSize()
    {
        var from = new RectI(2560, 0, 1920, 1080);
        var to = new RectI(4480, 0, 2560, 1440);
        var window = new RectI(2560 + 480, 270, 960, 540); // centrada em (50%, 50%)

        var mapped = WindowRelocation.Map(window, from, to);

        Assert.Equal(960, mapped.Width);
        Assert.Equal(540, mapped.Height);
        Assert.Equal(4480 + 1280 - 480, mapped.X);
        Assert.Equal(720 - 270, mapped.Y);
    }

    [Fact]
    public void Map_ShrinksAndClampsWhenDestinationIsSmaller()
    {
        var from = new RectI(0, 0, 3840, 2160);
        var to = new RectI(3840, 0, 1920, 1080);
        var window = new RectI(0, 0, 3000, 2000);

        var mapped = WindowRelocation.Map(window, from, to);

        Assert.True(mapped.Width <= 1920 && mapped.Height <= 1080);
        Assert.True(mapped.X >= 3840 && mapped.X + mapped.Width <= 3840 + 1920);
        Assert.True(mapped.Y >= 0 && mapped.Y + mapped.Height <= 1080);
    }

    private const string SampleXml = """
        <?xml version='1.0' encoding='utf-8'?>
        <vdd_settings>
            <monitors><count>2</count></monitors>
            <resolutions>
                <resolution><width>1920</width><height>1080</height><refresh_rate>60</refresh_rate></resolution>
            </resolutions>
        </vdd_settings>
        """;

    [Fact]
    public void EnsureResolution_AddsOnce()
    {
        string once = VddSettingsXml.EnsureResolution(SampleXml, 1440, 2560, 60);
        string twice = VddSettingsXml.EnsureResolution(once, 1440, 2560, 60);

        Assert.Contains("<width>1440</width>", once);
        Assert.Contains("<height>2560</height>", once);
        Assert.Equal(once, twice);
        Assert.Equal(2, VddSettingsXml.ReadMonitorCount(once));
    }

    [Fact]
    public void EnsureResolution_KeepsExistingEntry() =>
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(
            VddSettingsXml.EnsureResolution(SampleXml, 1920, 1080, 60), "<resolution>").Count);
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test host/Host.Tests -v q`
Expected: FAIL de compilação — `The type or namespace name 'Display' does not exist in the namespace 'ScreenPortal.Host.Domain'`.

- [ ] **Step 3: Implementar**

`host/Host.Domain/Display/WindowRelocation.cs`:
```csharp
namespace ScreenPortal.Host.Domain.Display;

public readonly record struct RectI(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>
/// "Recriar com outra resolução" (brief §10.4): leva uma janela para o monitor novo mantendo a posição relativa do
/// centro e o tamanho — reduzindo e encaixando se o destino for menor.
/// </summary>
public static class WindowRelocation
{
    public static RectI Map(RectI window, RectI from, RectI to)
    {
        int width = Math.Min(window.Width, to.Width);
        int height = Math.Min(window.Height, to.Height);

        double cx = (window.X + window.Width / 2.0 - from.X) / from.Width;
        double cy = (window.Y + window.Height / 2.0 - from.Y) / from.Height;

        int x = (int)Math.Round(to.X + cx * to.Width - width / 2.0);
        int y = (int)Math.Round(to.Y + cy * to.Height - height / 2.0);
        x = Math.Clamp(x, to.X, to.Right - width);
        y = Math.Clamp(y, to.Y, to.Bottom - height);
        return new RectI(x, y, width, height);
    }
}
```

`host/Host.Domain/Display/VddSettingsXml.cs`:
```csharp
using System.Xml.Linq;

namespace ScreenPortal.Host.Domain.Display;

/// <summary>Edita o vdd_settings.xml do Virtual Display Driver (a lista de resoluções é global a todos os monitores).</summary>
public static class VddSettingsXml
{
    public static string EnsureResolution(string xml, int width, int height, int refreshRate)
    {
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var resolutions = doc.Root!.Element("resolutions") ?? AddElement(doc.Root!, "resolutions");
        bool exists = resolutions.Elements("resolution").Any(r =>
            (int?)r.Element("width") == width && (int?)r.Element("height") == height && (int?)r.Element("refresh_rate") == refreshRate);
        if (exists) return xml;

        resolutions.Add(new XElement("resolution",
            new XElement("width", width), new XElement("height", height), new XElement("refresh_rate", refreshRate)));
        return doc.Declaration + doc.ToString(SaveOptions.DisableFormatting);
    }

    public static int ReadMonitorCount(string xml) =>
        (int?)XDocument.Parse(xml).Root!.Element("monitors")?.Element("count") ?? 1;

    private static XElement AddElement(XElement parent, string name)
    {
        var element = new XElement(name);
        parent.Add(element);
        return element;
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test host/Host.Tests -v q`
Expected: `Com falha: 0`.

- [ ] **Step 5: Commit**

```bash
git add host
git commit -m "phase2a: window relocation and VDD settings XML (pure, tested)"
```

---

### Task 2: Win32 — displays, janelas e captura por monitor/janela

**Files:**
- Create: `host/Host.App/Display/Win32Displays.cs`, `host/Host.App/Display/WindowOps.cs`
- Modify: `host/Host.Capture/WgcMonitorCapture.cs` (adicionar `ForMonitor(GpuDevice, IntPtr hmonitor, bool)` e `ForWindow(GpuDevice, IntPtr hwnd, bool)`; `ForPrimaryMonitor` passa a chamar `ForMonitor`)

**Interfaces:**
- Consumes: `RectI` (Task 1).
- Produces: `record DisplayInfo(string DeviceName, string AdapterName, RectI Bounds, bool IsPrimary, IntPtr HMonitor)`; `Win32Displays.Snapshot() → IReadOnlyList<DisplayInfo>`; `Win32Displays.SetMode(string deviceName, int width, int height, int refresh) → bool`; `WindowOps.StartTestWindow() → (Process process, IntPtr hwnd)`; `WindowOps.GetRect(IntPtr hwnd) → RectI`; `WindowOps.Move(IntPtr hwnd, RectI rect)`; `WindowOps.WindowsOn(IntPtr hmonitor) → IReadOnlyList<IntPtr>`; `WindowOps.MonitorOf(IntPtr hwnd) → IntPtr`; `WgcMonitorCapture.ForMonitor(...)`, `WgcMonitorCapture.ForWindow(...)`.

- [ ] **Step 1: Displays**

`host/Host.App/Display/Win32Displays.cs`:
```csharp
using System.Runtime.InteropServices;
using ScreenPortal.Host.Domain.Display;

namespace ScreenPortal.Host.App.Display;

public sealed record DisplayInfo(string DeviceName, string AdapterName, RectI Bounds, bool IsPrimary, IntPtr HMonitor);

/// <summary>Monitores ativos no desktop (pixels físicos; o processo é Per-Monitor DPI Aware v2).</summary>
public static class Win32Displays
{
    public static IReadOnlyList<DisplayInfo> Snapshot()
    {
        var monitors = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hmon, IntPtr hdc, ref Rect r, IntPtr data) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(hmon, ref info)) monitors[info.DeviceName] = hmon;
            return true;
        }, IntPtr.Zero);

        var result = new List<DisplayInfo>();
        var device = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
        for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++, device.cb = Marshal.SizeOf<DisplayDevice>())
        {
            if ((device.StateFlags & AttachedToDesktop) == 0) continue;
            var mode = NewDevMode();
            if (!EnumDisplaySettings(device.DeviceName, EnumCurrentSettings, ref mode)) continue;
            monitors.TryGetValue(device.DeviceName, out var hmon);
            result.Add(new DisplayInfo(device.DeviceName, device.DeviceString,
                new RectI(mode.dmPositionX, mode.dmPositionY, (int)mode.dmPelsWidth, (int)mode.dmPelsHeight),
                (device.StateFlags & PrimaryDevice) != 0, hmon));
        }
        return result;
    }

    /// <summary>Troca o modo de um monitor (usado só pelo VDD, que não cria monitor com resolução definida).</summary>
    public static bool SetMode(string deviceName, int width, int height, int refresh)
    {
        var mode = NewDevMode();
        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode)) return false;
        mode.dmPelsWidth = (uint)width;
        mode.dmPelsHeight = (uint)height;
        mode.dmDisplayFrequency = (uint)refresh;
        mode.dmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency;
        return ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero) == 0;
    }

    private static DevMode NewDevMode() => new() { dmSize = (ushort)Marshal.SizeOf<DevMode>() };

    private const int EnumCurrentSettings = -1;
    private const uint AttachedToDesktop = 0x1, PrimaryDevice = 0x4;
    private const uint DmPelsWidth = 0x80000, DmPelsHeight = 0x100000, DmDisplayFrequency = 0x400000;
    private const uint CdsUpdateRegistry = 0x1;

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size; public Rect Monitor; public Rect Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    private delegate bool MonitorEnumProc(IntPtr hmon, IntPtr hdc, ref Rect rect, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hmon, ref MonitorInfoEx info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice dd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string device, int mode, ref DevMode dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string device, ref DevMode dm, IntPtr hwnd, uint flags, IntPtr param);
}
```

- [ ] **Step 2: Janelas**

`host/Host.App/Display/WindowOps.cs`:
```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using ScreenPortal.Host.Domain.Display;

namespace ScreenPortal.Host.App.Display;

/// <summary>Operações de janela para os testes de recriar e de monitor-palco.</summary>
public static class WindowOps
{
    /// <summary>Abre o Bloco de Notas como janela de teste e devolve o HWND.</summary>
    public static (Process Process, IntPtr Hwnd) StartTestWindow()
    {
        var process = Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true })!;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000)
        {
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero) return (process, process.MainWindowHandle);
            Thread.Sleep(100);
        }
        throw new InvalidOperationException("Janela de teste não apareceu.");
    }

    public static RectI GetRect(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var r);
        return new RectI(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    public static void Move(IntPtr hwnd, RectI rect) =>
        SetWindowPos(hwnd, IntPtr.Zero, rect.X, rect.Y, rect.Width, rect.Height, SwpNoZOrder | SwpNoActivate);

    public static IntPtr MonitorOf(IntPtr hwnd) => MonitorFromWindow(hwnd, MonitorDefaultToNull);

    public static IReadOnlyList<IntPtr> WindowsOn(IntPtr hmonitor)
    {
        var result = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && MonitorFromWindow(hwnd, MonitorDefaultToNull) == hmonitor) result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private const uint SwpNoZOrder = 0x4, SwpNoActivate = 0x10, MonitorDefaultToNull = 0;

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
}
```

- [ ] **Step 3: Captura WGC por monitor e por janela**

Em `host/Host.Capture/WgcMonitorCapture.cs`, substituir o método `ForPrimaryMonitor` inteiro por:
```csharp
    public static WgcMonitorCapture ForPrimaryMonitor(GpuDevice gpu, bool captureCursor) =>
        ForMonitor(gpu, MonitorFromPoint(default, MONITOR_DEFAULTTOPRIMARY), captureCursor);

    public static WgcMonitorCapture ForMonitor(GpuDevice gpu, IntPtr hmonitor, bool captureCursor) =>
        new WgcMonitorCapture(gpu, CreateItem(hmonitor, forWindow: false), captureCursor);

    /// <summary>Captura só a janela (monitor-palco com recorte, brief §10.5).</summary>
    public static WgcMonitorCapture ForWindow(GpuDevice gpu, IntPtr hwnd, bool captureCursor) =>
        new WgcMonitorCapture(gpu, CreateItem(hwnd, forWindow: true), captureCursor);

    private static GraphicsCaptureItem CreateItem(IntPtr handle, bool forWindow)
    {
        if (!GraphicsCaptureSession.IsSupported())
            throw new PlatformNotSupportedException("Windows Graphics Capture não é suportado neste sistema.");

        var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var iidInterop = IID_IGraphicsCaptureItemInterop;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(factory.ThisPtr, in iidInterop, out IntPtr interop));
        IntPtr itemPtr;
        try
        {
            var iidItem = IID_IGraphicsCaptureItem;
            // IGraphicsCaptureItemInterop: slot 3 = CreateForWindow, slot 4 = CreateForMonitor.
            var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)(*(*(void***)interop + (forWindow ? 3 : 4)));
            Marshal.ThrowExceptionForHR(create(interop, handle, &iidItem, &itemPtr));
        }
        finally { Marshal.Release(interop); }

        try { return GraphicsCaptureItem.FromAbi(itemPtr); }
        finally { Marshal.Release(itemPtr); }
    }

    /// <summary>Tamanho atual do conteúdo (janela redimensionada muda o tamanho do frame).</summary>
    public Windows.Graphics.SizeInt32 LastFrameSize => _poolSize;
```

- [ ] **Step 4: Build**

Run: `dotnet build host/Host.sln -v q`
Expected: `Compilação com êxito`.

- [ ] **Step 5: Commit**

```bash
git add host
git commit -m "phase2a: Win32 display snapshot/mode, window ops, WGC capture by monitor and by window"
```

---

### Task 3: Drivers — SudoVDA (IOCTL) e VDD (XML + pipe)

**Files:**
- Create: `host/Host.App/Display/IVirtualDisplayDriver.cs`, `SudoVdaDriver.cs`, `VddDriver.cs`

**Interfaces:**
- Consumes: `Win32Displays`, `DisplayInfo` (Task 2), `VddSettingsXml` (Task 1).
- Produces: `record VirtualMonitor(string Id, DisplayInfo Display)`; `interface IVirtualDisplayDriver : IDisposable { string Name { get; } bool CanRemoveSpecific { get; } VirtualMonitor Create(int width, int height, int refresh); void Remove(VirtualMonitor monitor); }`; `SudoVdaDriver.Open(Action<string> log)`; `VddDriver.Open(Action<string> log)`.

- [ ] **Step 1: Contrato e utilitário de espera**

`host/Host.App/Display/IVirtualDisplayDriver.cs`:
```csharp
using System.Diagnostics;

namespace ScreenPortal.Host.App.Display;

public sealed record VirtualMonitor(string Id, DisplayInfo Display);

/// <summary>Driver de monitor virtual (spike 2A). Create devolve o monitor já ativo no desktop.</summary>
public interface IVirtualDisplayDriver : IDisposable
{
    string Name { get; }
    /// <summary>false no VDD: ele só ajusta a quantidade; remove sempre o último.</summary>
    bool CanRemoveSpecific { get; }
    VirtualMonitor Create(int width, int height, int refresh);
    void Remove(VirtualMonitor monitor);
}

internal static class DisplayWait
{
    /// <summary>Espera um display novo (que não estava em <paramref name="before"/>) aparecer.</summary>
    public static DisplayInfo ForNewDisplay(IReadOnlyList<DisplayInfo> before, TimeSpan timeout)
    {
        var known = before.Select(d => d.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var added = Win32Displays.Snapshot().FirstOrDefault(d => !known.Contains(d.DeviceName));
            if (added != null) return added;
            Thread.Sleep(100);
        }
        throw new TimeoutException("O monitor virtual não apareceu no desktop.");
    }

    public static void ForGone(string deviceName, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (Win32Displays.Snapshot().All(d => !d.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))) return;
            Thread.Sleep(100);
        }
        throw new TimeoutException("O monitor virtual não saiu do desktop.");
    }
}
```

- [ ] **Step 2: SudoVDA**

`host/Host.App/Display/SudoVdaDriver.cs`:
```csharp
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ScreenPortal.Host.App.Display;

/// <summary>
/// SudoVDA (SudoMaker, usado pelo Apollo): cria cada monitor com a resolução exata por IOCTL.
/// Watchdog: sem ping periódico o driver remove os monitores — uma thread pinga enquanto o driver estiver aberto.
/// Protocolo: Common/Include/sudovda-ioctl.h (github.com/SudoMaker/SudoVDA).
/// </summary>
public sealed class SudoVdaDriver : IVirtualDisplayDriver
{
    private static readonly Guid InterfaceGuid = new("e5bcc234-1e0c-418a-a0d4-ef8b7501414d");
    private const uint IoctlAdd = 0x222000, IoctlRemove = 0x222004, IoctlGetWatchdog = 0x22200C, IoctlPing = 0x222220, IoctlVersion = 0x2223FC;

    private readonly SafeFileHandle _device;
    private readonly Action<string> _log;
    private readonly Thread _pinger;
    private volatile bool _disposed;

    public string Name => "SudoVDA";
    public bool CanRemoveSpecific => true;

    private SudoVdaDriver(SafeFileHandle device, Action<string> log)
    {
        _device = device;
        _log = log;
        var version = new byte[4];
        if (Control(IoctlVersion, null, version))
            _log($"SudoVDA protocolo {version[0]}.{version[1]}.{version[2]}{(version[3] != 0 ? " (test build)" : "")}");
        var watchdog = new byte[8];
        if (Control(IoctlGetWatchdog, null, watchdog))
            _log($"SudoVDA watchdog: timeout {BitConverter.ToUInt32(watchdog, 0)} s");
        _pinger = new Thread(PingLoop) { IsBackground = true, Name = "sudovda-ping" };
        _pinger.Start();
    }

    public static SudoVdaDriver Open(Action<string> log) => new(OpenDevice(), log);

    public VirtualMonitor Create(int width, int height, int refresh)
    {
        var before = Win32Displays.Snapshot();
        var guid = Guid.NewGuid();
        var input = new byte[56];
        BitConverter.TryWriteBytes(input.AsSpan(0), (uint)width);
        BitConverter.TryWriteBytes(input.AsSpan(4), (uint)height);
        BitConverter.TryWriteBytes(input.AsSpan(8), (uint)refresh);
        guid.TryWriteBytes(input.AsSpan(12));
        System.Text.Encoding.ASCII.GetBytes("ScreenPortal").CopyTo(input, 28);          // DeviceName[14]
        System.Text.Encoding.ASCII.GetBytes(guid.ToString("N")[..13]).CopyTo(input, 42); // SerialNumber[14]
        var output = new byte[12];
        if (!Control(IoctlAdd, input, output))
            throw new InvalidOperationException($"IOCTL_ADD_VIRTUAL_DISPLAY falhou ({Marshal.GetLastWin32Error()}).");
        var display = DisplayWait.ForNewDisplay(before, TimeSpan.FromSeconds(10));
        return new VirtualMonitor(guid.ToString(), display);
    }

    public void Remove(VirtualMonitor monitor)
    {
        var input = new byte[16];
        Guid.Parse(monitor.Id).TryWriteBytes(input);
        if (!Control(IoctlRemove, input, null))
            throw new InvalidOperationException($"IOCTL_REMOVE_VIRTUAL_DISPLAY falhou ({Marshal.GetLastWin32Error()}).");
        DisplayWait.ForGone(monitor.Display.DeviceName, TimeSpan.FromSeconds(10));
    }

    private void PingLoop()
    {
        while (!_disposed)
        {
            Control(IoctlPing, null, null);
            Thread.Sleep(1000);
        }
    }

    private bool Control(uint code, byte[]? input, byte[]? output) =>
        DeviceIoControl(_device, code, input, input?.Length ?? 0, output, output?.Length ?? 0, out _, IntPtr.Zero);

    public void Dispose()
    {
        _disposed = true;
        _device.Dispose();
    }

    private static SafeFileHandle OpenDevice()
    {
        var guid = InterfaceGuid;
        IntPtr set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == new IntPtr(-1)) throw new InvalidOperationException("SetupDiGetClassDevs falhou.");
        try
        {
            var data = new SpDeviceInterfaceData { cbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
            if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, 0, ref data))
                throw new InvalidOperationException("SudoVDA não encontrado (driver não instalado?).");
            SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int required, IntPtr.Zero);
            IntPtr detail = Marshal.AllocHGlobal(required);
            try
            {
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6); // cbSize de SP_DEVICE_INTERFACE_DETAIL_DATA_W
                if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required, out _, IntPtr.Zero))
                    throw new InvalidOperationException("SetupDiGetDeviceInterfaceDetail falhou.");
                string path = Marshal.PtrToStringUni(detail + 4)!;
                var handle = CreateFile(path, GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (handle.IsInvalid) throw new InvalidOperationException($"Não abriu o SudoVDA ({Marshal.GetLastWin32Error()}).");
                return handle;
            }
            finally { Marshal.FreeHGlobal(detail); }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    private const uint DigcfPresent = 0x2, DigcfDeviceInterface = 0x10;
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, FileShareRead = 1, FileShareWrite = 2, OpenExisting = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData { public int cbSize; public Guid InterfaceClassGuid; public uint Flags; public IntPtr Reserved; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string? enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, uint index, ref SpDeviceInterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData data, IntPtr detail, int size, out int required, IntPtr devInfo);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? input, int inputSize, byte[]? output, int outputSize, out int returned, IntPtr overlapped);
}
```

- [ ] **Step 3: VDD**

`host/Host.App/Display/VddDriver.cs`:
```csharp
using System.IO.Pipes;
using System.Text;
using Microsoft.Win32;
using ScreenPortal.Host.Domain.Display;

namespace ScreenPortal.Host.App.Display;

/// <summary>
/// Virtual Display Driver (VirtualDrivers, MIT, decisão D3). Não tem "criar monitor X×Y": a quantidade vem de
/// SETDISPLAYCOUNT e a lista de resoluções é global (vdd_settings.xml). Resolução exata = garantir a entrada no XML,
/// recarregar, e aplicar o modo pelo Windows (ChangeDisplaySettingsEx). Remove sempre o último monitor.
/// </summary>
public sealed class VddDriver : IVirtualDisplayDriver
{
    private const string PipeName = "MTTVirtualDisplayPipe";
    private readonly Action<string> _log;
    private readonly string _settingsPath;
    private readonly List<VirtualMonitor> _created = new();

    public string Name => "VDD";
    public bool CanRemoveSpecific => false;

    private VddDriver(string settingsPath, Action<string> log)
    {
        _settingsPath = settingsPath;
        _log = log;
        _log($"VDD settings: {settingsPath}; resposta ao PING: '{Send("PING")}'");
    }

    public static VddDriver Open(Action<string> log) => new(FindSettingsPath(), log);

    public VirtualMonitor Create(int width, int height, int refresh)
    {
        var before = Win32Displays.Snapshot();
        string xml = File.ReadAllText(_settingsPath);
        string updated = VddSettingsXml.EnsureResolution(xml, width, height, refresh);
        if (updated != xml) File.WriteAllText(_settingsPath, updated);

        int count = VddSettingsXml.ReadMonitorCount(updated);
        int active = CountVddDisplays(before);
        _log($"VDD: {active} ativo(s), count no XML {count} → SETDISPLAYCOUNT {active + 1}");
        Send($"SETDISPLAYCOUNT {active + 1}");
        Send("RELOAD_DRIVER");

        var display = DisplayWait.ForNewDisplay(before, TimeSpan.FromSeconds(20));
        if (display.Bounds.Width != width || display.Bounds.Height != height)
        {
            if (!Win32Displays.SetMode(display.DeviceName, width, height, refresh))
                throw new InvalidOperationException($"ChangeDisplaySettingsEx falhou para {width}x{height}.");
            display = Win32Displays.Snapshot().First(d => d.DeviceName == display.DeviceName);
        }
        var monitor = new VirtualMonitor(display.DeviceName, display);
        _created.Add(monitor);
        return monitor;
    }

    public void Remove(VirtualMonitor monitor)
    {
        if (_created.Count == 0 || _created[^1].Id != monitor.Id)
            throw new NotSupportedException("VDD só remove o último monitor criado (SETDISPLAYCOUNT).");
        int active = CountVddDisplays(Win32Displays.Snapshot());
        Send($"SETDISPLAYCOUNT {Math.Max(0, active - 1)}");
        Send("RELOAD_DRIVER");
        DisplayWait.ForGone(monitor.Display.DeviceName, TimeSpan.FromSeconds(20));
        _created.RemoveAt(_created.Count - 1);
    }

    private static int CountVddDisplays(IReadOnlyList<DisplayInfo> displays) =>
        displays.Count(d => d.AdapterName.Contains("Virtual Display Driver", StringComparison.OrdinalIgnoreCase)
                            || d.AdapterName.Contains("VDD", StringComparison.OrdinalIgnoreCase));

    private string Send(string command)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
        pipe.Connect(3000);
        var bytes = Encoding.Unicode.GetBytes(command);
        pipe.Write(bytes, 0, bytes.Length);
        pipe.Flush();
        var buffer = new byte[4096];
        var sb = new StringBuilder();
        try
        {
            pipe.ReadTimeout = 2000;
            int read;
            while ((read = pipe.Read(buffer, 0, buffer.Length)) > 0) sb.Append(Encoding.Unicode.GetString(buffer, 0, read));
        }
        catch (IOException) { }
        catch (InvalidOperationException) { }
        return sb.ToString().Trim('\0', ' ', '\r', '\n');
    }

    /// <summary>O caminho do XML vem do registro do driver (HKLM\SOFTWARE\MikeTheTech\VirtualDisplayDriver); fallback C:\VirtualDisplayDriver.</summary>
    private static string FindSettingsPath()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\MikeTheTech\VirtualDisplayDriver");
        if (key != null)
        {
            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is not string value) continue;
                string candidate = value.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? value : Path.Combine(value, "vdd_settings.xml");
                if (File.Exists(candidate)) return candidate;
            }
        }
        const string fallback = @"C:\VirtualDisplayDriver\vdd_settings.xml";
        if (File.Exists(fallback)) return fallback;
        throw new InvalidOperationException("vdd_settings.xml não encontrado (VDD instalado?).");
    }

    public void Dispose()
    {
        for (int i = _created.Count - 1; i >= 0; i--)
        {
            try { Remove(_created[i]); } catch (Exception e) { _log($"VDD: falha ao remover na saída: {e.Message}"); }
        }
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build host/Host.sln -v q`
Expected: `Compilação com êxito`.

- [ ] **Step 5: Commit**

```bash
git add host
git commit -m "phase2a: virtual display drivers — SudoVDA (IOCTL + watchdog ping) and VDD (settings XML + pipe + mode)"
```

---

### Task 4: `display-spike` — os 6 testes e o relatório

**Files:**
- Create: `host/Host.App/Display/DisplaySpike.cs`
- Modify: `host/Host.App/Program.cs` (comando `display-spike`)

**Interfaces:**
- Consumes: tudo das Tasks 1–3; `GpuDevice`, `WgcMonitorCapture.ForMonitor/ForWindow`, `StreamPipeline`, `MfH264Encoder`, `EncoderSettings`, `IEncodedFrameSink`.
- Produces: `DisplaySpike.Run(IVirtualDisplayDriver driver, GpuDevice gpu, Action<string> log) → string` (markdown); comando `ScreenPortalHost display-spike --driver sudovda|vdd [--out <arquivo>]`.

- [ ] **Step 1: Implementar**

`host/Host.App/Display/DisplaySpike.cs`:
```csharp
using System.Diagnostics;
using System.Net;
using System.Text;
using ScreenPortal.Host.Capture;
using ScreenPortal.Host.Domain.Display;
using ScreenPortal.Host.Encode;
using Vortice.Direct3D11;

namespace ScreenPortal.Host.App.Display;

/// <summary>Os 6 testes do spike do driver (spec 2A §2). Cada teste é isolado: falha em um não impede os outros.</summary>
public static class DisplaySpike
{
    private sealed record Row(string Test, string Result, string Details);

    public static string Run(IVirtualDisplayDriver driver, GpuDevice gpu, Action<string> log)
    {
        var rows = new List<Row>
        {
            Guard("1. Resolução exata", () => ExactResolutions(driver, log)),
            Guard("2. Monitores simultâneos", () => Simultaneous(driver, log)),
            Guard("3. Retrato nativo 1440×2560", () => Portrait(driver, gpu, log)),
            Guard("4. Recriar movendo janelas", () => Recreate(driver, log)),
            Guard("5. Monitor-palco com recorte", () => StageCrop(driver, gpu, log)),
            Guard("6. Limite do H.264 da GPU", () => EncoderLimits(gpu, log)),
        };

        var md = new StringBuilder();
        md.AppendLine($"# Fase 2A — spike do driver: {driver.Name}");
        md.AppendLine();
        md.AppendLine($"Data: {DateTime.Now:dd/MM/yyyy HH:mm} · GPU: {gpu.AdapterName} · Windows {Environment.OSVersion.Version}");
        md.AppendLine();
        md.AppendLine("| Teste | Resultado | Detalhes |");
        md.AppendLine("|---|---|---|");
        foreach (var r in rows) md.AppendLine($"| {r.Test} | {r.Result} | {r.Details.Replace("|", "/")} |");
        return md.ToString();
    }

    private static Row Guard(string name, Func<(bool ok, string details)> test)
    {
        try
        {
            var (ok, details) = test();
            return new Row(name, ok ? "✅" : "❌", details);
        }
        catch (NotSupportedException e) { return new Row(name, "N/A", e.Message); }
        catch (Exception e) { return new Row(name, "❌", $"{e.GetType().Name}: {e.Message}"); }
    }

    private static (bool, string) ExactResolutions(IVirtualDisplayDriver driver, Action<string> log)
    {
        var details = new List<string>();
        bool all = true;
        foreach (var (w, h) in new[] { (2560, 1440), (3440, 1440), (1920, 1080) })
        {
            var sw = Stopwatch.StartNew();
            var monitor = driver.Create(w, h, 60);
            long createMs = sw.ElapsedMilliseconds;
            var b = monitor.Display.Bounds;
            bool ok = b.Width == w && b.Height == h;
            all &= ok;
            sw.Restart();
            driver.Remove(monitor);
            details.Add($"{w}×{h}: {(ok ? "exato" : $"veio {b.Width}×{b.Height}")}, cria {createMs} ms, remove {sw.ElapsedMilliseconds} ms");
            log(details[^1]);
        }
        return (all, string.Join("; ", details));
    }

    private static (bool, string) Simultaneous(IVirtualDisplayDriver driver, Action<string> log)
    {
        var created = new List<VirtualMonitor>();
        string stop = "chegou a 8";
        try
        {
            for (int i = 0; i < 8; i++)
            {
                try { created.Add(driver.Create(1920, 1080, 60)); }
                catch (Exception e) { stop = $"falhou no {i + 1}º: {e.Message}"; break; }
                log($"simultâneos: {created.Count}");
            }
        }
        finally
        {
            for (int i = created.Count - 1; i >= 0; i--) driver.Remove(created[i]);
        }
        return (created.Count >= 4, $"{created.Count} simultâneos ({stop})");
    }

    private static (bool, string) Portrait(IVirtualDisplayDriver driver, GpuDevice gpu, Action<string> log)
    {
        var monitor = driver.Create(1440, 2560, 60);
        try
        {
            var b = monitor.Display.Bounds;
            if (b.Width != 1440 || b.Height != 2560) return (false, $"modo veio {b.Width}×{b.Height}");
            using var capture = WgcMonitorCapture.ForMonitor(gpu, monitor.Display.HMonitor, captureCursor: false);
            string dump = Path.Combine(Path.GetTempPath(), "sp-portrait.h264");
            using (var pipeline = new StreamPipeline(gpu, capture, new EncoderSettings(1440, 2560, 30, 8000),
                       new IPEndPoint(IPAddress.Loopback, 9050), 1, dump))
            {
                pipeline.Start();
                Thread.Sleep(3000);
            }
            string probe = Ffprobe(dump);
            log($"retrato: ffprobe {probe}");
            return (probe.Contains("width=1440") && probe.Contains("height=2560"), $"captura {capture.Width}×{capture.Height}; stream {probe}");
        }
        finally { driver.Remove(monitor); }
    }

    private static (bool, string) Recreate(IVirtualDisplayDriver driver, Action<string> log)
    {
        if (!driver.CanRemoveSpecific)
            throw new NotSupportedException("o driver só remove o último monitor; recriar exigiria troca de modo ao vivo (evitada pelo brief §10.1)");

        var (process, hwnd) = WindowOps.StartTestWindow();
        var a = driver.Create(1920, 1080, 60);
        try
        {
            var start = new RectI(a.Display.Bounds.X + 480, a.Display.Bounds.Y + 270, 960, 540);
            WindowOps.Move(hwnd, start);
            Thread.Sleep(300);

            var b = driver.Create(2560, 1440, 60);
            foreach (var w in WindowOps.WindowsOn(a.Display.HMonitor))
                WindowOps.Move(w, WindowRelocation.Map(WindowOps.GetRect(w), a.Display.Bounds, b.Display.Bounds));
            driver.Remove(a);
            Thread.Sleep(500);

            var expected = WindowRelocation.Map(start, a.Display.Bounds, b.Display.Bounds);
            var actual = WindowOps.GetRect(hwnd);
            bool onB = WindowOps.MonitorOf(hwnd) == b.Display.HMonitor;
            bool near = Math.Abs(actual.X - expected.X) <= 8 && Math.Abs(actual.Y - expected.Y) <= 8;
            driver.Remove(b);
            log($"recriar: no monitor novo={onB}, esperado {expected}, real {actual}");
            return (onB && near, $"janela no monitor novo: {onB}; posição esperada {expected.X},{expected.Y}, real {actual.X},{actual.Y}");
        }
        finally
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
        }
    }

    private static (bool, string) StageCrop(IVirtualDisplayDriver driver, GpuDevice gpu, Action<string> log)
    {
        var (process, hwnd) = WindowOps.StartTestWindow();
        var stage = driver.Create(3840, 2160, 60);
        try
        {
            WindowOps.Move(hwnd, new RectI(stage.Display.Bounds.X + 100, stage.Display.Bounds.Y + 100, 800, 600));
            Thread.Sleep(300);
            using var capture = WgcMonitorCapture.ForWindow(gpu, hwnd, captureCursor: false);
            var sink = new SizeProbe();
            capture.Start(sink);
            var first = sink.WaitSize(TimeSpan.FromSeconds(3));
            WindowOps.Move(hwnd, new RectI(stage.Display.Bounds.X + 100, stage.Display.Bounds.Y + 100, 1200, 900));
            var second = sink.WaitSizeDifferentFrom(first, TimeSpan.FromSeconds(3));
            log($"palco: {first} → {second}");
            bool ok = second.w > first.w && second.h > first.h;
            return (ok, $"recorte da janela {first.w}×{first.h} → {second.w}×{second.h} após redimensionar");
        }
        finally
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
            driver.Remove(stage);
        }
    }

    private static (bool, string) EncoderLimits(GpuDevice gpu, Action<string> log)
    {
        var results = new List<string>();
        foreach (var (w, h) in new[] { (4096, 1152), (5120, 1440), (7680, 2160), (1440, 5120) })
        {
            try
            {
                using var encoder = new MfH264Encoder(gpu.Device, new EncoderSettings(w, h, 30, 20_000), new NullSink());
                results.Add($"{w}×{h} ok");
            }
            catch (Exception e) { results.Add($"{w}×{h} FALHOU ({e.GetType().Name})"); }
            log(results[^1]);
        }
        return (results[0].EndsWith("ok"), string.Join("; ", results));
    }

    private static string Ffprobe(string file)
    {
        var psi = new ProcessStartInfo("ffprobe", $"-v error -select_streams v:0 -show_entries stream=width,height,codec_name -of compact \"{file}\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit(10_000);
        return output;
    }

    private sealed class NullSink : IEncodedFrameSink
    {
        public void OnEncodedFrame(ReadOnlySpan<byte> accessUnit, bool keyframe, ulong captureTimeUs) { }
    }

    private sealed class SizeProbe : ICaptureFrameSink
    {
        private volatile int _w, _h;
        public void OnFrame(ID3D11Texture2D bgraTexture, int width, int height, ulong captureTimeUs) { _w = width; _h = height; }

        public (int w, int h) WaitSize(TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            while (_w == 0 && sw.Elapsed < timeout) Thread.Sleep(20);
            return (_w, _h);
        }

        public (int w, int h) WaitSizeDifferentFrom((int w, int h) old, TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            while ((_w, _h) == old && sw.Elapsed < timeout) Thread.Sleep(20);
            return (_w, _h);
        }
    }
}
```

Em `host/Host.App/Program.cs`, no `switch (opts.Command)` acrescentar antes de `default:`:
```csharp
    case "display-spike": return RunDisplaySpike(opts);
```
e antes de `static int RunStream`:
```csharp
static int RunDisplaySpike(Args o)
{
    static void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
    string driverName = o.Str("driver", "sudovda").ToLowerInvariant();
    using var gpu = new GpuDevice();
    using ScreenPortal.Host.App.Display.IVirtualDisplayDriver driver = driverName == "vdd"
        ? ScreenPortal.Host.App.Display.VddDriver.Open(Log)
        : ScreenPortal.Host.App.Display.SudoVdaDriver.Open(Log);
    string report = ScreenPortal.Host.App.Display.DisplaySpike.Run(driver, gpu, Log);
    string outPath = o.Str("out", Path.Combine("docs", "measurements", $"fase2a-driver-{driverName}.md"));
    File.WriteAllText(outPath, report);
    Console.WriteLine(report);
    Log($"relatório: {outPath}");
    return 0;
}
```

- [ ] **Step 2: Build e rodar com o SudoVDA (já instalado)**

Run:
```powershell
dotnet build host/Host.sln -c Release -v q
host/Host.App/bin/Release/net10.0-windows10.0.22621.0/ScreenPortalHost.exe display-spike --driver sudovda
```
Expected: relatório com as 6 linhas em `docs/measurements/fase2a-driver-sudovda.md`. O Windows pisca ao criar/remover monitores (esperado). Se `SudoVDA não encontrado`: o driver não está ativo (conferir no Gerenciador de Dispositivos) — registrar no relatório.

- [ ] **Step 3: Commit**

```bash
git add host docs/measurements/fase2a-driver-sudovda.md
git commit -m "phase2a: display-spike command (6 automated tests) and SudoVDA report"
```

---

### Task 5: VDD — instalação (Cayo), relatório e ADR 0007

**Files:**
- Create: `docs/measurements/fase2a-driver-vdd.md` (gerado), `docs/adr/0007-virtual-display-driver.md`

- [ ] **Step 1: Instalar o VDD (Cayo)**

Pedir ao Cayo, num terminal **como administrador**: `winget install --id=VirtualDrivers.Virtual-Display-Driver -e` e aceitar o driver.
Conferir: Gerenciador de Dispositivos → Adaptadores de vídeo → "Virtual Display Driver".

- [ ] **Step 2: Rodar o spike com o VDD (terminal de administrador, o XML fica em pasta protegida)**

Run: `host/Host.App/bin/Release/net10.0-windows10.0.22621.0/ScreenPortalHost.exe display-spike --driver vdd`
Expected: `docs/measurements/fase2a-driver-vdd.md`; o teste 4 sai `N/A` (VDD só remove o último — limitação conhecida).

- [ ] **Step 3: ADR 0007**

`docs/adr/0007-virtual-display-driver.md` com: contexto (D3, brief §15.7), a tabela comparativa (linhas dos dois relatórios lado a lado), decisão (driver escolhido e por quê), consequências (instalação, assinatura/licença, watchdog do SudoVDA, aviso do VDD sobre atualização de driver de GPU, limitação LIFO do VDD, limite do encoder → destino das opções > 4096 px no seletor). Se a escolha for o SudoVDA, atualizar a decisão D3 no brief §16.1 citando o ADR.

- [ ] **Step 4: Commit**

```bash
git add docs
git commit -m "docs: ADR 0007 — virtual display driver decision with VDD vs SudoVDA reports"
```

---

## Parte B — Capacidade do decoder

### Task 6: Pedido de keyframe (KFRQ) — pacote, host e IDR a 5 s

**Files:**
- Create: `host/Host.Domain/Video/KeyframeRequest.cs`, `host/Host.Transport/KeyframeRequestListener.cs`
- Modify: `host/Host.Encode/IVideoEncoder.cs` (`KeyframeIntervalMs = 5000`), `host/Host.App/Control/StreamRegistry.cs` (`RequestKeyframe`), `host/Host.App/Program.cs` (listener no `serve`), `protocol/video-packet-v1.md`
- Test: `host/Host.Tests/Spike/KeyframeRequestTests.cs`

**Interfaces:**
- Produces: `KeyframeRequest(ushort StreamId)` com `const int Size = 8`, `const uint Magic = 0x5152464B`, `void Write(Span<byte>)`, `static bool TryRead(ReadOnlySpan<byte>, out KeyframeRequest)`; `KeyframeRequestListener(int port, Action<IPAddress, ushort> onRequest) : IDisposable` (`const int DefaultPort = 9101`); `StreamRegistry.RequestKeyframe(string address, ushort streamId)`.

- [ ] **Step 1: Teste que falha**

`host/Host.Tests/Spike/KeyframeRequestTests.cs`:
```csharp
using System.Net;
using System.Net.Sockets;
using ScreenPortal.Host.Domain.Video;
using ScreenPortal.Host.Transport;

namespace ScreenPortal.Host.Tests.Spike;

public class KeyframeRequestTests
{
    [Fact]
    public void RoundTrips()
    {
        var buffer = new byte[KeyframeRequest.Size];
        new KeyframeRequest(3).Write(buffer);
        Assert.True(KeyframeRequest.TryRead(buffer, out var read));
        Assert.Equal((ushort)3, read.StreamId);
        buffer[0] = 0;
        Assert.False(KeyframeRequest.TryRead(buffer, out _));
    }

    [Fact]
    public void Listener_ReportsSenderAndStream()
    {
        IPAddress? from = null;
        ushort stream = 0;
        using var got = new ManualResetEventSlim();
        using var listener = new KeyframeRequestListener(0, (a, s) => { from = a; stream = s; got.Set(); });

        using var client = new UdpClient();
        var packet = new byte[KeyframeRequest.Size];
        new KeyframeRequest(2).Write(packet);
        client.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, listener.Port));

        Assert.True(got.Wait(2000));
        Assert.Equal(IPAddress.Loopback, from);
        Assert.Equal((ushort)2, stream);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test host/Host.Tests -v q` → Expected: FAIL (`KeyframeRequest` não existe).

- [ ] **Step 3: Implementar**

`host/Host.Domain/Video/KeyframeRequest.cs`:
```csharp
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
```

`host/Host.Transport/KeyframeRequestListener.cs`:
```csharp
using System.Net;
using System.Net.Sockets;
using ScreenPortal.Host.Domain.Video;

namespace ScreenPortal.Host.Transport;

/// <summary>Recebe pedidos de keyframe (KFRQ) dos headsets em UDP 9101.</summary>
public sealed class KeyframeRequestListener : IDisposable
{
    public const int DefaultPort = 9101;

    private readonly Socket _socket;
    private readonly Action<IPAddress, ushort> _onRequest;
    private volatile bool _stop;

    public int Port { get; }

    public KeyframeRequestListener(int port, Action<IPAddress, ushort> onRequest)
    {
        _onRequest = onRequest;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        Port = ((IPEndPoint)_socket.LocalEndPoint!).Port;
        new Thread(Loop) { IsBackground = true, Name = "kfrq" }.Start();
    }

    private void Loop()
    {
        var buffer = new byte[64];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        while (!_stop)
        {
            int n;
            try { n = _socket.ReceiveFrom(buffer, ref remote); }
            catch (SocketException) { if (_stop) break; continue; }
            catch (ObjectDisposedException) { break; }
            if (KeyframeRequest.TryRead(buffer.AsSpan(0, n), out var request))
                _onRequest(((IPEndPoint)remote).Address.MapToIPv4(), request.StreamId);
        }
    }

    public void Dispose()
    {
        _stop = true;
        _socket.Dispose();
    }
}
```

Em `host/Host.Encode/IVideoEncoder.cs`, trocar `int KeyframeIntervalMs = 1000);` por:
```csharp
    int KeyframeIntervalMs = 5000); // rede de segurança: o normal é o headset pedir IDR (KFRQ) quando precisa
```

Em `host/Host.App/Control/StreamRegistry.cs`, acrescentar o método:
```csharp
    public void RequestKeyframe(string address, ushort streamId)
    {
        lock (_lock)
        {
            foreach (var entry in _entries.Values)
                if (entry.Address == address && entry.StreamId == streamId) entry.Pipeline.RequestKeyframe();
        }
    }
```

Em `RunServe` (`host/Host.App/Program.cs`), após `server.Start();`:
```csharp
    using var keyframes = new KeyframeRequestListener(KeyframeRequestListener.DefaultPort,
        (address, streamId) => streams.RequestKeyframe(address.ToString(), streamId));
```

Em `protocol/video-packet-v1.md`, acrescentar ao final:
```markdown
## Pedido de keyframe (KFRQ) — headset → host, UDP 9101
8 bytes LE: `magic u32 = 0x5152464B ("KFRQ")`, `stream_id u16`, `reserved u16`. O decoder envia após descartar um
frame (falta de input buffer ou fragmento perdido) e enquanto espera o IDR, no máximo 1 pedido a cada 100 ms por stream.
O host força IDR imediato. O IDR periódico (5 s) é só rede de segurança. Mesmo papel do PLI no WebRTC.
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test host/Host.Tests -v q` → Expected: `Com falha: 0`.

- [ ] **Step 5: Commit**

```bash
git add host protocol
git commit -m "phase2a: keyframe request (KFRQ over UDP 9101), host listener; periodic IDR 1 s → 5 s"
```

---

### Task 7: Decoder nativo — espera de 16 ms e pedido de keyframe

**Files:**
- Modify: `headset-native/screenportal/src/main/cpp/packet.h`, `stream_session.h`, `stream_session.cpp`

- [ ] **Step 1: Pacote KFRQ no C++**

Em `packet.h`, antes do `}  // namespace sp` final:
```cpp
// --- Pedido de keyframe (headset → host, UDP 9101). Espelho de host/Host.Domain/Video/KeyframeRequest.cs ---
constexpr uint32_t kKeyframeRequestMagic = 0x5152464B;  // "KFRQ"
constexpr size_t kKeyframeRequestSize = 8;
constexpr uint16_t kKeyframeRequestPort = 9101;

inline void writeKeyframeRequest(uint8_t* dst, uint16_t streamId) {
    uint32_t magic = kKeyframeRequestMagic;
    uint16_t reserved = 0;
    std::memcpy(dst, &magic, 4);
    std::memcpy(dst + 4, &streamId, 2);
    std::memcpy(dst + 6, &reserved, 2);
}
```

- [ ] **Step 2: Sessão**

Em `stream_session.h`, na seção `private:` acrescentar:
```cpp
    void requestKeyframe();
    sockaddr_in hostAddr_{};
    bool hasHost_ = false;
    uint64_t lastKeyframeRequestUs_ = 0;
    uint64_t lastDroppedSeen_ = 0;
```
e o include `#include <netinet/in.h>` no topo.

Em `stream_session.cpp`:

1. Trocar `constexpr int64_t kInputTimeoutUs = 2000;    // não segura a thread de rede` por:
```cpp
constexpr int64_t kInputTimeoutUs = 16000;   // até 1 frame: evita descartar (e a cascata de IDR) por picos curtos
constexpr uint64_t kKeyframeRequestIntervalUs = 100000;  // no máximo 1 pedido a cada 100 ms
```

2. Em `receiveLoop`, logo após `clock_.startIfNeeded(from);`:
```cpp
        if (!hasHost_) {
            hostAddr_ = from;
            hostAddr_.sin_port = htons(kKeyframeRequestPort);
            hasHost_ = true;
        }
```
e trocar `if (!reassembler_.push(packet, static_cast<size_t>(n))) continue;` por:
```cpp
        bool complete = reassembler_.push(packet, static_cast<size_t>(n));
        if (reassembler_.framesDropped() != lastDroppedSeen_) {  // fragmento perdido → decoder vai esperar IDR
            lastDroppedSeen_ = reassembler_.framesDropped();
            requestKeyframe();
        }
        if (!complete) continue;
```

3. Em `submitFrame`, trocar `if (waitingForKeyframe_ && !keyframe) return;` por:
```cpp
    if (waitingForKeyframe_ && !keyframe) {
        requestKeyframe();  // limitado a 1 a cada 100 ms
        return;
    }
```
e, no bloco `if (idx < 0) {`, antes do `std::lock_guard`, acrescentar `requestKeyframe();`.

4. Acrescentar ao final do arquivo (antes de `}  // namespace sp`):
```cpp
void StreamSession::requestKeyframe() {
    if (!hasHost_) return;
    uint64_t now = monotonicUs();
    if (now - lastKeyframeRequestUs_ < kKeyframeRequestIntervalUs) return;
    lastKeyframeRequestUs_ = now;
    uint8_t packet[kKeyframeRequestSize];
    writeKeyframeRequest(packet, static_cast<uint16_t>(config_.streamId));
    sendto(fd_, packet, sizeof(packet), 0, reinterpret_cast<const sockaddr*>(&hostAddr_), sizeof(hostAddr_));
}
```

- [ ] **Step 3: Compilar**

Run: `powershell -ExecutionPolicy Bypass -File tools/build-native.ps1`
Expected: `OK: ...screenportal-native.aar`.

- [ ] **Step 4: Commit**

```bash
git add headset-native Assets/Plugins/Android/screenportal-native.aar
git commit -m "phase2a(native): wait up to 16 ms for input buffers; request keyframe (KFRQ) instead of waiting for periodic IDR"
```

---

### Task 8: Cenários mistos — `stream --mix` e script de medição

**Files:**
- Create: `host/Host.Domain/Video/MixSpec.cs`
- Modify: `host/Host.App/Program.cs` (`RunStream` com `--mix` e KFRQ), `tools/phase0-streams-test.ps1`
- Test: `host/Host.Tests/Spike/MixSpecTests.cs`

**Interfaces:**
- Produces: `MixSpec.Parse(string text) → IReadOnlyList<MixGroup>`; `record MixGroup(int Fps, int Count)`; `MixSpec.TotalStreams(IReadOnlyList<MixGroup>) → int`.

- [ ] **Step 1: Teste que falha**

`host/Host.Tests/Spike/MixSpecTests.cs`:
```csharp
using ScreenPortal.Host.Domain.Video;

namespace ScreenPortal.Host.Tests.Spike;

public class MixSpecTests
{
    [Fact]
    public void ParsesGroups()
    {
        var groups = MixSpec.Parse("60x1, 15x3");
        Assert.Equal(new[] { new MixGroup(60, 1), new MixGroup(15, 3) }, groups);
        Assert.Equal(4, MixSpec.TotalStreams(groups));
    }

    [Theory]
    [InlineData("")]
    [InlineData("60")]
    [InlineData("0x2")]
    [InlineData("60x0")]
    [InlineData("abc")]
    public void RejectsInvalid(string text) => Assert.Throws<FormatException>(() => MixSpec.Parse(text));
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test host/Host.Tests -v q` → Expected: FAIL (`MixSpec` não existe).

- [ ] **Step 3: Implementar**

`host/Host.Domain/Video/MixSpec.cs`:
```csharp
namespace ScreenPortal.Host.Domain.Video;

public readonly record struct MixGroup(int Fps, int Count);

/// <summary>"60x1,15x3" = 1 stream a 60 fps + 3 streams a 15 fps (cenário da FocusPolicy, brief §8.8).</summary>
public static class MixSpec
{
    public static IReadOnlyList<MixGroup> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("mix vazio");
        var groups = new List<MixGroup>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split('x');
            if (pieces.Length != 2 || !int.TryParse(pieces[0], out int fps) || !int.TryParse(pieces[1], out int count)
                || fps <= 0 || fps > 120 || count <= 0 || count > 16)
                throw new FormatException($"grupo inválido: '{part}' (use <fps>x<quantidade>)");
            groups.Add(new MixGroup(fps, count));
        }
        return groups;
    }

    public static int TotalStreams(IReadOnlyList<MixGroup> groups) => groups.Sum(g => g.Count);
}
```

Em `host/Host.App/Program.cs`, substituir o corpo de `RunStream` por:
```csharp
static int RunStream(Args o, CancellationToken ct)
{
    var target = new IPEndPoint(IPAddress.Parse(o.Str("target", "127.0.0.1")), o.Int("port", 9000));
    var groups = o.StrOrNull("mix") is { } mix
        ? ScreenPortal.Host.Domain.Video.MixSpec.Parse(mix)
        : new[] { new ScreenPortal.Host.Domain.Video.MixGroup(o.Int("fps", 60), o.Int("streams", 1)) };

    using var gpu = new GpuDevice();
    using var clock = new ClockSyncServer();
    var pipelines = new List<StreamPipeline>();
    var captures = new List<ICaptureSource>();
    int nextStream = 0;
    foreach (var g in groups)
    {
        var capture = WgcMonitorCapture.ForPrimaryMonitor(gpu, captureCursor: o.Flag("cursor"));
        captures.Add(capture);
        int width = o.Int("width", capture.Width) & ~1;
        int height = o.Int("height", capture.Height) & ~1;
        var settings = new EncoderSettings(width, height, g.Fps, o.Int("bitrate", 20_000));
        var groupTarget = new IPEndPoint(target.Address, target.Port + nextStream);
        pipelines.Add(new StreamPipeline(gpu, capture, settings, groupTarget, g.Count, o.StrOrNull("dump"), (ushort)nextStream));
        nextStream += g.Count;
    }

    using var keyframes = new KeyframeRequestListener(KeyframeRequestListener.DefaultPort, (address, streamId) =>
    {
        int first = 0;
        for (int i = 0; i < groups.Count; i++)
        {
            if (streamId >= first && streamId < first + groups[i].Count) { pipelines[i].RequestKeyframe(); return; }
            first += groups[i].Count;
        }
    });

    Console.WriteLine($"GPU: {gpu.AdapterName}");
    foreach (var p in pipelines) Console.WriteLine(p.Description);
    Console.WriteLine($"clock sync: udp/{ClockSyncServer.DefaultPort} · keyframe requests: udp/{KeyframeRequestListener.DefaultPort}. Ctrl+C para parar.");
    foreach (var p in pipelines) p.Start();

    using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
    if (o.Int("seconds", 0) > 0) timer.CancelAfter(TimeSpan.FromSeconds(o.Int("seconds", 0)));
    ct = timer.Token;

    var sw = Stopwatch.StartNew();
    while (!ct.WaitHandle.WaitOne(1000))
    {
        double s = sw.Elapsed.TotalSeconds;
        sw.Restart();
        for (int i = 0; i < pipelines.Count; i++)
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {groups[i].Fps}fps×{groups[i].Count}: {pipelines[i].TakeStats(s)} | clk req {Interlocked.Exchange(ref clock.Requests, 0)}");
    }
    foreach (var p in pipelines) p.Dispose();
    return 0;
}
```

- [ ] **Step 4: Script de medição com `-Mix`, `-Runs` e critério**

Em `tools/phase0-streams-test.ps1`:
1. Trocar a linha `param(...)` para incluir `[string]$Mix = "", [int]$Runs = 1,` logo após `[int]$Fps = 60,`.
2. Após a definição de `$Label`, inserir:
```powershell
if ($Mix) { $Streams = ($Mix -split ',' | ForEach-Object { [int](($_.Trim() -split 'x')[1]) } | Measure-Object -Sum).Sum }
```
3. Trocar `$hostArgs = "stream --target $QuestIp --streams $Streams --bitrate $Bitrate --fps $Fps" +` por:
```powershell
$hostArgs = $(if ($Mix) { "stream --target $QuestIp --mix `"$Mix`" --bitrate $Bitrate" } else { "stream --target $QuestIp --streams $Streams --bitrate $Bitrate --fps $Fps" }) +
```
4. Envolver o bloco desde `& $adb -s $Serial shell am force-stop` até o final num laço `for ($run = 1; $run -le $Runs; $run++) { ... }`, usando `$runLabel = "$Label-r$run"` no lugar de `$Label` dentro do laço, e ao final de cada volta imprimir, por tela, `fps médio`, `fps mínimo`, `lat média`, `descartes`, e a avaliação:
```powershell
    $focus = $summary | Where-Object Stream -eq 0
    $others = $summary | Where-Object Stream -ne 0
    $pass = $focus.MinFps -ge 58 -and ($others | Where-Object { $_.MinFps -lt 14 }).Count -eq 0 `
            -and ($summary | Where-Object { $_.AvgLat -ge 50 -or $_.Drops -gt 0 }).Count -eq 0
    "  critério MVP (1@60 ≥58 · demais ≥14 · lat < 50 ms · 0 descartes): $(if ($pass) { 'PASSOU' } else { 'FALHOU' })"
```
onde `$summary` é a lista de objetos `{ Stream, AvgFps, MinFps, AvgLat, Drops }` montada a partir do CSV (descartando os 5 primeiros segundos, como hoje).

- [ ] **Step 5: Rodar os testes e commit**

Run: `dotnet test host/Host.Tests -v q` → Expected: `Com falha: 0`.
```bash
git add host tools
git commit -m "phase2a: mixed-rate streaming (--mix) with keyframe requests; measurement script with runs and MVP criterion"
```

---

### Task 9: Sessão no Quest, relatório e ADR 0008

**Files:**
- Create: `docs/measurements/fase2a-decoder.md`, `docs/adr/0008-decode-capacity.md`

- [ ] **Step 1: Preparar (Claude)**

```powershell
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command boot-video
powershell -ExecutionPolicy Bypass -File tools/unity-cmd.ps1 -Command build-apk -TimeoutMinutes 40
& "D:\Unity Editor\6000.3.19f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" -s 2G0YC1ZG410B1H install -r -g Builds/Phase0.apk
Start-Process msedge -ArgumentList "--kiosk `"file:///$((Resolve-Path tools/text-test.html).Path -replace '\\','/')`" --edge-kiosk-type=fullscreen"
```
Conferir bateria ≥ 80 % (`dumpsys battery`) e headset no cabo.

- [ ] **Step 2: Cenários (Cayo com o headset na cabeça ~20 min; Claude roda os comandos)**

```powershell
powershell -ExecutionPolicy Bypass -File tools/phase0-streams-test.ps1 -Mix "60x1,15x3" -Runs 3 -Seconds 60 -Label mvp
powershell -ExecutionPolicy Bypass -File tools/phase0-streams-test.ps1 -Mix "60x1,15x5" -Runs 1 -Seconds 60 -Label mix6
powershell -ExecutionPolicy Bypass -File tools/phase0-streams-test.ps1 -Streams 2 -Runs 1 -Seconds 60 -Label all60-2
powershell -ExecutionPolicy Bypass -File tools/phase0-streams-test.ps1 -Streams 3 -Runs 1 -Seconds 60 -Label all60-3
powershell -ExecutionPolicy Bypass -File tools/phase0-streams-test.ps1 -Streams 4 -Runs 1 -Seconds 60 -Label all60-4
```
(4K: repetir o `mvp` com o monitor primário em 3840×2160 se disponível; senão, registrar como não medido.)

- [ ] **Step 3: Relatório e ADR 0008**

`docs/measurements/fase2a-decoder.md`: condições (bateria, conteúdo, commit), uma tabela por cenário (por tela: fps médio/mín, latência média/máx, descartes) e o veredito do critério MVP nas 3 repetições.
`docs/adr/0008-decode-capacity.md`: limites que o `BandwidthBudget` (2F) vai usar (telas simultâneas por perfil de fps), efeito da correção (comparar com a Fase 0), e o plano B (atlas) se o critério falhar.

- [ ] **Step 4: Atualizar STATUS e commit**

STATUS: 2A ✅ com links para os dois relatórios e os ADRs 0007/0008.
```bash
git add docs
git commit -m "docs: Phase 2A decoder capacity report and ADR 0008; STATUS"
```
