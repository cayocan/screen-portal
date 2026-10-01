using ScreenPortal.Host.Domain.Display;

namespace ScreenPortal.Host.Tests.Screens;

public class MonitorArrangementTests
{
    private static readonly RectI Primary = new(0, 0, 2560, 1080);

    [Fact]
    public void Above_KeepsTheBottomEdgeOnThePrimary()
    {
        // Pedido do Cayo: recriar um monitor que estava em cima não pode jogá-lo para o lado.
        var target = MonitorArrangement.Reattach(new RectI(0, -1440, 2560, 1440), 1920, 1080, [Primary]);
        Assert.Equal(new RectI(0, -1080, 1920, 1080), target);
    }

    [Fact]
    public void Below_KeepsTheTopEdge()
    {
        var target = MonitorArrangement.Reattach(new RectI(0, 1080, 2560, 1440), 1440, 2560, [Primary]);
        Assert.Equal(new RectI(0, 1080, 1440, 2560), target);
    }

    [Fact]
    public void Right_KeepsTheLeftEdge()
    {
        var target = MonitorArrangement.Reattach(new RectI(2560, 0, 2560, 1440), 1920, 1080, [Primary]);
        Assert.Equal(new RectI(2560, 0, 1920, 1080), target);
    }

    [Fact]
    public void Left_KeepsTheRightEdge()
    {
        var target = MonitorArrangement.Reattach(new RectI(-2560, 0, 2560, 1440), 1440, 2560, [Primary]);
        Assert.Equal(new RectI(-1440, 0, 1440, 2560), target);
    }

    [Fact]
    public void OnlyACornerTouching_DoesNotCountAsANeighbor_KeepsTheTopLeft()
    {
        var target = MonitorArrangement.Reattach(new RectI(2560, -1440, 2560, 1440), 1920, 1080, [Primary]);
        Assert.Equal(new RectI(2560, -1440, 1920, 1080), target);
    }
}
