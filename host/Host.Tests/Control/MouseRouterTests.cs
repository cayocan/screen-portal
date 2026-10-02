using ScreenPortal.App.Connection;
using ScreenPortal.Host.Domain.Display;
using ScreenPortal.Host.Transport.Control;

namespace ScreenPortal.Host.Tests.Control;

public class MouseRouterTests
{
    // Monitor do PC (0..1920), tela 1 do Quest à direita (1920..4480), tela 2 do Quest acima da 1, sem nada à direita da 1.
    private static readonly RouterMonitor Pc = new("\\\\.\\DISPLAY1", new RectI(0, 0, 1920, 1080), 0);
    private static readonly RouterMonitor Q1 = new("\\\\.\\DISPLAY60", new RectI(1920, 0, 2560, 1440), 1);
    private static readonly RouterMonitor Q2 = new("\\\\.\\DISPLAY61", new RectI(1920, -1440, 2560, 1440), 2);

    private static MouseRouter Router(params RouterMonitor[] monitors)
    {
        var r = new MouseRouter();
        r.SetMonitors(monitors.Length > 0 ? monitors : new[] { Pc, Q1, Q2 });
        r.SetConnected(true);
        return r;
    }

    private static void At(MouseRouter r, int x, int y) => r.OnMove(x, y, out _);

    [Fact]
    public void InsideAScreen_EverythingGoesToWindows()
    {
        var r = Router();
        At(r, 2500, 500);
        Assert.False(r.OnMove(2510, 505, out var warp));
        Assert.Null(warp);
        Assert.False(r.OnButton(MouseState.ButtonLeft, true));
        Assert.False(r.OnWheel(120));
        Assert.Equal(MouseMode.Screen, r.Mode);
    }

    [Fact]
    public void ReachingAFreeEdge_LeavesAtOnce_AndParksAtTheCenter()
    {
        var r = Router();
        At(r, 4470, 700);
        Assert.True(r.OnMove(4479, 700, out var warp)); // borda direita da tela 1, nada além
        Assert.Equal(MouseMode.World, r.Mode);
        Assert.Equal(1u, r.ExitScreenKey);
        Assert.Equal((2559, 700), (r.ExitX, r.ExitY));
        Assert.Equal(new CursorWarp(1920 + 1280, 720), warp);
    }

    [Fact]
    public void GoingIntoAnotherQuestScreen_PassesThroughTheWorld()
    {
        var r = Router();
        At(r, 2500, 3);
        Assert.True(r.OnMove(2500, -4, out _)); // a tela 2 está colada acima no Windows
        Assert.Equal(MouseMode.World, r.Mode);
        Assert.Equal((580, 0), (r.ExitX, r.ExitY));
        Assert.Equal((0, -4), (r.Dx, r.Dy)); // o que passou da borda já conta
    }

    [Fact]
    public void GoingIntoAPcMonitor_IsLeftToWindows()
    {
        var r = Router();
        At(r, 1925, 500);
        Assert.False(r.OnMove(1915, 500, out _));
        Assert.Equal(MouseMode.Screen, r.Mode);
        Assert.False(r.OnMove(1000, 500, out _), "no monitor do PC o Windows manda");
        Assert.False(r.OnMove(0, 500, out _), "borda livre de monitor do PC não sai para o mundo");
    }

    [Fact]
    public void SlidingAlongAFreeEdge_DoesNotLeave()
    {
        var r = Router();
        At(r, 4479, 700);
        r = Router();
        At(r, 4479, 700); // já na borda (chegou por outro caminho)
        Assert.False(r.OnMove(4479, 760, out _));
        Assert.Equal(MouseMode.Screen, r.Mode);
        Assert.True(r.OnMove(4479, 760, out _), "empurrão puro contra a borda sai");
    }

