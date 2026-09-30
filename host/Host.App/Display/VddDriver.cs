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
