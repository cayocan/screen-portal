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

        [Header("HUD")]
        [SerializeField] private TextMesh statsLabel;

        private OVROverlay[] _overlays;
        private long[] _handles;
        private float _nextStatsTime;
        private readonly StringBuilder _hud = new StringBuilder(1024);

        private void Start()
        {
            _overlays = new OVROverlay[streamCount];
            _handles = new long[streamCount];

            float height = screenWidthMeters * videoHeight / videoWidth;
            int cols = Mathf.Min(columns, streamCount);
            int rows = (streamCount + cols - 1) / cols;

            for (int i = 0; i < streamCount; i++)
            {
                int col = i % cols;
                int row = i / cols;
                float x = (col - (cols - 1) * 0.5f) * (screenWidthMeters + gapMeters);
                float y = 1.2f + ((rows - 1) * 0.5f - row) * (height + gapMeters);

                var go = new GameObject("Screen" + i);
                go.SetActive(false); // configurar a overlay antes do OnEnable criar a layer
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(x, y, distanceMeters);
                go.transform.localScale = new Vector3(screenWidthMeters, height, 1f);

                var overlay = go.AddComponent<OVROverlay>();
                overlay.currentOverlayType = OVROverlay.OverlayType.Overlay;
                overlay.currentOverlayShape = OVROverlay.OverlayShape.Quad;
                overlay.isExternalSurface = true;
                overlay.externalSurfaceWidth = videoWidth;
                overlay.externalSurfaceHeight = videoHeight;
                overlay.useBicubicFiltering = useBicubicFiltering;
                overlay.useEfficientSharpen = useEfficientSharpen;
                overlay.compositionDepth = i;
                _overlays[i] = overlay;

                go.SetActive(true);
            }
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
                    ? $"[ScreenPortal] tela {i}: sessão nativa na porta {basePort + i}"
                    : $"[ScreenPortal] tela {i}: falha ao iniciar a sessão nativa");
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
