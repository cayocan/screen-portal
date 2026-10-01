namespace ScreenPortal.Host.Domain.Video;

/// <summary>Faixa aceita para <c>SetStreamRate</c> (spec 2F §4): o valor vem da rede.</summary>
public static class StreamRateLimits
{
    public const int MinFps = 1, MaxFps = 60;
    public const int MinKbps = 1_000, MaxKbps = 50_000;

    public static (int Fps, int Kbps) Clamp(int fps, int kbps) =>
        (Math.Clamp(fps, MinFps, MaxFps), Math.Clamp(kbps, MinKbps, MaxKbps));
}
