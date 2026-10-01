using ScreenPortal.Host.Domain.Display;

namespace ScreenPortal.Host.Tests.Screens;

public class DisplayArrangementTests
{
    private const string Primary = @"\\.\DISPLAY1";
    private static readonly WindowsMonitor PrimaryMonitor = new(Primary, new RectI(0, 0, 2560, 1080), true);

    // Telas de 1 m de largura a 1,3 m: ~42° de largura → meio-tamanho ~21°.
    private static ArrangedScreen Screen(string name, int w, int h, double az, double el, double hw = 21, double hh = 0) =>
        new(name, w, h, az, el, hw, hh > 0 ? hh : hw * h / w);

    private static IReadOnlyDictionary<string, RectI> Plan(params ArrangedScreen[] screens) =>
        DisplayArrangement.Plan(screens, [PrimaryMonitor], Primary);

    [Fact]
    public void ToTheRightInTheQuest_IsToTheRightInWindows_VerticallyCentered()
    {
        var plan = Plan(Screen(Primary, 2560, 1080, 0, 0), Screen("V", 2560, 1440, 43, 0));
        Assert.Equal(new RectI(0, 0, 2560, 1080), plan[Primary]);
        Assert.Equal(new RectI(2560, -180, 2560, 1440), plan["V"]);
    }

    [Fact]
    public void Above_IsAbove_HorizontallyCentered()
    {
        var plan = Plan(Screen(Primary, 2560, 1080, 0, 0), Screen("V", 2560, 1440, 0, 22));
        Assert.Equal(new RectI(0, -1440, 2560, 1440), plan["V"]);
    }

    [Fact]
    public void Left_And_Below()
    {
        var plan = Plan(Screen(Primary, 2560, 1080, 0, 0), Screen("L", 1080, 1920, -32, 0), Screen("B", 1600, 1200, 0, -24));
        Assert.Equal(-1080, plan["L"].X);
        Assert.Equal(1080, plan["B"].Y);
    }

    [Fact]
    public void ThreeInARow_ChainFromTheNearestNeighbor()
    {
        var plan = Plan(Screen(Primary, 2560, 1080, 0, 0), Screen("B", 2560, 1440, 43, 0), Screen("C", 2560, 1440, 86, 0));
        Assert.Equal(2560, plan["B"].X);
        Assert.Equal(5120, plan["C"].X);
    }

    [Fact]
    public void PrimaryNotInTheQuest_TheMostCentralScreenGoesRightOfIt()
    {
        var plan = Plan(Screen("V1", 2560, 1440, 2, 0), Screen("V2", 2560, 1440, 45, 0));
        Assert.Equal(2560, plan["V1"].X);
        Assert.Equal(5120, plan["V2"].X);
        Assert.False(plan.ContainsKey(Primary));
    }

    [Fact]
    public void PhysicalMonitorOutsideTheQuest_IsAnObstacle()
    {
        var physical = new WindowsMonitor(@"\\.\DISPLAY2", new RectI(2560, 0, 1920, 1080), false);
        var plan = DisplayArrangement.Plan([Screen(Primary, 2560, 1080, 0, 0), Screen("V", 2560, 1440, 43, 0)],
            [PrimaryMonitor, physical], Primary);
        Assert.Equal(4480, plan["V"].X);
    }

    [Fact]
    public void FiveScreens_NeverOverlap_AndEveryOneTouchesAnother()
    {
        var plan = Plan(Screen(Primary, 2560, 1080, 0, 0), Screen("A", 2560, 1440, 0, 23), Screen("B", 1080, 1920, -33, 5),
            Screen("C", 1080, 1920, 33, 5), Screen("D", 1600, 1200, 0, -25));
        var rects = plan.Values.ToList();
        for (int i = 0; i < rects.Count; i++)
        {
            Assert.True(rects.Where((_, j) => j != i).Any(o => Touch(rects[i], o)), $"{rects[i]} solta");
            for (int j = i + 1; j < rects.Count; j++) Assert.False(Overlap(rects[i], rects[j]), $"{rects[i]} × {rects[j]}");
        }
    }

    private static bool Overlap(RectI a, RectI b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    private static bool Touch(RectI a, RectI b) =>
        (a.Right == b.X || b.Right == a.X) && a.Y < b.Bottom && b.Y < a.Bottom
        || (a.Bottom == b.Y || b.Bottom == a.Y) && a.X < b.Right && b.X < a.Right;
}
