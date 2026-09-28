using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Screens;
using UnityEngine;
using Resolution = ScreenPortal.Domain.Screens.Resolution; // UnityEngine.Resolution também existe

namespace ScreenPortal.Presentation.Screens
{
    /// <summary>
    /// Tela como compositor layer (OVROverlay quad, external surface, brief §6.3). Nesta fase só reflete
    /// pose e tamanho; o vídeo é ligado na Fase 2 (a Surface fica em <see cref="Overlay"/>.externalSurfaceObject).
    /// Curvatura (layer cilíndrica) também é Fase 2.
    /// </summary>
    public sealed class OverlayScreenView : IScreenView
    {
        private readonly GameObject _root;
        private Resolution _resolution;

        public OVROverlay Overlay { get; private set; }

        public ScreenId Id { get; }
        private readonly System.Action<OverlayScreenView> _onDisposed;

        public OverlayScreenView(Transform parent, VirtualScreen screen, System.Action<OverlayScreenView> onDisposed = null)
        {
            Id = screen.Id;
            _onDisposed = onDisposed;
            _root = new GameObject("Screen " + screen.Id.Value);
            _root.transform.SetParent(parent, false);
            BuildOverlay(screen.Resolution);
        }

        public void Apply(VirtualScreen screen)
        {
            // Resolução só muda via RecreateScreen (raro): recria a layer com a nova external surface.
            if (!screen.Resolution.Equals(_resolution)) BuildOverlay(screen.Resolution);

            var t = _root.transform;
            t.SetLocalPositionAndRotation(screen.Pose.Position.ToUnity(), screen.Pose.Rotation.ToUnity());
            t.localScale = new Vector3(screen.WidthMeters, screen.HeightMeters, 1f);
            _root.name = string.IsNullOrEmpty(screen.Label) ? "Screen " + screen.Id.Value : screen.Label;
        }

        private void BuildOverlay(Resolution resolution)
        {
            if (Overlay != null) Object.Destroy(Overlay);
            _resolution = resolution;

            _root.SetActive(false); // configurar antes do OnEnable criar a layer
            var overlay = _root.AddComponent<OVROverlay>();
            overlay.currentOverlayType = OVROverlay.OverlayType.Overlay;
            overlay.currentOverlayShape = OVROverlay.OverlayShape.Quad;
            overlay.isExternalSurface = true;
            overlay.externalSurfaceWidth = resolution.Width;
            overlay.externalSurfaceHeight = resolution.Height;
            overlay.useBicubicFiltering = true; // padrão validado no teste de texto (fase0-texto.md)
            Overlay = overlay;
            _root.SetActive(true);
        }

        public void Dispose()
        {
            _onDisposed?.Invoke(this);
            if (_root != null) Object.Destroy(_root);
        }
    }
}
