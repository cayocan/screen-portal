using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Screens;
using UnityEngine;
using Pose = ScreenPortal.Domain.Spatial.Pose; // UnityEngine.Pose também existe
using Resolution = ScreenPortal.Domain.Screens.Resolution; // UnityEngine.Resolution também existe

namespace ScreenPortal.Presentation.Screens
{
    /// <summary>
    /// Tela com vídeo do PC (monitor virtual ou espelho): compositor layer (OVROverlay quad, Surface externa na
    /// resolução exata, brief §6.3) + estados (spec 2C §5). Antes do primeiro quadro a layer fica escondida (hidden só
    /// pula a submissão: a Surface existe e o decoder já pode abrir) e um quad no eye buffer mostra o estado. Depois de ter
    /// tido vídeo, perder a conexão só escurece a layer.
    /// </summary>
    public sealed class VideoScreenView : IScreenView
    {
        private static readonly Vector4 Dimmed = new Vector4(0.35f, 0.35f, 0.35f, 1f);
        private static readonly Vector4 Normal = Vector4.one;

        private readonly GameObject _root;       // pose (sem escala)
        private readonly Transform _statusQuad;  // escala = largura × altura
        private readonly TextMesh _statusText;
        private readonly Material _statusMaterial;
        private readonly Material _textMaterial;
        private readonly System.Action<VideoScreenView> _onDisposed;
        private Resolution _resolution;
        private bool _everLive;
        private ScreenLinkState _state = ScreenLinkState.Offline;
        private string _detail = string.Empty;
        private Vector3 _size = Vector3.one;
        private Transform _layer;

        public OVROverlay Overlay { get; private set; }
        public ScreenId Id { get; }

        public VideoScreenView(Transform parent, VirtualScreen screen, Material unlit, System.Action<VideoScreenView> onDisposed = null)
        {
            Id = screen.Id;
            _onDisposed = onDisposed;
            _root = new GameObject("Screen " + screen.Id.Value);
            _root.transform.SetParent(parent, false);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Status";
            quad.GetComponent<Collider>().enabled = false;
            quad.transform.SetParent(_root.transform, false);
            _statusQuad = quad.transform;
            if (unlit != null)
            {
                _statusMaterial = new Material(unlit) { color = new Color(0.08f, 0.09f, 0.11f) };
                quad.GetComponent<MeshRenderer>().sharedMaterial = _statusMaterial;
            }

            var textGo = new GameObject("StatusText");
            textGo.transform.SetParent(_root.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, -0.003f);
            _statusText = textGo.AddComponent<TextMesh>();
            _statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _textMaterial = StereoText.MaterialFor(_statusText.font);
            textGo.GetComponent<MeshRenderer>().sharedMaterial = _textMaterial;
            _statusText.fontSize = 96;
            _statusText.anchor = TextAnchor.MiddleCenter;
            _statusText.alignment = TextAlignment.Center;
            _statusText.color = Color.white;

            BuildOverlay(screen.Resolution);
            SetLink(ScreenLinkState.Offline, string.Empty);
        }

        public void Apply(VirtualScreen screen, Pose pose)
        {
            // Resolução só muda via RecreateScreen (raro): recria a layer com a nova external surface.
            if (!screen.Resolution.Equals(_resolution))
            {
                _everLive = false; // a última imagem não vale no novo tamanho
                BuildOverlay(screen.Resolution);
                SetLink(_state, _detail);
            }
            ApplyTransform(pose, screen.WidthMeters, screen.HeightMeters);
            _root.name = string.IsNullOrEmpty(screen.Label) ? "Screen " + screen.Id.Value : screen.Label;
        }

        public void ApplyTransform(Pose pose, float widthMeters, float heightMeters)
        {
            _root.transform.SetLocalPositionAndRotation(pose.Position.ToUnity(), pose.Rotation.ToUnity());
            var size = new Vector3(widthMeters, heightMeters, 1f);
            _size = size;
            _layer.localScale = size;
            _statusQuad.localScale = size;
            _statusText.characterSize = 0.006f * widthMeters;
        }

        public void SetLink(ScreenLinkState state, string detail)
        {
            _state = state;
            _detail = detail ?? string.Empty;
            if (state == ScreenLinkState.Live) _everLive = true;
            string text;
            switch (state)
            {
                case ScreenLinkState.Live: text = null; break;
                case ScreenLinkState.Creating: text = "Criando monitor…"; break;
                case ScreenLinkState.Connecting: text = "Conectando vídeo…"; break;
                case ScreenLinkState.Failed: text = "Falha:\n" + (_detail.Length > 60 ? _detail.Substring(0, 60) + "…" : _detail); break;
                default: text = "PC desconectado"; break;
            }
            // Depois de já ter tido vídeo, só a falha troca a imagem por texto; o resto escurece a última imagem.
            bool showText = text != null && (!_everLive || state == ScreenLinkState.Failed);
            _statusQuad.gameObject.SetActive(showText);
            _statusText.gameObject.SetActive(showText);
            if (showText) _statusText.text = text;
            if (Overlay == null) return;
            Overlay.hidden = showText;
            Overlay.SetPerLayerColorScaleAndOffset(state == ScreenLinkState.Live ? Normal : Dimmed, Vector4.zero);
        }

        private void BuildOverlay(Resolution resolution)
        {
            var old = _layer;
            _resolution = resolution;

            var go = new GameObject("Layer");
            _layer = go.transform;
            _layer.SetParent(_root.transform, false);
            _layer.localScale = _size;
            if (old != null) Object.Destroy(old.gameObject); // adiada: o decoder fecha antes da Surface antiga morrer
            go.SetActive(false); // configurar antes do OnEnable criar a layer
            var overlay = go.AddComponent<OVROverlay>();
            overlay.currentOverlayType = OVROverlay.OverlayType.Overlay;
            overlay.currentOverlayShape = OVROverlay.OverlayShape.Quad;
            overlay.isExternalSurface = true;
            overlay.externalSurfaceWidth = resolution.Width;
            overlay.externalSurfaceHeight = resolution.Height;
            overlay.useBicubicFiltering = true; // padrão validado no teste de texto (fase0-texto.md)
            overlay.overridePerLayerColorScaleAndOffset = true;
            overlay.hidden = true;
            Overlay = overlay;
            go.SetActive(true);
        }

        public void Dispose()
        {
            _onDisposed?.Invoke(this);
            if (_root != null) Object.Destroy(_root);
            if (_statusMaterial != null) Object.Destroy(_statusMaterial);
            if (_textMaterial != null) Object.Destroy(_textMaterial);
        }
    }
}
