using Vortice.Direct3D11;

namespace ScreenPortal.Host.Capture;

/// <summary>
/// Recebe cada frame capturado. A textura (BGRA) só é válida durante a chamada: quem precisa
/// guardar o conteúdo copia/converte na GPU antes de retornar.
/// </summary>
public interface ICaptureFrameSink
{
    void OnFrame(ID3D11Texture2D bgraTexture, int width, int height, ulong captureTimeUs);
}

/// <summary>Strategy de captura (brief §8.6): WGC por monitor, WGC por janela, DXGI Desktop Duplication.</summary>
public interface ICaptureSource : IDisposable
{
    string Name { get; }
    int Width { get; }
    int Height { get; }
    void Start(ICaptureFrameSink sink);
}
