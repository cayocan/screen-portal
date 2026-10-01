using System;
using System.Collections.Generic;
using ScreenPortal.App.Connection;
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
        AddVirtual,
        AddMirror,
        ReturnToSurface,
        RemoveScreen,
        AddTestScreen,
        Curvature,
        Recreate,
        FrameRate,
        Audio,
        AutoArrange,
    }

    public enum MenuPage
    {
        Main,
        Aspects,
        Resolutions,
        Mirrors,
        Curvature,
        FrameRate,
        Audio,
    }

    /// <summary>
    /// Menu de pulso (spec 2B §5, 2C §3) com páginas: principal → proporções (frames) → resoluções, espelhos e
    /// curvatura (2E); o modo recriar (2D §3) reusa proporções → resoluções para a tela em foco.
    /// Tudo vira Command (ou undo/redo) no bus. Itens por índice; sem alocação por quadro (rótulos em cache).
    /// </summary>
    public sealed class WristMenu
    {
        public const int MaxItems = 16;
        public const string PlaybackOn = "Som do PC: ligado";
        public const string PlaybackOff = "Som do PC: desligado";
        public const string MicOn = "Microfone: ligado";
        public const string MicOff = "Microfone: mudo";
        public const string AutoArrangeOn = "Arranjo automático: ligado";
        public const string AutoArrangeOff = "Arranjo automático: desligado";
        public const int ComfortableVideoScreens = 4;
        public const string TooManyNotice = "Mais de 4 telas:\npode haver quedas e travamentos";
        public const string BackLabel = "← Voltar";
        public const string RecreateLabel = "Recriar ▸";
        public const string RecreateTitlePrefix = "Recriar: ";
        public const string CurrentSuffix = " · atual";

        private static readonly MenuItemId[] MainItems =
        {
            MenuItemId.Undo, MenuItemId.Redo, MenuItemId.ToggleMode, MenuItemId.Recenter, MenuItemId.AlignPosture,
            MenuItemId.ToggleGrid, MenuItemId.AddVirtual, MenuItemId.AddMirror, MenuItemId.ReturnToSurface, MenuItemId.RemoveScreen,
            MenuItemId.Curvature, MenuItemId.Recreate, MenuItemId.FrameRate, MenuItemId.Audio, MenuItemId.AutoArrange,
        };

        private static readonly MenuItemId[] MainItemsWithTest =
        {
            MenuItemId.Undo, MenuItemId.Redo, MenuItemId.ToggleMode, MenuItemId.Recenter, MenuItemId.AlignPosture,
            MenuItemId.ToggleGrid, MenuItemId.AddVirtual, MenuItemId.AddMirror, MenuItemId.ReturnToSurface, MenuItemId.RemoveScreen,
            MenuItemId.Curvature, MenuItemId.Recreate, MenuItemId.FrameRate, MenuItemId.Audio, MenuItemId.AutoArrange,
            MenuItemId.AddTestScreen,
        };

        private readonly WorkspaceStore _store;
        private readonly ICommandBus _bus;
        private readonly IHeadPoseSource _head;
        private readonly IHostCatalog _host;
        private readonly MenuItemId[] _main;
        private readonly CurvatureMenu _curvature = new CurvatureMenu();
        private readonly FrameRateMenu _frameRate = new FrameRateMenu();
        private readonly ScreenPortal.App.Screens.RateSync _rates;
        private readonly ScreenPortal.App.Audio.AudioSync _audio;
        private readonly ScreenPortal.App.Screens.LayoutSyncSettings _arrange;
        private int _testScreens;
        private int _aspect;
        private string _notice = string.Empty;

        private Workspace _statusFor;
        private bool _statusConnected;
        private string _statusNotice;
        private string _status = string.Empty;

        private IReadOnlyList<MonitorDescriptor> _mirrorLabelsFor;
        private string[] _mirrorLabels = Array.Empty<string>();

        // Modo recriar (spec 2D §3): tela alvo fixada ao entrar; textos montados uma vez (o painel lê a cada quadro).
        private ScreenId _recreate = ScreenId.None;
        private Resolution _recreateFrom;
        private string _recreateTitle = string.Empty;
        private string _recreateCurrentLabel = string.Empty;

        public bool IsRecreating => !_recreate.IsNone;

        public MenuPage Page { get; private set; } = MenuPage.Main;
        public Orientation Orientation { get; private set; } = Orientation.Landscape;

        public WristMenu(WorkspaceStore store, ICommandBus bus, IHeadPoseSource head, IHostCatalog host = null, bool showTestScreens = false,
            ScreenPortal.App.Screens.RateSync rates = null, ScreenPortal.App.Audio.AudioSync audio = null,
            ScreenPortal.App.Screens.LayoutSyncSettings arrange = null)
        {
            _arrange = arrange;
            _rates = rates;
            _audio = audio;
            _store = store;
            _bus = bus;
            _head = head;
            _host = host ?? new FixedHostCatalog(false, Array.Empty<MonitorDescriptor>());
            _main = showTestScreens ? MainItemsWithTest : MainItems;
        }

        public int ItemCount
        {
            get
            {
                switch (Page)
                {
                    case MenuPage.Aspects: return ResolutionCatalog.AspectCount + 2; // orientação + proporções + voltar
                    case MenuPage.Resolutions: return ResolutionCatalog.Count(_aspect) + 1;
                    case MenuPage.Mirrors: return MirrorCount + 1;
                    case MenuPage.Curvature: return CurvatureMenu.ItemCount + 1;
                    case MenuPage.FrameRate: return FrameRateMenu.ItemCount + 1;
                    case MenuPage.Audio: return 3;
                    default: return _main.Length;
                }
            }
        }

        private int MirrorCount => Math.Min(_host.PhysicalMonitors.Count, MaxItems - 1);

        private bool IsBack(int index) => Page != MenuPage.Main && index == ItemCount - 1;

        public int IndexOf(MenuItemId id) => Array.IndexOf(_main, id);

        /// <summary>Recalculada só quando o estado, a conexão ou o aviso mudam (sem alocar por quadro).</summary>
        public string StatusLine
        {
            get
            {
                if (Page == MenuPage.Curvature) return _curvature.Status(_store.State);
                if (Page == MenuPage.FrameRate) return _frameRate.Status(_store.State, _rates);
                if (Page == MenuPage.Audio) return _audio != null ? _audio.Status : string.Empty;
                if (IsRecreating) return _recreateTitle;
                var ws = _store.State;
                bool connected = _host.IsConnected;
                if (ReferenceEquals(ws, _statusFor) && connected == _statusConnected && ReferenceEquals(_notice, _statusNotice))
                    return _status;
                _statusFor = ws;
                _statusConnected = connected;
                _statusNotice = _notice;
                if (_notice.Length > 0)
                {
                    _status = _notice;
                    return _status;
                }
                int n = ws.Screens.Count;
                _status = (ws.Surface.Mode == LayoutMode.Cylinder ? "Cilindro" : "Esfera") + " · " + n + (n == 1 ? " tela" : " telas")
                          + (connected ? string.Empty : " · Conecte ao PC");
                return _status;
            }
        }

        public string Label(int index)
        {
            if (IsBack(index)) return BackLabel;
            switch (Page)
            {
                case MenuPage.Aspects:
                    if (index == 0) return Orientation == Orientation.Landscape ? "Orientação: Paisagem" : "Orientação: Retrato";
                    return ResolutionCatalog.Name(index - 1, Orientation);
                case MenuPage.Resolutions:
                    if (IsCurrent(index)) return _recreateCurrentLabel;
                    return ResolutionCatalog.Label(_aspect, index, Orientation);
                case MenuPage.Mirrors:
                    return MirrorLabels()[index];
                case MenuPage.Curvature:
                    return CurvatureMenu.Label(index, FocusedCurvature());
                case MenuPage.FrameRate:
                    return FrameRateMenu.Label(index, IsCurrentFrameRate(index));
                case MenuPage.Audio:
                    return index == 0 ? (_audio.Settings.Playback ? PlaybackOn : PlaybackOff)
                        : _audio.Settings.Mic ? MicOn : MicOff;
                default:
                    return Label(_main[index]);
            }
        }

        private string Label(MenuItemId id)
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
                case MenuItemId.AddVirtual: return "+ Monitor virtual";
                case MenuItemId.AddMirror: return "+ Espelhar monitor";
                case MenuItemId.ReturnToSurface: return "Voltar à superfície";
                case MenuItemId.RemoveScreen: return "Remover tela";
                case MenuItemId.AddTestScreen: return "+ Tela de teste";
                case MenuItemId.Curvature: return "Curvatura ▸";
                case MenuItemId.FrameRate: return "Taxa de quadros ▸";
                case MenuItemId.Audio: return "Áudio ▸";
                case MenuItemId.AutoArrange: return _arrange != null && _arrange.Enabled ? AutoArrangeOn : AutoArrangeOff;
                case MenuItemId.Recreate: return RecreateLabel;
                default: return id.ToString();
            }
        }

        private string[] MirrorLabels()
        {
            var monitors = _host.PhysicalMonitors;
            if (ReferenceEquals(monitors, _mirrorLabelsFor)) return _mirrorLabels;
            _mirrorLabelsFor = monitors;
            _mirrorLabels = new string[Math.Min(monitors.Count, MaxItems - 1)];
            for (int i = 0; i < _mirrorLabels.Length; i++)
                _mirrorLabels[i] = monitors[i].FriendlyName + " · " + monitors[i].Width + "×" + monitors[i].Height;
            return _mirrorLabels;
        }

        public bool IsEnabled(int index)
        {
            if (IsBack(index)) return true;
            switch (Page)
            {
                case MenuPage.Aspects: return index == 0 || _host.IsConnected;
                case MenuPage.Resolutions: return _host.IsConnected && !IsCurrent(index);
                case MenuPage.Mirrors: return _host.IsConnected;
                case MenuPage.Curvature: return HasFocusedScreen();
                case MenuPage.FrameRate: return HasFocusedVideoScreen();
                case MenuPage.Audio: return _audio != null;
                default: return IsEnabled(_main[index]);
            }
        }

        private bool IsEnabled(MenuItemId id)
        {
            var ws = _store.State;
            switch (id)
            {
                case MenuItemId.Undo: return _store.CanUndo;
                case MenuItemId.Redo: return _store.CanRedo;
                case MenuItemId.RemoveScreen:
                case MenuItemId.Curvature:
                    return ws.Contains(ws.FocusedScreenId);
                case MenuItemId.ReturnToSurface:
                    return ws.TryGetScreen(ws.FocusedScreenId, out var focused) && focused.Placement.IsLoose;
                case MenuItemId.FrameRate:
                    return HasFocusedVideoScreen();
                case MenuItemId.Audio:
                    return _audio != null;
                case MenuItemId.AutoArrange:
                    return _arrange != null;
                case MenuItemId.AddVirtual:
                case MenuItemId.AddMirror:
                    return _host.IsConnected;
                case MenuItemId.Recreate:
                    return _host.IsConnected && ws.TryGetScreen(ws.FocusedScreenId, out var target)
                           && target.Source.Kind == ScreenSourceKind.Virtual;
                default: return true;
            }
        }

        /// <summary>Largura/altura do frame de exemplo desenhado no botão; 0 = botão comum.</summary>
        public float FrameAspect(int index) =>
            Page == MenuPage.Aspects && index >= 1 && index <= ResolutionCatalog.AspectCount
                ? ResolutionCatalog.FrameAspect(index - 1, Orientation)
                : 0f;

        public void Invoke(int index)
        {
            if (index < 0 || index >= ItemCount || !IsEnabled(index)) return;
            if (IsBack(index))
            {
                Page = Page == MenuPage.Resolutions ? MenuPage.Aspects : MenuPage.Main;
                if (Page == MenuPage.Main) ExitRecreate();
                return;
            }
            switch (Page)
            {
                case MenuPage.Aspects:
                    if (index == 0)
                    {
                        Orientation = Orientation == Orientation.Landscape ? Orientation.Portrait : Orientation.Landscape;
                        return;
                    }
                    _aspect = index - 1;
                    Page = MenuPage.Resolutions;
                    return;
                case MenuPage.Resolutions:
                {
                    var resolution = ResolutionCatalog.Get(_aspect, index, Orientation);
                    if (IsRecreating) Recreate(resolution);
                    else Create(resolution, ScreenSource.Virtual, ResolutionCatalog.Label(_aspect, index, Orientation));
                    return;
                }
                case MenuPage.Mirrors:
                {
                    var monitor = _host.PhysicalMonitors[index];
                    var resolution = ResolutionFit.FromCapture((int)monitor.Width, (int)monitor.Height);
                    if (resolution.IsValid) Create(resolution, ScreenSource.Mirror(monitor.Id), monitor.FriendlyName);
                    return;
                }
                case MenuPage.Curvature:
                    _bus.Send(new SetScreenCurvature(_store.State.FocusedScreenId, CurvaturePresets.Value(index)));
                    return; // fica na página: dá para comparar os presets
                case MenuPage.FrameRate:
                    _bus.Send(new SetFrameRateMode(_store.State.FocusedScreenId, FrameRateMenu.Mode(index)));
                    return; // fica na página: mostra o fps concedido
                case MenuPage.Audio:
                    if (index == 0) _audio.Settings.TogglePlayback();
                    else _audio.Settings.ToggleMic();
                    return;
                default:
                    Invoke(_main[index]);
                    return;
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
                case MenuItemId.AddVirtual:
                    Page = MenuPage.Aspects;
                    break;
                case MenuItemId.AddMirror:
                    Page = MenuPage.Mirrors;
                    break;
                case MenuItemId.Curvature:
                    Page = MenuPage.Curvature;
                    break;
                case MenuItemId.FrameRate:
                    Page = MenuPage.FrameRate;
                    break;
                case MenuItemId.Audio:
                    Page = MenuPage.Audio;
                    break;
                case MenuItemId.AutoArrange:
                    _arrange.Toggle();
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
                case MenuItemId.Recreate:
                    EnterRecreate(ws);
                    break;
            }
        }

        private void Create(Resolution resolution, ScreenSource source, string label)
        {
            var near = SurfaceSlots.Facing(_store.State.Surface, _head.HeadPose); // spec 2C §3: nasce à frente da visão
            var result = _bus.Send(new CreateScreen(resolution, null, ScreenSizing.InitialWidthMeters(resolution), 0f, label, source, near));
            Page = MenuPage.Main;
            if (result.Accepted && VideoScreens(_store.State) > ComfortableVideoScreens) _notice = TooManyNotice;
        }

        private void EnterRecreate(Workspace ws)
        {
            ws.TryGetScreen(ws.FocusedScreenId, out var target);
            _recreate = target.Id;
            _recreateFrom = target.Resolution;
            _recreateTitle = RecreateTitlePrefix + target.Label;
            _recreateCurrentLabel = target.Resolution + CurrentSuffix;
            Orientation = target.Orientation; // a atual aparece marcada sem trocar a orientação
            Page = MenuPage.Aspects;
        }

        private void Recreate(Resolution resolution)
        {
            _bus.Send(new RecreateScreen(_recreate, resolution)); // undo recria na anterior (ADR 0012)
            ExitRecreate();
            Page = MenuPage.Main;
        }

        private void ExitRecreate()
        {
            _recreate = ScreenId.None;
            _recreateTitle = string.Empty;
        }

        private bool IsCurrent(int index) =>
            IsRecreating && ResolutionCatalog.Get(_aspect, index, Orientation).Equals(_recreateFrom);

        private static int VideoScreens(Workspace ws)
        {
            int n = 0;
            for (int i = 0; i < ws.Screens.Count; i++)
                if (ws.Screens[i].Source.HasVideo) n++;
            return n;
        }

        private bool HasFocusedScreen()
        {
            var ws = _store.State;
            return ws.Contains(ws.FocusedScreenId);
        }

        /// <returns>Curvatura pedida da tela em foco, ou −1 (nenhum preset marcado).</returns>
        private bool HasFocusedVideoScreen()
        {
            var ws = _store.State;
            return ws.TryGetScreen(ws.FocusedScreenId, out var screen) && screen.Source.HasVideo;
        }

        private bool IsCurrentFrameRate(int index)
        {
            var ws = _store.State;
            return ws.TryGetScreen(ws.FocusedScreenId, out var screen) && screen.FrameRateMode.Equals(FrameRateMenu.Mode(index));
        }

        private float FocusedCurvature()
        {
            var ws = _store.State;
            return ws.TryGetScreen(ws.FocusedScreenId, out var screen) ? screen.Curvature : -1f;
        }

        /// <summary>O painel sumiu: volta à página principal e apaga o aviso.</summary>
        public void OnHidden()
        {
            Page = MenuPage.Main;
            _notice = string.Empty;
            ExitRecreate();
        }
    }
}
