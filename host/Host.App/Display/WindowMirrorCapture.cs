using System.Diagnostics;
using System.Runtime.InteropServices;
using ScreenPortal.Host.Capture;
using ScreenPortal.Host.Domain.Display;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Box = Vortice.Mathematics.Box;

namespace ScreenPortal.Host.App.Display;

/// <summary>
/// Espelho de janela (ADR 0018): WGC da janela + popups do <see cref="PopupTracker"/> compostos num quadro fixo do
/// tamanho da janela no destaque (popups cortados na borda). Com popup aberto, o quadro é refeito a ~30 fps mesmo sem
/// quadro novo da janela. O contexto D3D é protegido para multithread (GpuDevice); um lock serializa as duas fontes.
/// </summary>
public sealed class WindowMirrorCapture : ICaptureSource, ICaptureFrameSink
{
    private static readonly TimeSpan PopupPeriod = TimeSpan.FromMilliseconds(33);

    private readonly GpuDevice _gpu;
    private readonly IntPtr _hwnd;
    private readonly WgcMonitorCapture _window;
    private readonly PopupTracker _popups;
    private readonly ID3D11Texture2D _canvas;
    private readonly ID3D11Texture2D _mainCopy;
    private readonly Dictionary<IntPtr, (ID3D11Texture2D Texture, int W, int H, int Version)> _popupTextures = new();
    private readonly object _gate = new();
    private readonly Thread _popupThread;
    private ICaptureFrameSink? _sink;
    private bool _hasMain;
    private int _composedVersion = -1;
    private volatile bool _stop;

    public string Name => $"espelho da janela 0x{_hwnd.ToInt64():X}";
    public int Width { get; }
    public int Height { get; }

    public WindowMirrorCapture(GpuDevice gpu, IntPtr hwnd, WgcMonitorCapture window, int width, int height)
    {
        _gpu = gpu;
        _hwnd = hwnd;
        _window = window;
        Width = width;
        Height = height;
        _canvas = Texture(width, height);
        _mainCopy = Texture(width, height);
        _popups = new PopupTracker(hwnd);
        _popupThread = new Thread(RunPopups) { IsBackground = true, Name = "MirrorPopups" };
    }

    private ID3D11Texture2D Texture(int w, int h) => _gpu.Device.CreateTexture2D(new Texture2DDescription
    {
        Width = (uint)w,
        Height = (uint)h,
        MipLevels = 1,
        ArraySize = 1,
        Format = Format.B8G8R8A8_UNorm,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Default,
        BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
    });

    public void Start(ICaptureFrameSink sink)
    {
        _sink = sink;
        _window.Start(this);
        _popupThread.Start();
    }

    /// <summary>Quadro novo da janela (thread do WGC).</summary>
    public void OnFrame(ID3D11Texture2D bgraTexture, int width, int height, ulong captureTimeUs)
    {
        lock (_gate)
        {
            if (_stop) return;
            var box = new Box(0, 0, 0, Math.Min(width, Width), Math.Min(height, Height), 1);
            _gpu.Context.CopySubresourceRegion(_mainCopy, 0, 0, 0, 0, bgraTexture, 0, box);
            _hasMain = true;
            Compose(captureTimeUs);
        }
    }

    /// <summary>Popup aberto/fechado/mudou: refaz o quadro com a última imagem da janela.</summary>
    private void RunPopups()
    {
        while (!_stop)
        {
            Thread.Sleep(PopupPeriod);
            int version = _popups.Version;
            if (version == _composedVersion) continue;
            lock (_gate)
            {
                if (_stop || !_hasMain) continue;
                Compose((ulong)(Stopwatch.GetTimestamp() * 1_000_000 / Stopwatch.Frequency));
            }
        }
    }

    private void Compose(ulong timeUs) // chamar com _gate
    {
        var ctx = _gpu.Context;
        ctx.CopyResource(_canvas, _mainCopy);
        _composedVersion = _popups.Version;
        var popups = _popups.Snapshot;
        if (popups.Count > 0 && DwmGetWindowAttribute(_hwnd, 9, out var frame, Marshal.SizeOf<Rect>()) == 0) // EXTENDED_FRAME_BOUNDS
        {
            var window = new RectI(frame.Left, frame.Top, frame.Right - frame.Left, frame.Bottom - frame.Top);
            foreach (var p in popups)
            {
                if (!PopupPlacement.Clip(window, Width, Height, p.Rect, out var blit)) continue;
                var texture = Upload(p);
                ctx.CopySubresourceRegion(_canvas, 0, (uint)blit.DstX, (uint)blit.DstY, 0, texture, 0,
                    new Box(blit.SrcX, blit.SrcY, 0, blit.SrcX + blit.Width, blit.SrcY + blit.Height, 1));
            }
        }
        _sink?.OnFrame(_canvas, Width, Height, timeUs);
    }

    private ID3D11Texture2D Upload(PopupTracker.Popup p)
    {
        if (_popupTextures.TryGetValue(p.Hwnd, out var t) && t.W == p.Width && t.H == p.Height)
        {
            if (t.Version == p.Version) return t.Texture;
        }
        else
        {
            t.Texture?.Dispose();
            t = (Texture(p.Width, p.Height), p.Width, p.Height, 0);
        }
        var handle = GCHandle.Alloc(p.Bgra, GCHandleType.Pinned);
        try { _gpu.Context.UpdateSubresource(t.Texture, 0, null, handle.AddrOfPinnedObject(), (uint)(p.Width * 4), 0); }
        finally { handle.Free(); }
        _popupTextures[p.Hwnd] = (t.Texture, p.Width, p.Height, p.Version);
        return t.Texture;
    }

    public void Dispose()
    {
        _stop = true;
        _window.Dispose();
        _popups.Dispose();
        if (_popupThread.IsAlive) _popupThread.Join(TimeSpan.FromSeconds(1));
        lock (_gate)
        {
            foreach (var t in _popupTextures.Values) t.Texture.Dispose();
            _popupTextures.Clear();
            _canvas.Dispose();
            _mainCopy.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);
}
