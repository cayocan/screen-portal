namespace ScreenPortal.Host.Domain.Video;

/// <summary>Faixa aceita para <c>SetStreamRate</c> (spec 2F §4): o valor vem da rede.</summary>
public static class StreamRateLimits
{
    public const int MinFps = 1, MaxFps = 60;
    public const int MinKbps = 1_000, MaxKbps = 50_000;

    public static (int Fps, int Kbps) Clamp(int fps, int kbps) =>
        (Math.Clamp(fps, MinFps, MaxFps), Math.Clamp(kbps, MinKbps, MaxKbps));

    /// <summary>
    /// Bitrate do encoder (configurado para 60 fps): o AMF reserva bitrate÷60 por quadro, então cortar quadros já baixa a
    /// banda na proporção. Mudar o bitrate ao vivo (ICodecAPI) desregula o AMF (medido na 2F: ×3 a 60 fps), então a base
    /// só muda recriando o encoder.
    /// </summary>
    public static int BaseKbpsAt60(int fps, int kbps) =>
        Math.Clamp((int)Math.Round(kbps * 60.0 / Math.Max(1, fps), MidpointRounding.AwayFromZero), MinKbps, MaxKbps);

    /// <summary>Recriar o encoder só quando a base muda de verdade (mais de 5%): Auto/Fixo/Eco = 20 Mbps, Vídeo = 30.</summary>
    public static bool NeedsNewEncoder(int currentBaseKbps, int baseKbps) =>
        Math.Abs(baseKbps - currentBaseKbps) > currentBaseKbps * 0.05;
}
