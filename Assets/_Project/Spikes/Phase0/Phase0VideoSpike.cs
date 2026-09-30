using System.Text;
using UnityEngine;

namespace ScreenPortal.Spikes.Phase0
{
    /// <summary>
    /// Fase 0 — caminho de vídeo: N telas, cada uma um OVROverlay quad com external surface
    /// alimentado direto pelo MediaCodec (headset-native). Mede fps, perdas e latência
    /// captura→decodificado por tela, e loga linhas "SPSTAT" (tools/phase0-collect.ps1 extrai o CSV).
    /// Código de spike: fora das regras de arquitetura da Fase 1 (sem DI/Commands), mas sem alocação por frame.
    /// </summary>
    public sealed class Phase0VideoSpike : MonoBehaviour
    {
        [Header("Stream (igual ao host: ScreenPortalHost stream --streams N)")]
        [SerializeField, Range(1, 8)] private int streamCount = 1;
        [SerializeField] private int basePort = 9000;
        [Tooltip("Resolução que o host envia (o log do host mostra). O decoder se adapta ao SPS se diferir.")]
        [SerializeField] private int videoWidth = 2560;
        [SerializeField] private int videoHeight = 1080;

        [Header("Layout")]
        [SerializeField] private float screenWidthMeters = 1.4f;
        [SerializeField] private float distanceMeters = 1.3f;
        [SerializeField] private float gapMeters = 0.05f;
        [SerializeField] private int columns = 3;

        [Header("Qualidade da layer")]
        [SerializeField] private bool useBicubicFiltering = true;
        [SerializeField] private bool useEfficientSharpen = false;
        [SerializeField] private bool useEfficientSupersample = false;

        [Header("Fase 2E (spike de telas curvas)")]
        [Tooltip("Telas como layers cilíndricas lado a lado (centro do cilindro na cabeça, raio = distância).")]
        [SerializeField] private bool cylinder = false;
        [Tooltip("> 0: a tela 0 alterna plana ↔ curva a cada N s (recria a layer e a sessão nativa).")]
        [SerializeField] private int toggleSeconds = 0;

        [Header("HUD")]
        [SerializeField] private TextMesh statsLabel;

        private OVROverlay[] _overlays;
        private long[] _handles;
        private float _nextStatsTime;
        private readonly StringBuilder _hud = new StringBuilder(1024);

        /// <summary>
        /// Permite variar o teste sem rebuild:
        /// adb shell am start -n com.screenportal.app/&lt;activity&gt; --ei streams 4 --ei width 2560 --ei height 1080
        /// </summary>
        private void ApplyLaunchOverrides()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var intent = activity.Call<AndroidJavaObject>("getIntent");
            streamCount = Mathf.Clamp(intent.Call<int>("getIntExtra", "streams", streamCount), 1, 8);
            videoWidth = intent.Call<int>("getIntExtra", "width", videoWidth);
            videoHeight = intent.Call<int>("getIntExtra", "height", videoHeight);
            // Teste de legibilidade (docs/measurements/fase0-texto.md): tamanho/distância em cm e filtros da layer.
            screenWidthMeters = intent.Call<int>("getIntExtra", "widthcm", Mathf.RoundToInt(screenWidthMeters * 100f)) / 100f;
            distanceMeters = intent.Call<int>("getIntExtra", "distcm", Mathf.RoundToInt(distanceMeters * 100f)) / 100f;
            useBicubicFiltering = intent.Call<int>("getIntExtra", "bicubic", useBicubicFiltering ? 1 : 0) != 0;
            useEfficientSharpen = intent.Call<int>("getIntExtra", "sharpen", useEfficientSharpen ? 1 : 0) != 0;
            useEfficientSupersample = intent.Call<int>("getIntExtra", "supersample", useEfficientSupersample ? 1 : 0) != 0;
            cylinder = intent.Call<int>("getIntExtra", "cylinder", cylinder ? 1 : 0) != 0;
            toggleSeconds = intent.Call<int>("getIntExtra", "toggle", toggleSeconds);
            Debug.Log($"[ScreenPortal] config: {streamCount} stream(s), {videoWidth}x{videoHeight}, " +
                      $"{screenWidthMeters:F2} m a {distanceMeters:F2} m, bicubic={useBicubicFiltering} " +
                      $"sharpen={useEfficientSharpen} supersample={useEfficientSupersample} cylinder={cylinder} toggle={toggleSeconds}");
#endif
        }

        private void Start()
        {
            ApplyLaunchOverrides();
            _overlays = new OVROverlay[streamCount];
            _handles = new long[streamCount];

            float height = screenWidthMeters * videoHeight / videoWidth;
            int cols = Mathf.Min(columns, streamCount);
            int rows = (streamCount + cols - 1) / cols;

            _height = height;
            _cols = cols;
            _rows = rows;
            for (int i = 0; i < streamCount; i++) _overlays[i] = CreateOverlay(i, cylinder);
            _nextToggleTime = toggleSeconds > 0 ? Time.unscaledTime + toggleSeconds : float.MaxValue;
        }

