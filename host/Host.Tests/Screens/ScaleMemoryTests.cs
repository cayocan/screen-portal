using ScreenPortal.Host.Domain.Display;
using Xunit;

namespace ScreenPortal.Host.Tests.Screens;

public class ScaleMemoryTests
{
    [Fact]
    public void Observe_ReportsOnlyChanges()
    {
        var m = new ScaleMemory();
        Assert.True(m.Observe("quest", 7, 150));
        Assert.False(m.Observe("quest", 7, 150));
        Assert.True(m.Observe("quest", 7, 125));
        Assert.True(m.TryGet("quest", 7, out var p));
        Assert.Equal(125, p);
    }

    [Fact]
    public void MonitorNotFound_IsIgnored()
    {
        var m = new ScaleMemory();
        Assert.False(m.Observe("quest", 7, 0));
        Assert.False(m.TryGet("quest", 7, out _));
    }

    [Fact]
    public void KeysAreSeparatedByHeadsetAndScreen()
    {
        var m = new ScaleMemory();
        m.Observe("quest", 7, 150);
        m.Observe("other", 7, 200);
        m.Observe("quest", 8, 100);
        Assert.True(m.TryGet("quest", 7, out var a));
        Assert.True(m.TryGet("other", 7, out var b));
        Assert.True(m.TryGet("quest", 8, out var c));
        Assert.Equal((150, 200, 100), (a, b, c));
    }

    [Fact]
    public void RoundTripsThroughJson()
    {
        var m = new ScaleMemory();
        m.Observe("quest", 7, 175);
        var back = ScaleMemory.FromJson(m.ToJson());
        Assert.True(back.TryGet("quest", 7, out var p));
        Assert.Equal(175, p);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ quebrado")]
    public void BadFile_StartsEmpty(string? json)
    {
        Assert.False(ScaleMemory.FromJson(json).TryGet("quest", 7, out _));
    }
}
