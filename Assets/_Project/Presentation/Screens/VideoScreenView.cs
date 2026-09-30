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
    /// A layer é Underlay (ADR 0010): um furo no eye buffer (alpha 0 + profundidade) a revela, e a interface desenhada
    /// na frente (moldura, alças, laser, menu) fica por cima da tela.
    /// </summary>
    public sealed class VideoScreenView : IScreenView
    {
        private static readonly Vector4 Dimmed = new Vector4(0.35f, 0.35f, 0.35f, 1f);
        private static readonly Vector4 Normal = Vector4.one;
        private const string HoleShaderName = "ScreenPortal/ScreenHole"; // Resources/ScreenHole.shader

        private readonly GameObject _root;       // pose (sem escala)
        private readonly CurvedQuadMesh _shape;   // furo e quad de estado: plana ou arco (spec 2E §4)
        private readonly GameObject _statusQuad;
        private readonly TextMesh _statusText;
        private readonly Material _statusMaterial;
        private readonly Material _textMaterial;
        private readonly GameObject _hole;
        private readonly Material _holeMaterial;
        private readonly System.Action<VideoScreenView> _onDisposed;
        private Resolution _resolution;
        private bool _everLive;
        private ScreenLinkState _state = ScreenLinkState.Offline;
        private string _detail = string.Empty;
        private float _width = 1f;
        private float _height = 1f;
        private bool _cylinder;
        private float _layerRadius = 1f; // último raio válido da layer cilíndrica
        private Transform _layer;

        public OVROverlay Overlay { get; private set; }
        public ScreenId Id { get; }

        public VideoScreenView(Transform parent, VirtualScreen screen, Material unlit, System.Action<VideoScreenView> onDisposed = null)
        {
            Id = screen.Id;
            _onDisposed = onDisposed;
            _root = new GameObject("Screen " + screen.Id.Value);
            _root.transform.SetParent(parent, false);

            _shape = new CurvedQuadMesh("Screen " + screen.Id.Value);
            if (unlit != null) _statusMaterial = new Material(unlit) { color = new Color(0.08f, 0.09f, 0.11f) };
            _statusQuad = ScreenMeshes.MeshObject("Status", _root.transform, _shape.Mesh, _statusMaterial);

            var holeShader = Shader.Find(HoleShaderName);
            if (holeShader != null) _holeMaterial = new Material(holeShader) { name = "ScreenHole" };
            else Debug.LogError($"[VideoScreenView] shader {HoleShaderName} não encontrado: a tela não aparece sob o eye buffer.");
            _hole = ScreenMeshes.MeshObject("Hole", _root.transform, _shape.Mesh, _holeMaterial);
            _hole.SetActive(false);

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

        public void Apply(VirtualScreen screen, Pose pose, float radiusM)
        {
            bool cylinder = radiusM > 0f;
            // Resolução (RecreateScreen) ou forma (plana ↔ curva, spec 2E; ADR 0011 — se o Horizon OS voltar a limitar
            // cilindros, o fallback é aqui): recria a layer com uma external surface nova. O HostSync fecha o decoder no
            // mesmo evento e o reabre nela no próximo Tick (StartStream → IDR).
            if (!screen.Resolution.Equals(_resolution) || cylinder != _cylinder)
            {
                if (!screen.Resolution.Equals(_resolution)) _everLive = false; // a última imagem não vale no novo tamanho
                _cylinder = cylinder;
                if (cylinder) _layerRadius = radiusM;
                BuildOverlay(screen.Resolution);
                SetLink(_state, _detail);
            }
            ApplyTransform(pose, screen.WidthMeters, screen.HeightMeters, radiusM);
            _root.name = string.IsNullOrEmpty(screen.Label) ? "Screen " + screen.Id.Value : screen.Label;
        }

        /// <summary>Também a prévia do arraste: nunca troca a forma da layer (só no <see cref="Apply"/> confirmado).</summary>
        public void ApplyTransform(Pose pose, float widthMeters, float heightMeters, float radiusM)
        {
            _root.transform.SetLocalPositionAndRotation(pose.Position.ToUnity(), pose.Rotation.ToUnity());
            _width = widthMeters;
            _height = heightMeters;
            if (_cylinder && radiusM > 0f) _layerRadius = radiusM; // prévia plana numa layer cilíndrica: mantém o último raio
            PlaceLayer();
            // furo e estado seguem a forma que a layer mostra (senão o furo recorta fora da imagem)
            _shape.Set(widthMeters, heightMeters, _cylinder ? _layerRadius : 0f);
            _statusText.characterSize = 0.006f * widthMeters; // texto plano no centro, 3 mm à frente
        }

        /// <summary>Cilindro: centro R atrás da tela (do lado do usuário), escala = (arco, altura, raio), como no spike 2E.</summary>
        private void PlaceLayer()
        {
            if (_cylinder)
            {
                _layer.localPosition = new Vector3(0f, 0f, -_layerRadius);
                _layer.localScale = new Vector3(_width, _height, _layerRadius);
            }
            else
            {
                _layer.localPosition = Vector3.zero;
                _layer.localScale = new Vector3(_width, _height, 1f);
            }
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
            _statusQuad.SetActive(showText);
            _statusText.gameObject.SetActive(showText);
            if (showText) _statusText.text = text;
            _hole.SetActive(!showText && Overlay != null);
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
            PlaceLayer();
            if (old != null) Object.Destroy(old.gameObject); // adiada: o decoder fecha antes da Surface antiga morrer
            go.SetActive(false); // configurar antes do OnEnable criar a layer
            var overlay = go.AddComponent<OVROverlay>();
            overlay.currentOverlayType = OVROverlay.OverlayType.Underlay;
            overlay.currentOverlayShape = _cylinder ? OVROverlay.OverlayShape.Cylinder : OVROverlay.OverlayShape.Quad;
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
            _shape.Dispose();
            if (_statusMaterial != null) Object.Destroy(_statusMaterial);
            if (_textMaterial != null) Object.Destroy(_textMaterial);
            if (_holeMaterial != null) Object.Destroy(_holeMaterial);
        }
    }
}