        private float _height;
        private int _cols, _rows;
        private float _nextToggleTime;
        private bool _screen0Cylinder;

        private OVROverlay CreateOverlay(int i, bool asCylinder)
        {
            int col = i % _cols;
            int row = i / _cols;
            float y = 1.2f + ((_rows - 1) * 0.5f - row) * (_height + gapMeters);

            var go = new GameObject("Screen" + i);
            go.SetActive(false); // configurar a overlay antes do OnEnable criar a layer
            go.transform.SetParent(transform, false);
            if (asCylinder)
            {
                // Centro do cilindro na cabeça; scale = (comprimento do arco, altura, raio).
                float stepDeg = (screenWidthMeters + gapMeters) / distanceMeters * Mathf.Rad2Deg;
                go.transform.localPosition = new Vector3(0f, y, 0f);
                go.transform.localRotation = Quaternion.Euler(0f, (col - (_cols - 1) * 0.5f) * stepDeg, 0f);
                go.transform.localScale = new Vector3(screenWidthMeters, _height, distanceMeters);
            }
            else
            {
                float x = (col - (_cols - 1) * 0.5f) * (screenWidthMeters + gapMeters);
                go.transform.localPosition = new Vector3(x, y, distanceMeters);
                go.transform.localScale = new Vector3(screenWidthMeters, _height, 1f);
            }

            var overlay = go.AddComponent<OVROverlay>();
            overlay.currentOverlayType = OVROverlay.OverlayType.Overlay;
            overlay.currentOverlayShape = asCylinder ? OVROverlay.OverlayShape.Cylinder : OVROverlay.OverlayShape.Quad;
            overlay.isExternalSurface = true;
            overlay.externalSurfaceWidth = videoWidth;
            overlay.externalSurfaceHeight = videoHeight;
            overlay.useBicubicFiltering = useBicubicFiltering;
            overlay.useEfficientSharpen = useEfficientSharpen;
            overlay.useEfficientSupersample = useEfficientSupersample;
            overlay.compositionDepth = i;
            go.SetActive(true);
            if (i == 0) _screen0Cylinder = asCylinder;
            return overlay;
        }

        /// <summary>Spike 2E: troca a forma da tela 0 recriando a layer e a sessão (mede o buraco sem imagem no logcat).</summary>
        private void ToggleScreen0()
        {
            NativeVideoBridge.Stop(_handles[0]);
            _handles[0] = 0;
            Destroy(_overlays[0].gameObject);
            _overlays[0] = CreateOverlay(0, !_screen0Cylinder);
            Debug.Log($"[ScreenPortal] toggle: tela 0 agora {(_screen0Cylinder ? "cilindro" : "quad")} t={Time.realtimeSinceStartup:F3}");
        }

        private void Update()
        {
            for (int i = 0; i < _overlays.Length; i++)
            {
                if (_handles[i] != 0) continue;
                var surface = _overlays[i].externalSurfaceObject; // criado de forma assíncrona pelo compositor
                if (surface == System.IntPtr.Zero) continue;

                _handles[i] = NativeVideoBridge.Start(i, basePort + i, surface, videoWidth, videoHeight);
                Debug.Log(_handles[i] != 0
                    ? $"[ScreenPortal] tela {i}: sessão nativa na porta {basePort + i} t={Time.realtimeSinceStartup:F3}"
                    : $"[ScreenPortal] tela {i}: falha ao iniciar a sessão nativa");
            }

            if (Time.unscaledTime >= _nextToggleTime)
            {
                _nextToggleTime = Time.unscaledTime + toggleSeconds;
                ToggleScreen0();
            }

            if (Time.unscaledTime >= _nextStatsTime)
            {
                _nextStatsTime = Time.unscaledTime + 1f;
                RefreshStats();
            }
        }

        private void RefreshStats()
        {
            _hud.Clear();
            _hud.Append("stream rx_fps dec_fps Mbps dropNet dropDec lat_avg lat_min lat_max rtt\n");
            for (int i = 0; i < _handles.Length; i++)
            {
                string line = NativeVideoBridge.TakeStats(_handles[i]);
                _hud.Append(line ?? $"SPSTAT,{i},aguardando surface/host").Append('\n');
            }
            if (statsLabel != null) statsLabel.text = _hud.ToString();
        }

        private void OnDestroy()
        {
            if (_handles == null) return;
            for (int i = 0; i < _handles.Length; i++)
            {
                NativeVideoBridge.Stop(_handles[i]);
                _handles[i] = 0;
            }
        }
    }
}
