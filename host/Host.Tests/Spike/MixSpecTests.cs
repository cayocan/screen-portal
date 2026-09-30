using ScreenPortal.Host.Domain.Video;

namespace ScreenPortal.Host.Tests.Spike;

public class MixSpecTests
{
    [Fact]
    public void ParsesGroups()
    {
        var groups = MixSpec.Parse("60x1, 15x3");
        Assert.Equal(new[] { new MixGroup(60, 1), new MixGroup(15, 3) }, groups);
        Assert.Equal(4, MixSpec.TotalStreams(groups));
    }

    [Theory]
    [InlineData("")]
    [InlineData("60")]
    [InlineData("0x2")]
    [InlineData("60x0")]
    [InlineData("abc")]
    public void RejectsInvalid(string text) => Assert.Throws<FormatException>(() => MixSpec.Parse(text));
}
