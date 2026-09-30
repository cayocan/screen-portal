using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace ScreenPortal.Host.Capture;

/// <summary>
/// Captura de um monitor via Windows Graphics Capture (brief §6.3).
/// O timestamp é o SystemRelativeTime do frame (QPC), o mesmo domínio de <c>HostClock</c>.
/// </summary>
public sealed unsafe class WgcMonitorCapture : ICaptureSource
{
    private static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid IID_IGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    private static readonly Guid IID_ID3D11Texture2D = typeof(ID3D11Texture2D).GUID;

    private readonly GpuDevice _gpu;
    private readonly GraphicsCaptureItem _item;
    private readonly bool _captureCursor;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private ICaptureFrameSink? _sink;
    private Windows.Graphics.SizeInt32 _poolSize;
    private readonly object _frameLock = new();
    private bool _disposed;

    /// <summary>Diagnóstico (2C): mudança de tamanho da captura e fechamento do item pelo Windows.</summary>
    public event Action<string>? Diagnostic;

    public string Name => $"WGC monitor ({_item.DisplayName})";
    public int Width => _item.Size.Width;
    public int Height => _item.Size.Height;

    private WgcMonitorCapture(GpuDevice gpu, GraphicsCaptureItem item, bool captureCursor)
    {
        _gpu = gpu;
        _item = item;
        _captureCursor = captureCursor;
    }

    public static WgcMonitorCapture ForPrimaryMonitor(GpuDevice gpu, bool captureCursor) =>
        ForMonitor(gpu, MonitorFromPoint(default, MONITOR_DEFAULTTOPRIMARY), captureCursor);

    public static WgcMonitorCapture ForMonitor(GpuDevice gpu, IntPtr hmonitor, bool captureCursor) =>
        new(gpu, CreateItem(hmonitor, forWindow: false), captureCursor);

    /// <summary>Captura só a janela (monitor-palco com recorte, brief §10.5).</summary>
    public static WgcMonitorCapture ForWindow(GpuDevice gpu, IntPtr hwnd, bool captureCursor) =>
        new(gpu, CreateItem(hwnd, forWindow: true), captureCursor);

    private static GraphicsCaptureItem CreateItem(IntPtr handle, bool forWindow)
    {
        if (!GraphicsCaptureSession.IsSupported())
            throw new PlatformNotSupportedException("Windows Graphics Capture não é suportado neste sistema.");

        var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");

        // IGraphicsCaptureItemInterop: slot 3 = CreateForWindow, slot 4 = CreateForMonitor — chamada direta, sem RCW.
        var iidInterop = IID_IGraphicsCaptureItemInterop;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(factory.ThisPtr, in iidInterop, out IntPtr interop));
        IntPtr itemPtr;
        try
        {
            var iidItem = IID_IGraphicsCaptureItem;
            var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)(*(*(void***)interop + (forWindow ? 3 : 4)));
            Marshal.ThrowExceptionForHR(create(interop, handle, &iidItem, &itemPtr));
        }
        finally { Marshal.Release(interop); }

        try { return GraphicsCaptureItem.FromAbi(itemPtr); }
        finally { Marshal.Release(itemPtr); }
    }

    public void Start(ICaptureFrameSink sink)
    {
        _sink = sink;
        _poolSize = _item.Size;
        // 2 buffers: o mínimo para não bloquear a captura enquanto o frame anterior é convertido.
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _gpu.WinRtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
        _pool.FrameArrived += OnFrameArrived;
        _item.Closed += OnItemClosed;

        _session = _pool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = _captureCursor;
        if (ApiInformation.IsPropertyPresent(typeof(GraphicsCaptureSession).FullName, nameof(GraphicsCaptureSession.IsBorderRequired)))
        {
            try { _session.IsBorderRequired = false; } catch { /* exige consentimento no Win11; a borda amarela é aceitável no spike */ }
        }
        _session.StartCapture();
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool pool, object _)
    {
        // O pool é free-threaded: sem o lock, Dispose pode liberar pool/sessão no meio deste callback (0xC0000005).
        lock (_frameLock)
        {
            if (_disposed) return;
            HandleFrame(pool);
        }
    }

    private void HandleFrame(Direct3D11CaptureFramePool pool)
    {
        using var frame = pool.TryGetNextFrame();
        if (frame is null) return;

        var size = frame.ContentSize;
        if (size.Width != _poolSize.Width || size.Height != _poolSize.Height)
        {
            // Tamanho mudou (janela redimensionada, ou monitor recém-criado cujo item.Size ainda era o antigo):
            // recria o pool. O frame precisa ser liberado ANTES do Recreate — liberar depois é 0xC0000005.
            frame.Dispose();
            Diagnostic?.Invoke($"tamanho da captura mudou {_poolSize.Width}×{_poolSize.Height} → {size.Width}×{size.Height}");
            _poolSize = size;
            pool.Recreate(_gpu.WinRtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            return;
        }

        // SystemRelativeTime é o instante de apresentação alvo do DWM (medido: ~6 ms no FUTURO em
        // relação à chegada), então não serve como início do relógio de latência. Usamos a chegada
        // do frame: subestima o glass-to-glass em ~1 vsync do PC (documentado em docs/measurements).
        ulong captureUs = Domain.Timing.HostClock.NowUs();
        using var texture = new ID3D11Texture2D(GetTexturePointer(frame.Surface));
        _sink!.OnFrame(texture, size.Width, size.Height, captureUs);
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args) =>
        Diagnostic?.Invoke("item de captura fechado pelo Windows (monitor removido ou topologia mudou)");

    /// <summary>IDirect3DDxgiInterfaceAccess::GetInterface (vtable slot 3). Devolve ponteiro com AddRef.</summary>
    private static IntPtr GetTexturePointer(IDirect3DSurface surface)
    {
        IntPtr surfacePtr = ((IWinRTObject)surface).NativeObject.ThisPtr;
        var iidAccess = IID_IDirect3DDxgiInterfaceAccess;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(surfacePtr, in iidAccess, out IntPtr access));
        try
        {
            var iidTex = IID_ID3D11Texture2D;
            IntPtr texture;
            var getInterface = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)(*(*(void***)access + 3));
            Marshal.ThrowExceptionForHR(getInterface(access, &iidTex, &texture));
            return texture;
        }
        finally { Marshal.Release(access); }
    }

    public void Dispose()
    {
        lock (_frameLock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        if (_pool is not null) _pool.FrameArrived -= OnFrameArrived;
        _item.Closed -= OnItemClosed;
        _session?.Dispose();
        _pool?.Dispose();
    }

    private const uint MONITOR_DEFAULTTOPRIMARY = 1;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint flags);
}
