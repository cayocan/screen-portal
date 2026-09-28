using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Screens;
using UnityEngine;

namespace ScreenPortal.Presentation.Screens
{
    public sealed class OverlayScreenViewFactory : IScreenViewFactory
    {
        private readonly Transform _root;

        public OverlayScreenViewFactory(Transform root) => _root = root;

        public IScreenView Create(VirtualScreen screen) => new OverlayScreenView(_root, screen);
    }
}
