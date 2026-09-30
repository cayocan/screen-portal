using ScreenPortal.App.Menu;

namespace ScreenPortal.App.Manipulation
{
    /// <summary>
    /// Um quadro de entrada: lê os dois ponteiros, trata atalhos (X/Y), dá prioridade ao menu de pulso (o raio que
    /// está no menu não manipula telas atrás dele) e alimenta o controlador. Sem alocação por quadro.
    /// </summary>
    public sealed class InteractionRouter
    {
        private readonly IManipulationInput _right;
        private readonly IManipulationInput _left;
        private readonly ScreenManipulationController _manipulation;
        private readonly WristMenu _menu;
        private readonly IWristMenuPanel _panel;
        private readonly ICommandBus _bus;
        private bool _prevRightSelect;
        private bool _prevPanelVisible;

        public ManipulationInput LastRight { get; private set; }
        public ManipulationInput LastLeft { get; private set; }
        public int MenuHover { get; private set; } = -1;

        public InteractionRouter(IManipulationInput right, IManipulationInput left, ScreenManipulationController manipulation,
            WristMenu menu, IWristMenuPanel panel, ICommandBus bus)
        {
            _right = right ?? NullManipulationInput.Instance;
            _left = left ?? NullManipulationInput.Instance;
            _manipulation = manipulation;
            _menu = menu;
            _panel = panel;
            _bus = bus;
        }

        public void Tick()
        {
            if (!_right.Read(out var right)) right = default;
            if (!_left.Read(out var left)) left = default;
            LastRight = right;
            LastLeft = left;

            if (!_manipulation.IsDragging)
            {
                if (left.UndoPressed) _bus.Undo();
                else if (left.RedoPressed) _bus.Redo();
            }

            MenuHover = -1;
            bool rightSelect = right.SelectHeld;
            bool panelVisible = _panel != null && _panel.Visible;
            if (!panelVisible && _prevPanelVisible) _menu.OnHidden();
            _prevPanelVisible = panelVisible;
            int count = _menu.ItemCount;
            if (panelVisible && !_manipulation.IsDragging && right.HasRay
                && ScreenHitTest.Raycast(right.Origin, right.Direction, _panel.Pose, MenuLayout.PanelWidth, MenuLayout.PanelHeight(count),
                    out _, out float lx, out float ly))
            {
                MenuHover = MenuLayout.ButtonAt(lx, ly, count);
                if (MenuHover >= 0 && rightSelect && !_prevRightSelect) _menu.Invoke(MenuHover);
                right.HasRay = false; // SelectHeld continua: o controlador só pega a tela num gatilho novo
            }
            _prevRightSelect = rightSelect;

            _manipulation.Update(in right, in left);
            _panel?.Render(_menu, MenuHover);
        }
    }
}
