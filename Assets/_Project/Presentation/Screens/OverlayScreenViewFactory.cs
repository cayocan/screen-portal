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
    /// Cria as views: OVROverlay (vídeo do PC) ou padrão de teste (telas "Teste N"). Informa a Surface de cada
    /// overlay ao decoder e dá acesso às views para a pré-visualização do arraste.
    /// </summary>
    public sealed class OverlayScreenViewFactory : IScreenViewFactory, IScreenSurfaceProvider
    {
        private readonly Transform _root;
        private readonly Material _unlit;
        private readonly Dictionary<ScreenId, IScreenView> _views = new Dictionary<ScreenId, IScreenView>();
        private readonly Dictionary<ScreenId, OverlayScreenView> _overlays = new Dictionary<ScreenId, OverlayScreenView>();

        public OverlayScreenViewFactory(Transform root, Material unlit = null)
        {
            _root = root;
            _unlit = unlit;
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
                var overlay = new OverlayScreenView(_root, screen, v =>
                {
                    _views.Remove(v.Id);
                    _overlays.Remove(v.Id);
                });
                _overlays[screen.Id] = overlay;
                view = overlay;
            }
            _views[screen.Id] = view;
            return view;
        }

        public bool TryGetView(ScreenId screen, out IScreenView view) => _views.TryGetValue(screen, out view);

        public bool TryGetSurface(ScreenId screen, out IntPtr surface)
        {
            surface = IntPtr.Zero;
            if (!_overlays.TryGetValue(screen, out var view) || view.Overlay == null) return false;
            surface = view.Overlay.externalSurfaceObject;
            return surface != IntPtr.Zero;
        }
    }
}
