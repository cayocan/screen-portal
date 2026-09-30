using System;
using System.Collections.Generic;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Menu;
using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Screens;
using UnityEngine;

namespace ScreenPortal.Presentation.Screens
{
    /// <summary>
    /// Cria as views pela origem da tela: vídeo do PC (virtual/espelho, OVROverlay) ou padrão de teste. Informa a Surface
    /// de cada layer ao decoder, repassa o estado do vídeo (IScreenLinks) e dá acesso às views para a pré-visualização.
    /// </summary>
    public sealed class OverlayScreenViewFactory : IScreenViewFactory, IScreenSurfaceProvider
    {
        private readonly Transform _root;
        private readonly Material _unlit;
        private readonly Dictionary<ScreenId, IScreenView> _views = new Dictionary<ScreenId, IScreenView>();
        private readonly Dictionary<ScreenId, VideoScreenView> _videos = new Dictionary<ScreenId, VideoScreenView>();
        private IScreenLinks _links;

        public OverlayScreenViewFactory(Transform root, Material unlit = null)
        {
            _root = root;
            _unlit = unlit;
        }

        /// <summary>Liga o estado do vídeo (HostSync). Chamado uma vez na partida (evita ciclo no container).</summary>
        public void Bind(IScreenLinks links)
        {
            if (_links != null) _links.LinkChanged -= OnLinkChanged;
            _links = links;
            if (_links == null) return;
            _links.LinkChanged += OnLinkChanged;
            foreach (var id in _videos.Keys) OnLinkChanged(id);
        }

        public IScreenView Create(VirtualScreen screen)
        {
            IScreenView view;
            if (TestScreens.IsTestScreen(screen))
            {
                view = new TestPatternScreenView(_root, screen, _unlit, v => _views.Remove(v.Id));
            }
            else
            {
                var video = new VideoScreenView(_root, screen, _unlit, v =>
                {
                    _views.Remove(v.Id);
                    _videos.Remove(v.Id);
                });
                _videos[screen.Id] = video;
                view = video;
                OnLinkChanged(screen.Id);
            }
            _views[screen.Id] = view;
            return view;
        }

        private void OnLinkChanged(ScreenId id)
        {
            if (_links == null || !_videos.TryGetValue(id, out var view)) return;
            if (_links.TryGetLink(id, out var state, out var detail)) view.SetLink(state, detail);
        }

        public bool TryGetView(ScreenId screen, out IScreenView view) => _views.TryGetValue(screen, out view);

        public bool TryGetSurface(ScreenId screen, out IntPtr surface)
        {
            surface = IntPtr.Zero;
            if (!_videos.TryGetValue(screen, out var view) || view.Overlay == null) return false;
            surface = view.Overlay.externalSurfaceObject;
            return surface != IntPtr.Zero;
        }
    }
}
