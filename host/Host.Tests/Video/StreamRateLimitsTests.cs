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
}
