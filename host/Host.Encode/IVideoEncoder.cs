using Vortice.Direct3D11;

namespace ScreenPortal.Host.Encode;

public interface IEncodedFrameSink
{
    /// <param name="accessUnit">H.264 Annex-B. Só é válido durante a chamada.</param>
    void OnEncodedFrame(ReadOnlySpan<byte> accessUnit, bool keyframe, ulong captureTimeUs);
}

public sealed record EncoderSettings(
    int Width,
    int Height,
    int Fps = 60,
    int BitrateKbps = 20_000,
    int KeyframeIntervalMs = 5000); // rede de segurança: o normal é o headset pedir IDR (KFRQ) quando precisa

/// <summary>Strategy de encoder (brief §8.6): MF hardware (NVENC/AMF/QSV via MFT), software, etc.</summary>
public interface IVideoEncoder : IDisposable
{
    string Name { get; }

    /// <summary>
    /// Entrega uma textura NV12 para codificar. Não bloqueia: se o encoder estiver ocupado,
    /// o frame pendente anterior é substituído (descartar frame velho &gt; enfileirar latência).
    /// </summary>
    void Submit(ID3D11Texture2D nv12, ulong captureTimeUs);

    void RequestKeyframe();
}