    [Fact]
    public void InTheWorld_MovesAreSwallowed_AndSummedFromThePark()
    {
        var r = Router();
        At(r, 4470, 700);
        r.OnMove(4479, 700, out var park);
        Assert.True(r.OnMove(park!.Value.X, park.Value.Y, out _), "eco do SetCursorPos");
        Assert.True(r.OnMove(park.Value.X + 15, park.Value.Y - 4, out _));
        Assert.True(r.OnMove(park.Value.X + 5, park.Value.Y, out _));
        Assert.Equal((20, -4), (r.Dx, r.Dy));
        Assert.True(r.OnButton(MouseState.ButtonLeft, true), "clique no vazio não chega a nada");
        Assert.True(r.OnWheel(-120));
    }

    [Fact]
    public void Enter_PutsTheCursorOnThePixel_AndGivesTheMouseBack()
    {
        var r = Router();
        At(r, 4470, 700);
        r.OnMove(4479, 700, out _);
        uint epoch = r.Epoch;
        var warp = r.Enter(2, 100, 1439);
        Assert.Equal(new CursorWarp(2020, -1), warp);
        Assert.Equal(MouseMode.Screen, r.Mode);
        Assert.True(r.Epoch > epoch);
        Assert.False(r.OnMove(2021, -2, out _));
        Assert.Null(r.Enter(1, 0, 0)); // já na tela: ignora
    }

    [Fact]
    public void DraggingWithAButtonHeld_NeverLeaves_AndTheReleaseReachesWindows()
    {
        var r = Router();
        At(r, 4470, 700);
        Assert.False(r.OnButton(MouseState.ButtonLeft, true));
        Assert.False(r.OnMove(4479, 700, out _));
        Assert.False(r.OnMove(4479, 700, out _));
        Assert.Equal(MouseMode.Screen, r.Mode);
        Assert.False(r.OnButton(MouseState.ButtonLeft, false));
        Assert.True(r.OnMove(4479, 700, out _), "solto, o empurrão sai");
        Assert.True(r.OnButton(MouseState.ButtonRight, false), "no mundo, soltura de um clique que o Windows não viu é engolida");
    }

    [Fact]
    public void ReleaseOfAClickStartedOnScreen_StillReachesWindows_FromTheWorld()
    {
        var r = Router();
        At(r, 2500, 500);
        r.OnButton(MouseState.ButtonMiddle, true);
        r.OnButton(MouseState.ButtonMiddle, false);
        At(r, 4470, 700);
        r.OnMove(4479, 700, out _);
        Assert.True(r.OnButton(MouseState.ButtonLeft, true));
        Assert.True(r.OnButton(MouseState.ButtonLeft, false), "clique inteiro no mundo: engolido");
    }

    [Fact]
    public void LosingTheHeadset_GivesTheCursorBackAtTheExitPoint()
    {
        var r = Router();
        At(r, 4470, 700);
        r.OnMove(4479, 700, out _);
        Assert.Equal(new CursorWarp(4479, 700), r.SetConnected(false));
        Assert.Equal(MouseMode.Screen, r.Mode);
        Assert.False(r.OnMove(4400, 700, out _), "sem headset nada é interceptado");
        Assert.False(r.OnMove(4479, 700, out _));
    }

    [Fact]
    public void WithoutHeadset_NothingIsIntercepted()
    {
        var r = new MouseRouter();
        r.SetMonitors(new[] { Pc, Q1 });
        At(r, 4470, 700);
        Assert.False(r.OnMove(4479, 700, out _));
        Assert.Null(r.SetEditMode(true));
    }

    [Fact]
    public void EditMode_SwallowsEverything_StartsAtTheCursor_AndOffGoesToTheWorld()
    {
        var r = Router();
        At(r, 2500, 500);
        var park = r.SetEditMode(true);
        Assert.Equal(new CursorWarp(3200, 720), park);
        Assert.Equal(MouseMode.Edit, r.Mode);
        Assert.Equal((1u, 580, 500), (r.ExitScreenKey, r.ExitX, r.ExitY));
        Assert.True(r.OnMove(3210, 720, out _));
        Assert.True(r.OnButton(MouseState.ButtonRight, true));
        Assert.Equal(MouseState.ButtonRight, r.Buttons);
        Assert.True(r.OnWheel(240));
        Assert.Equal(240, r.Wheel);
        r.SetEditMode(false);
        Assert.Equal(MouseMode.World, r.Mode);
        Assert.Equal(0u, r.ExitScreenKey); // continua de onde o cursor 3D está
        Assert.Equal(0, r.Buttons);
    }

