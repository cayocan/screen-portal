using ScreenPortal.Host.Domain.Display;

namespace ScreenPortal.Host.Tests.Screens;

public class PopupPlacementTests
{
    private static readonly RectI Window = new(1000, 200, 1101, 826);

    [Fact]
    public void PopupInside_IsCopiedWhole_AtItsPlace()
    {
        Assert.True(PopupPlacement.Clip(Window, 1096, 824, new RectI(1150, 350, 393, 329), out var b));
        Assert.Equal(new PopupBlit(0, 0, 150, 150, 393, 329), b);
    }

    [Fact]
    public void PopupLeakingRightAndBottom_IsCut()
    {
        Assert.True(PopupPlacement.Clip(Window, 1096, 824, new RectI(1900, 900, 393, 329), out var b));
        Assert.Equal(new PopupBlit(0, 0, 900, 700, 196, 124), b);
    }

    [Fact]
    public void PopupLeakingLeftAndTop_StartsInsideTheBitmap()
    {
        Assert.True(PopupPlacement.Clip(Window, 1096, 824, new RectI(900, 150, 300, 200), out var b));
        Assert.Equal(new PopupBlit(100, 50, 0, 0, 200, 150), b);
    }

    [Fact]
    public void PopupOutside_IsSkipped()
    {
        Assert.False(PopupPlacement.Clip(Window, 1096, 824, new RectI(3000, 200, 300, 200), out _));
        Assert.False(PopupPlacement.Clip(Window, 1096, 824, new RectI(1000, 1024, 300, 200), out _), "logo abaixo do quadro");
    }
}
