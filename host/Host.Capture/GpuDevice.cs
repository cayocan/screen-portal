using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace ScreenPortal.Host.Capture;

/// <summary>
/// Device D3D11 único compartilhado por captura, conversão de cor e encoder (zero cópia entre eles).
/// Multithread-protected porque WGC, o video processor e o MFT assíncrono usam threads diferentes.
/// </summary>
public sealed class GpuDevice : IDisposable
{
    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }
    public IDirect3DDevice WinRtDevice { get; }
    public string AdapterName { get; }

    public GpuDevice()
    {
        D3D11.D3D11CreateDevice(
            IntPtr.Zero, DriverType.Hardware,
            DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out ID3D11Device? device, out ID3D11DeviceContext? context).CheckError();

        Device = device!;
        Context = context!;
        using (var mt = Device.QueryInterface<ID3D11Multithread>())
            mt.SetMultithreadProtected(true);

        using var dxgiDevice = Device.QueryInterface<IDXGIDevice>();
        using (var adapter = dxgiDevice.GetAdapter())
            AdapterName = adapter.Description.Description;

        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var inspectable));
        try { WinRtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable); }
        finally { Marshal.Release(inspectable); }
    }

    public void Dispose()
    {
        (WinRtDevice as IDisposable)?.Dispose();
        Context.Dispose();
        Device.Dispose();
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
}
