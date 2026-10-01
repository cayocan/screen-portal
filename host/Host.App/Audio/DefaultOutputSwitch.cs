using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace ScreenPortal.Host.App.Audio;

/// <summary>
/// Troca a saída padrão do Windows (os três papéis) e devolve a original no <see cref="Dispose"/> (spec 2G §3). Usa o
/// IPolicyConfig, a interface (não documentada, estável desde o Windows 7) que o próprio painel de Som usa.
/// </summary>
public sealed class DefaultOutputSwitch : IDisposable
{
    private readonly string?[] _original = new string?[3];
    private readonly Action<string> _log;
    private bool _switched;

    public DefaultOutputSwitch(Action<string> log) => _log = log;

    public void SwitchTo(MMDevice target)
    {
        if (_switched) return;
        using var enumerator = new MMDeviceEnumerator();
        for (int role = 0; role < 3; role++)
        {
            try { _original[role] = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, (Role)role).ID; }
            catch (COMException) { _original[role] = null; }
        }
        SetAll(target.ID);
        _switched = true;
        _log($"áudio: saída padrão do Windows → {target.FriendlyName} (devolvo ao desconectar)");
    }

    public void Restore()
    {
        if (!_switched) return;
        _switched = false;
        for (int role = 0; role < 3; role++)
            if (_original[role] != null) Set(_original[role]!, role);
        _log("áudio: saída padrão do Windows devolvida");
    }

    public void Dispose() => Restore();

    private static void SetAll(string id)
    {
        for (int role = 0; role < 3; role++) Set(id, role);
    }

    private static void Set(string id, int role)
    {
        var config = (IPolicyConfig)new PolicyConfigClient();
        try { Marshal.ThrowExceptionForHR(config.SetDefaultEndpoint(id, role)); }
        finally { Marshal.ReleaseComObject(config); }
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string device, IntPtr format);
        [PreserveSig] int GetDeviceFormat(string device, int isDefault, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(string device);
        [PreserveSig] int SetDeviceFormat(string device, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(string device, int isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
        [PreserveSig] int SetProcessingPeriod(string device, IntPtr period);
        [PreserveSig] int GetShareMode(string device, IntPtr mode);
        [PreserveSig] int SetShareMode(string device, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string device, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue(string device, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string device, int role);
        [PreserveSig] int SetEndpointVisibility(string device, int visible);
    }
}
