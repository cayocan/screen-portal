using ScreenPortal.Host.Domain.Display;

namespace ScreenPortal.Host.Tests.Screens;

public class WindowPlanTests
{
    private static readonly RectI From = new(2560, 0, 2560, 1440);
    private static readonly RectI To = new(5120, 0, 1920, 1080);

    [Fact]
    public void Normal_MapsTheWindowRect_AndRestoreFollowsInWorkspaceCoordinates()
    {
        var window = new WindowSnapshot(WindowShowState.Normal, new RectI(3040, 450, 960, 540), default);

        var move = WindowRelocation.Plan(window, From, To, 0, 40);

        Assert.Equal(WindowShowState.Normal, move.State);
        Assert.Equal(WindowRelocation.Map(window.Rect, From, To), move.Rect);
        Assert.Equal(move.Rect with { Y = move.Rect.Y - 40 }, move.RestoreRect);
    }

    [Fact]
    public void Maximized_UsesTheRestoreRect_ConvertedFromWorkspaceCoordinates()
    {
        // Barra de tarefas no topo: área de trabalho começa em y = 40. Restauração (workspace) 3040,230 = tela 3040,270,
        // centro (3520, 540) = (37,5 %, 37,5 %) do monitor antigo.
        var window = new WindowSnapshot(WindowShowState.Maximized, From, new RectI(3040, 230, 960, 540));

        var move = WindowRelocation.Plan(window, From, To, 0, 40);

        Assert.Equal(WindowShowState.Maximized, move.State);
        Assert.Equal(new RectI(5360, 135, 960, 540), move.Rect);
        Assert.Equal(new RectI(5360, 95, 960, 540), move.RestoreRect);
    }

    [Fact]
    public void Minimized_IgnoresTheIconRect_AndMovesOnlyTheRestorePosition()
    {
        var window = new WindowSnapshot(WindowShowState.Minimized, new RectI(-32000, -32000, 160, 28), new RectI(3040, 270, 960, 540));

        var move = WindowRelocation.Plan(window, From, To, 0, 0);

        Assert.Equal(WindowShowState.Minimized, move.State);
        Assert.Equal(new RectI(5360, 135, 960, 540), move.RestoreRect);
        Assert.Equal(move.RestoreRect, move.Rect);
    }

    [Fact]
    public void Maximized_LargerThanTheNewMonitor_ShrinksTheRestoreRect()
    {
        var window = new WindowSnapshot(WindowShowState.Maximized, From, new RectI(2560, 0, 2400, 1400));

        var move = WindowRelocation.Plan(window, From, To, 0, 0);

        Assert.True(move.RestoreRect.Width <= To.Width && move.RestoreRect.Height <= To.Height);
        Assert.True(move.RestoreRect.X >= To.X && move.RestoreRect.Right <= To.Right);
    }
}
