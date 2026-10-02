using ScreenPortal.App.Connection;
using ScreenPortal.Host.Domain.Display;

namespace ScreenPortal.Host.Transport.Control;

/// <summary>Monitor do Windows para o roteador. <paramref name="ScreenKey"/> 0 = não é tela do Quest (monitor do PC).</summary>
public readonly record struct RouterMonitor(string Name, RectI Bounds, uint ScreenKey);

/// <summary>Pedido de <c>SetCursorPos</c> (coordenadas do desktop).</summary>
public readonly record struct CursorWarp(int X, int Y);

/// <summary>
/// Decide o que fazer com cada evento do mouse (spec 3B §2), puro. <b>Tela</b>: o Windows recebe tudo; ao sair de uma
/// tela do Quest para fora (vizinha do Quest, nada, ou chegando à borda sem vizinho) entra no <b>Mundo</b> na hora, sem
/// resistência. No Mundo/Edição o cursor fica estacionado no centro da tela de saída (monitor virtual: invisível) e cada
/// movimento é engolido e somado como <c>ponto − centro</c> — nunca preso por borda, com a aceleração do Windows.
/// Arraste em andamento (botão passado ao Windows) nunca sai, e a soltura sempre chega ao Windows.
/// </summary>
public sealed class MouseRouter
{
    private IReadOnlyList<RouterMonitor> _monitors = Array.Empty<RouterMonitor>();
    private IReadOnlyList<RouterMonitor> _windows = Array.Empty<RouterMonitor>();
    private RouterMonitor? _window; // 3D: espelho de janela onde o cursor entrou (só se entra tocando com o cursor 3D)
    private bool _connected;
    private bool _hasLast;
    private int _lastX, _lastY;
    private int _parkX, _parkY;
    private RectI _exitBounds;
    private byte _passedDown;

    public MouseMode Mode { get; private set; } = MouseMode.Screen;
    public uint Epoch { get; private set; }
    public uint ExitScreenKey { get; private set; }
    public int ExitX { get; private set; }
    public int ExitY { get; private set; }
    public int Dx { get; private set; }
    public int Dy { get; private set; }
    public int Wheel { get; private set; }
    public byte Buttons { get; private set; }

    public bool Swallowing => _connected && (Mode == MouseMode.World || Mode == MouseMode.Edit);

    /// <summary>3D: o Windows está movendo uma janela (arraste pela barra de título)? Só consultado na borda.</summary>
    public Func<bool>? IsMovingWindow { get; set; }

    /// <summary>3D: a última saída para o Mundo foi arrastando uma janela (o host a destaca ali). Lido e zerado pelo host.</summary>
    public bool LeftDraggingWindow { get; private set; }

    public bool TakeLeftDraggingWindow()
    {
        bool v = LeftDraggingWindow;
        LeftDraggingWindow = false;
        return v;
    }

    /// <summary>Espelho de janela em que o cursor está (0 = nenhum) e o retângulo dele no desktop.</summary>
    public uint WindowKey => _window?.ScreenKey ?? 0;
    public RectI WindowBounds => _window?.Bounds ?? default;

    /// <summary>3D: retângulos das janelas espelhadas (chave = a tela-espelho). Atualiza o espelho ativo.</summary>
    public void SetWindows(IReadOnlyList<RouterMonitor> windows)
    {
        _windows = windows;
        if (_window is not { } active) return;
        _window = null;
        foreach (var w in windows) if (w.ScreenKey == active.ScreenKey) _window = w;
    }

    public void SetMonitors(IReadOnlyList<RouterMonitor> monitors) => _monitors = monitors;

    /// <summary>Headset ligado/desligado. Desligar no Mundo/Edição devolve o cursor ao ponto de saída.</summary>
    public CursorWarp? SetConnected(bool connected)
    {
        _connected = connected;
        if (!connected) _window = null;
        if (connected || Mode == MouseMode.Screen || Mode == MouseMode.Outside) return null;
        var warp = new CursorWarp(_exitBounds.X + ExitX, _exitBounds.Y + ExitY);
        BackToScreen(warp.X, warp.Y);
        return warp;
    }

