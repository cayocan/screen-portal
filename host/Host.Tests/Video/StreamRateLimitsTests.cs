using ScreenPortal.Host.Domain.Video;

namespace ScreenPortal.Host.Tests.Video;

public class StreamRateLimitsTests
{
    [Theory]
    [InlineData(15, 5000, 15, 5000)]
    [InlineData(0, 0, 1, 1000)]
    [InlineData(255, 900000, 60, 50000)]
    public void Clamp_KeepsTheRateInRange(int fps, int kbps, int expectedFps, int expectedKbps) =>
        Assert.Equal((expectedFps, expectedKbps), StreamRateLimits.Clamp(fps, kbps));

    [Theory]
    [InlineData(15, 5000, 20000)]  // Auto fora do foco: mesma base
    [InlineData(5, 1667, 20004)]   // Eco: mesma base (arredondamento)
    [InlineData(60, 30000, 30000)] // Vídeo
    [InlineData(30, 15000, 30000)] // Vídeo rebaixado pelo orçamento: base de Vídeo
    public void BaseKbpsAt60_IsTheBitsPerFrameTimes60(int fps, int kbps, int expected) =>
        Assert.Equal(expected, StreamRateLimits.BaseKbpsAt60(fps, kbps));

    [Theory]
    [InlineData(20000, 20004, false)]
    [InlineData(20000, 30000, true)]
    [InlineData(30000, 20000, true)]
    public void NewEncoder_OnlyWhenTheBaseReallyChanges(int current, int next, bool expected) =>
        Assert.Equal(expected, StreamRateLimits.NeedsNewEncoder(current, next));
}