    [Fact]
    public void EditMode_FromAPcMonitor_ParksOnAQuestScreen()
    {
        var r = Router();
        At(r, 500, 500);
        var park = r.SetEditMode(true);
        Assert.Equal(new CursorWarp(3200, 720), park);
        Assert.Equal((1u, 1280, 720), (r.ExitScreenKey, r.ExitX, r.ExitY));
    }
}

public class MouseRouterWindowTests
{
    // a janela espelhada (tela 7) fica fisicamente na tela 1 do Quest
    private static readonly RouterMonitor Q1 = new("DISPLAY60", new RectI(1920, 0, 2560, 1440), 1);
    private static readonly RouterMonitor Win = new("janela 0x1", new RectI(2200, 200, 1096, 824), 7);

    private static MouseRouter Router()
    {
        var r = new MouseRouter();
        r.SetMonitors(new[] { Q1 });
        r.SetWindows(new[] { Win });
        r.SetConnected(true);
        r.OnMove(4470, 700, out _);
        r.OnMove(4479, 700, out _); // sai para o mundo pela borda direita da tela 1
        return r;
    }

    [Fact]
    public void MovingOverTheRealWindow_DoesNotJumpToTheMirror()
    {
        var r = new MouseRouter();
        r.SetMonitors(new[] { Q1 });
        r.SetWindows(new[] { Win });
        r.SetConnected(true);
        r.OnMove(2100, 300, out _);
        Assert.False(r.OnMove(2300, 300, out _));
        Assert.Equal(0u, r.WindowKey);
    }

    [Fact]
    public void TouchingTheMirror_EntersTheWindow_AtThePoint()
    {
        var r = Router();
        var warp = r.Enter(7, 100, 50);
        Assert.Equal(new CursorWarp(2300, 250), warp);
        Assert.Equal(MouseMode.Screen, r.Mode);
        Assert.Equal(7u, r.WindowKey);
        Assert.False(r.OnMove(2400, 300, out _), "dentro da janela: o Windows recebe");
    }

    [Fact]
    public void LeavingTheWindowRectangle_GoesToTheWorld_EvenOverAQuestScreen()
    {
        var r = Router();
        r.Enter(7, 100, 50);
        r.OnMove(2210, 300, out _);
        Assert.True(r.OnMove(2190, 300, out _), "à esquerda da janela ainda é a tela 1 no Windows, mas sai do espelho");
        Assert.Equal(MouseMode.World, r.Mode);
        Assert.Equal(7u, r.ExitScreenKey);
        Assert.Equal((0, 100), (r.ExitX, r.ExitY));
        Assert.Equal(0u, r.WindowKey);
    }

    [Fact]
    public void DraggingInsideTheMirror_DoesNotLeave()
    {
        var r = Router();
        r.Enter(7, 100, 50);
        r.OnButton(MouseState.ButtonLeft, true);
        Assert.False(r.OnMove(2100, 300, out _), "arraste (seleção) passa da borda sem sair");
    }

    [Fact]
    public void WindowMovedOrClosed_UpdatesTheActiveMirror()
    {
        var r = Router();
        r.Enter(7, 100, 50);
        r.SetWindows(new[] { Win with { Bounds = new RectI(2000, 100, 1096, 824) } });
        Assert.Equal(2000, r.WindowBounds.X);
        r.SetWindows(Array.Empty<RouterMonitor>());
        Assert.Equal(0u, r.WindowKey);
    }
}