    /// <returns>true = engolir o evento.</returns>
    public bool OnMove(int x, int y, out CursorWarp? warp)
    {
        warp = null;
        if (Swallowing)
        {
            if (x == _parkX && y == _parkY) return true; // eco do estacionamento
            Dx += x - _parkX;
            Dy += y - _parkY;
            return true;
        }
        if (!_connected || !_hasLast)
        {
            Track(x, y);
            return false;
        }
        if (_window is { } mirror && _passedDown == 0)
        {
            var wb = mirror.Bounds;
            if (x >= wb.X && x < wb.Right && y >= wb.Y && y < wb.Bottom)
            {
                Track(x, y);
                return false;
            }
            // saiu do retângulo da janela: Mundo, mesmo que o Windows tenha monitor ali (spec 3D §4)
            int wx = Math.Clamp(x, wb.X, wb.Right - 1), wy = Math.Clamp(y, wb.Y, wb.Bottom - 1);
            warp = Leave(MouseMode.World, mirror, wx, wy);
            Dx = x - wx;
            Dy = y - wy;
            return true;
        }
        var from = MonitorAt(_lastX, _lastY);
        if (from is not { ScreenKey: not 0 } current)
        {
            Track(x, y);
            return false;
        }
        bool draggingWindow = false;
        if (_passedDown != 0)
        {
            // arraste nunca sai — exceto arrastar uma janela pela barra de título para o vazio (spec 3D §1, item 5)
            bool leaving = !(x >= current.Bounds.X && x < current.Bounds.Right && y >= current.Bounds.Y && y < current.Bounds.Bottom)
                           || ReachedFreeEdge(current.Bounds, x, y);
            if (!leaving || IsMovingWindow?.Invoke() != true)
            {
                Track(x, y);
                return false;
            }
            draggingWindow = true;
        }
        var b = current.Bounds;
        var target = MonitorAt(x, y);
        bool inside = x >= b.X && x < b.Right && y >= b.Y && y < b.Bottom;
        if (!inside && target is { ScreenKey: 0 })
        {
            Track(x, y); // monitor do PC fora do Quest: o Windows cuida
            return false;
        }
        if (inside && !ReachedFreeEdge(b, x, y))
        {
            Track(x, y);
            return false;
        }
        int ex = Math.Clamp(x, b.X, b.Right - 1), ey = Math.Clamp(y, b.Y, b.Bottom - 1);
        warp = Leave(MouseMode.World, current, ex, ey);
        Dx = x - ex;
        Dy = y - ey;
        if (draggingWindow)
        {
            Buttons = _passedDown; // o botão continua segurado no mundo
            LeftDraggingWindow = true;
        }
        return true;
    }

    /// <summary>Chegou à borda (pixel extremo) movendo para fora, num lado sem monitor vizinho ali.</summary>
    private bool ReachedFreeEdge(RectI b, int x, int y)
    {
        bool same = x == _lastX && y == _lastY; // empurrão puro contra a borda
        if (x == b.Right - 1 && (x > _lastX || same) && MonitorAt(b.Right, y) == null) return true;
        if (x == b.X && (x < _lastX || same) && MonitorAt(b.X - 1, y) == null) return true;
        if (y == b.Bottom - 1 && (y > _lastY || same) && MonitorAt(x, b.Bottom) == null) return true;
        if (y == b.Y && (y < _lastY || same) && MonitorAt(x, b.Y - 1) == null) return true;
        return false;
    }

    private CursorWarp Leave(MouseMode mode, RouterMonitor from, int ex, int ey)
    {
        _window = null;
        Mode = mode;
        Epoch++;
        ExitScreenKey = from.ScreenKey;
        _exitBounds = from.Bounds;
        ExitX = ex - from.Bounds.X;
        ExitY = ey - from.Bounds.Y;
        Dx = Dy = Wheel = 0;
        _parkX = from.Bounds.X + from.Bounds.Width / 2;
        _parkY = from.Bounds.Y + from.Bounds.Height / 2;
        return new CursorWarp(_parkX, _parkY);
    }

