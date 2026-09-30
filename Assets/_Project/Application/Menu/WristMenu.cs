using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Menu
{
    public enum MenuItemId
    {
        Undo,
        Redo,
        ToggleMode,
        Recenter,
        AlignPosture,
        ToggleGrid,
        AddTestScreen,
        ReturnToSurface,
        RemoveScreen,
    }

    /// <summary>Ações do menu de pulso (spec 2B §5). Tudo vira Command (ou undo/redo) no bus.</summary>
    public sealed class WristMenu
    {
        public static readonly MenuItemId[] Items =
        {
            MenuItemId.Undo, MenuItemId.Redo, MenuItemId.ToggleMode, MenuItemId.Recenter, MenuItemId.AlignPosture,
            MenuItemId.ToggleGrid, MenuItemId.AddTestScreen, MenuItemId.ReturnToSurface, MenuItemId.RemoveScreen,
        };

        private readonly WorkspaceStore _store;
        private readonly ICommandBus _bus;
        private readonly IHeadPoseSource _head;
        private int _testScreens;
        private Workspace _statusFor;
        private string _status = string.Empty;

        public WristMenu(WorkspaceStore store, ICommandBus bus, IHeadPoseSource head)
        {
            _store = store;
            _bus = bus;
            _head = head;
        }

        /// <summary>Recalculada só quando o estado muda (sem alocar por quadro).</summary>
        public string StatusLine
        {
            get
            {
                var ws = _store.State;
                if (ReferenceEquals(ws, _statusFor)) return _status;
                _statusFor = ws;
                int n = ws.Screens.Count;
                _status = (ws.Surface.Mode == LayoutMode.Cylinder ? "Cilindro" : "Esfera") + " · " + n + (n == 1 ? " tela" : " telas");
                return _status;
            }
        }

        public bool IsEnabled(MenuItemId id)
        {
            var ws = _store.State;
            switch (id)
            {
                case MenuItemId.Undo: return _store.CanUndo;
                case MenuItemId.Redo: return _store.CanRedo;
                case MenuItemId.RemoveScreen: return ws.Contains(ws.FocusedScreenId);
                case MenuItemId.ReturnToSurface:
                    return ws.TryGetScreen(ws.FocusedScreenId, out var focused) && focused.Placement.IsLoose;
                default: return true;
            }
        }

        public string Label(MenuItemId id)
        {
            var surface = _store.State.Surface;
            switch (id)
            {
                case MenuItemId.Undo: return "Desfazer";
                case MenuItemId.Redo: return "Refazer";
                case MenuItemId.ToggleMode: return surface.Mode == LayoutMode.Cylinder ? "Cilindro → Esfera" : "Esfera → Cilindro";
                case MenuItemId.Recenter: return "Recentrar";
                case MenuItemId.AlignPosture: return "Alinhar à postura";
                case MenuItemId.ToggleGrid: return surface.Snap.AngularGrid ? "Grade: ligada" : "Grade: desligada";
                case MenuItemId.AddTestScreen: return "+ Tela de teste";
                case MenuItemId.ReturnToSurface: return "Voltar à superfície";
                case MenuItemId.RemoveScreen: return "Remover tela";
                default: return id.ToString();
            }
        }

        public void Invoke(MenuItemId id)
        {
            if (!IsEnabled(id)) return;
            var ws = _store.State;
            switch (id)
            {
                case MenuItemId.Undo:
                    _bus.Undo();
                    break;
                case MenuItemId.Redo:
                    _bus.Redo();
                    break;
                case MenuItemId.ToggleMode:
                    _bus.Send(new SetLayoutMode(ws.Surface.Mode == LayoutMode.Cylinder ? LayoutMode.Sphere : LayoutMode.Cylinder));
                    break;
                case MenuItemId.Recenter:
                    var head = _head.HeadPose;
                    _bus.Send(new RecenterLayout(head.Position, LayoutSurface.HeadingOf(head.Rotation)));
                    break;
                case MenuItemId.AlignPosture:
                    _bus.Send(new AlignSurfaceToPosture(_head.HeadPose));
                    break;
                case MenuItemId.ToggleGrid:
                    _bus.Send(new SetSnapOptions(ws.Surface.Snap.WithAngularGrid(!ws.Surface.Snap.AngularGrid)));
                    break;
                case MenuItemId.AddTestScreen:
                    _bus.Send(TestScreens.Next(_testScreens++));
                    break;
                case MenuItemId.ReturnToSurface:
                    _bus.Send(new ReturnScreenToSurface(ws.FocusedScreenId));
                    break;
                case MenuItemId.RemoveScreen:
                    _bus.Send(new RemoveScreen(ws.FocusedScreenId));
                    break;
            }
        }
    }
}
