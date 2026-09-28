using System;
using System.Collections.Generic;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Screens;
using UnityEngine;

namespace ScreenPortal.Presentation.Screens
{
    /// <summary>Cria as views OVROverlay e informa a Surface (external surface) de cada tela ao decoder.</summary>
    public sealed class OverlayScreenViewFactory : IScreenViewFactory, IScreenSurfaceProvider
    {
        private readonly Transform _root;
        private readonly Dictionary<ScreenId, OverlayScreenView> _views = new Dictionary<ScreenId, OverlayScreenView>();

        public OverlayScreenViewFactory(Transform root) => _root = root;

        public IScreenView Create(VirtualScreen screen)
        {
            var view = new OverlayScreenView(_root, screen, v => _views.Remove(v.Id));
            _views[screen.Id] = view;
            return view;
        }

        public bool TryGetSurface(ScreenId screen, out IntPtr surface)
        {
            surface = IntPtr.Zero;
            if (!_views.TryGetValue(screen, out var view) || view.Overlay == null) return false;
            surface = view.Overlay.externalSurfaceObject;
            return surface != IntPtr.Zero;
        }
    }
}