    /// <summary>Botão (bit de <see cref="MouseState.ButtonLeft"/>…). true = engolir.</summary>
    public bool OnButton(byte button, bool down)
    {
        if (!down && (_passedDown & button) != 0)
        {
            _passedDown &= (byte)~button; // a soltura de um clique que o Windows viu sempre chega a ele
            Buttons &= (byte)~button;
            return false;
        }
        if (Swallowing)
        {
            Buttons = down ? (byte)(Buttons | button) : (byte)(Buttons & ~button);
            return true;
        }
        if (down) _passedDown |= button;
        return false;
    }

    /// <summary>Roda (unidades de 120). true = engolir (no Mundo/Edição é somada).</summary>
    public bool OnWheel(int delta)
    {
        if (!Swallowing) return false;
        Wheel += delta;
        return true;
    }

    /// <summary>Modo edição: engole tudo. Ligar a partir da tela parte do ponto do cursor; desligar volta ao Mundo.</summary>
    public CursorWarp? SetEditMode(bool on)
    {
        if (!_connected) return null;
        if (!on)
        {
            if (Mode != MouseMode.Edit) return null;
            Mode = MouseMode.World; // o headset entra na tela sob o cursor 3D (CursorEnter) ou o cursor segue no mundo
            Epoch++;
            ExitScreenKey = 0; // continua de onde o cursor 3D está
            Dx = Dy = Wheel = 0;
            Buttons = 0;
            return null;
        }
        if (Mode == MouseMode.Edit) return null;
        if (Mode == MouseMode.World)
        {
            Mode = MouseMode.Edit;
            Epoch++;
            ExitScreenKey = 0;
            Dx = Dy = Wheel = 0;
            return null;
        }
        if (_window is { } inMirror) return Leave(MouseMode.Edit, inMirror, _lastX, _lastY);
        if (_hasLast && MonitorAt(_lastX, _lastY) is { ScreenKey: not 0 } under)
            return Leave(MouseMode.Edit, under, _lastX, _lastY); // o cursor 3D nasce onde o cursor estava
        if (FirstQuestScreen() is not { } q) return null;
        // começando de um monitor do PC: estaciona numa tela do Quest (invisível) e nasce no centro dela
        return Leave(MouseMode.Edit, q, q.Bounds.X + q.Bounds.Width / 2, q.Bounds.Y + q.Bounds.Height / 2);
    }

    /// <summary>O cursor 3D tocou a tela (spec 3B §4). Só vale no Mundo.</summary>
    public CursorWarp? Enter(uint screenKey, int x, int y)
    {
        if (!_connected || Mode != MouseMode.World) return null;
        foreach (var w in _windows)
        {
            if (w.ScreenKey != screenKey) continue;
            var wb = w.Bounds;
            var into = new CursorWarp(wb.X + Math.Clamp(x, 0, wb.Width - 1), wb.Y + Math.Clamp(y, 0, wb.Height - 1));
            BackToScreen(into.X, into.Y);
            _window = w;
            return into;
        }
        foreach (var m in _monitors)
        {
            if (m.ScreenKey != screenKey) continue;
            var b = m.Bounds;
            var warp = new CursorWarp(b.X + Math.Clamp(x, 0, b.Width - 1), b.Y + Math.Clamp(y, 0, b.Height - 1));
            BackToScreen(warp.X, warp.Y);
            return warp;
        }
        return null;
    }

    private void BackToScreen(int x, int y)
    {
        Mode = MouseMode.Screen;
        Epoch++;
        Dx = Dy = Wheel = 0;
        Buttons = 0;
        Track(x, y);
    }

    private void Track(int x, int y)
    {
        _lastX = x;
        _lastY = y;
        _hasLast = true;
    }

    private RouterMonitor? MonitorAt(int x, int y)
    {
        foreach (var m in _monitors)
            if (x >= m.Bounds.X && x < m.Bounds.Right && y >= m.Bounds.Y && y < m.Bounds.Bottom) return m;
        return null;
    }

    private RouterMonitor? FirstQuestScreen()
    {
        foreach (var m in _monitors) if (m.ScreenKey != 0) return m;
        return null;
    }
}
