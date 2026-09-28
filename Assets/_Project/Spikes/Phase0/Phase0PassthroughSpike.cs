using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace ScreenPortal.Spikes.Phase0
{
    /// <summary>
    /// Fase 0 — validação da zona de passthrough (brief §9.2 e §17 item 5):
    /// MRUK Keyboard Tracking → caixa sugerida com as margens padrão → Passthrough Window com feather.
    /// Também mede: tempo até detectar o teclado, perdas de tracking, e posiciona um OVROverlay
    /// logo acima da mesa para checar a oclusão overlay × janela de passthrough.
    ///
    /// Controles (controle direito): thumbstick X = margem do lado do mouse, Y = margem da frente
    /// ("arraste simples de borda" do spike); A = voltar à sugestão; B = alterna depth test da overlay.
    /// </summary>
    public sealed class Phase0PassthroughSpike : MonoBehaviour
    {
        [Header("Referências")]
        [SerializeField] private Transform head;
        [SerializeField] private Material windowMaterial;
        [SerializeField] private TextMesh statusLabel;
        [SerializeField] private OVROverlay occlusionTestOverlay;

        [Header("Margens padrão (brief §9.2), em metros")]
        [SerializeField] private float frontMargin = 0.08f;
        [SerializeField] private float backMargin = 0.04f;
        [SerializeField] private float mouseSideMargin = 0.25f;
        [SerializeField] private float otherSideMargin = 0.03f;
        [SerializeField] private float heightAboveDesk = 0.25f;
        [SerializeField] private bool rightHanded = true;

        [Header("Ajuste pelo thumbstick")]
        [SerializeField] private float adjustSpeedMetersPerSecond = 0.15f;

        private static readonly int FaceSizeId = Shader.PropertyToID("_FaceSizeMeters");

        private MRUKTrackable _keyboard;
        private Transform _zone;
        private MeshRenderer _zoneRenderer;
        private MaterialPropertyBlock _props;
        private float _deltaFront;
        private float _deltaMouseSide;
        private float _startTime;
        private float _detectedAfter = -1f;
        private int _trackingLosses;
        private bool _wasTracked;
        private float _nextLabelTime;

        private void Start()
        {
            _startTime = Time.realtimeSinceStartup;
            _props = new MaterialPropertyBlock();

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "PassthroughZone";
            Destroy(cube.GetComponent<Collider>());
            _zone = cube.transform;
            _zoneRenderer = cube.GetComponent<MeshRenderer>();
            _zoneRenderer.sharedMaterial = windowMaterial;
            cube.SetActive(false);

            var mruk = MRUK.Instance;
            if (mruk == null)
            {
                Debug.LogError("[ScreenPortal] MRUK não está na cena.");
                return;
            }
            var config = mruk.SceneSettings.TrackerConfiguration;
            config.KeyboardTrackingEnabled = true;
            mruk.SceneSettings.TrackerConfiguration = config;
            mruk.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
            mruk.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);
        }

        private void OnTrackableAdded(MRUKTrackable trackable)
        {
            if (trackable.TrackableType != OVRAnchor.TrackableType.Keyboard) return;
            _keyboard = trackable;
            if (_detectedAfter < 0f)
            {
                _detectedAfter = Time.realtimeSinceStartup - _startTime;
                Debug.Log($"SPKB,detected,{_detectedAfter:F2}");
            }
            _zone.gameObject.SetActive(true);
        }

        private void OnTrackableRemoved(MRUKTrackable trackable)
        {
            if (trackable != _keyboard) return;
            _trackingLosses++;
            Debug.Log($"SPKB,removed,{Time.realtimeSinceStartup - _startTime:F2}");
            // Brief §9.5: a zona congela na última posição conhecida (não some nem pula).
            _keyboard = null;
        }

        private void Update()
        {
            HandleInput();

            if (_keyboard != null)
            {
                bool tracked = _keyboard.IsTracked;
                if (_wasTracked && !tracked)
                {
                    _trackingLosses++;
                    Debug.Log($"SPKB,lost,{Time.realtimeSinceStartup - _startTime:F2}");
                }
                _wasTracked = tracked;
                if (tracked) UpdateZoneFromKeyboard();
            }

            if (Time.unscaledTime >= _nextLabelTime)
            {
                _nextLabelTime = Time.unscaledTime + 0.5f;
                UpdateLabel();
            }
        }

        private void HandleInput()
        {
            Vector2 stick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
            _deltaMouseSide += stick.x * adjustSpeedMetersPerSecond * Time.deltaTime;
            _deltaFront += stick.y * adjustSpeedMetersPerSecond * Time.deltaTime;

            if (OVRInput.GetDown(OVRInput.Button.One))
            {
                _deltaFront = 0f;
                _deltaMouseSide = 0f;
            }
            if (OVRInput.GetDown(OVRInput.Button.Two) && occlusionTestOverlay != null)
            {
                occlusionTestOverlay.noDepthBufferTesting = !occlusionTestOverlay.noDepthBufferTesting;
            }
        }

        /// <summary>
        /// Sugestão ⊕ ajuste (brief §9.1). Independe da convenção de eixos do MRUK: projeta os 8
        /// cantos do VolumeBounds num referencial com o yaw do teclado e "frente" = lado da cabeça.
        /// </summary>
        private void UpdateZoneFromKeyboard()
        {
            if (!_keyboard.VolumeBounds.HasValue) return;
            Bounds local = _keyboard.VolumeBounds.Value;
            Transform kb = _keyboard.transform;

            // Eixo "largura" do teclado = o eixo local mais longo, projetado no plano horizontal.
            Vector3 right = Vector3.ProjectOnPlane(kb.right, Vector3.up);
            Vector3 alt = Vector3.ProjectOnPlane(kb.up, Vector3.up);
            Vector3 alt2 = Vector3.ProjectOnPlane(kb.forward, Vector3.up);
            float wRight = local.size.x * right.magnitude;
            float wAlt = local.size.y * alt.magnitude;
            float wAlt2 = local.size.z * alt2.magnitude;
            Vector3 widthAxis = wRight >= wAlt && wRight >= wAlt2 ? right : (wAlt >= wAlt2 ? alt : alt2);
            widthAxis.Normalize();
            Vector3 depthAxis = Vector3.Cross(Vector3.up, widthAxis); // aponta para "trás" ou "frente"

            // Extensões do teclado nesse referencial (mundo).
            Vector3 center = kb.TransformPoint(local.center);
            float minW = float.MaxValue, maxW = float.MinValue, minD = float.MaxValue, maxD = float.MinValue;
            float minY = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 w = kb.TransformPoint(corner) - center;
                float a = Vector3.Dot(w, widthAxis), b = Vector3.Dot(w, depthAxis);
                minW = Mathf.Min(minW, a); maxW = Mathf.Max(maxW, a);
                minD = Mathf.Min(minD, b); maxD = Mathf.Max(maxD, b);
                minY = Mathf.Min(minY, center.y + w.y);
            }

            // "Frente" = lado do teclado voltado para o usuário.
            if (head != null && Vector3.Dot(head.position - center, depthAxis) < 0f)
            {
                depthAxis = -depthAxis;
                float t = minD; minD = -maxD; maxD = -t;
            }
            // Lado do mouse: direita do usuário (widthAxis orientado pela cabeça).
            Vector3 userRight = Vector3.Cross(Vector3.up, -depthAxis); // olhando para o teclado
            if (Vector3.Dot(userRight, widthAxis) < 0f)
            {
                widthAxis = -widthAxis;
                float t = minW; minW = -maxW; maxW = -t;
            }

            float mouse = mouseSideMargin + _deltaMouseSide;
            float front = frontMargin + _deltaFront;
            float leftMargin = rightHanded ? otherSideMargin : mouse;
            float rightMargin = rightHanded ? mouse : otherSideMargin;

            float wMin = minW - leftMargin, wMax = maxW + rightMargin;
            float dMin = minD - backMargin, dMax = maxD + front; // +depth = frente (usuário)
            float bottom = minY - 0.01f, top = minY + heightAboveDesk;

            // Tamanho mínimo 10×10 cm (brief §9.3).
            float width = Mathf.Max(0.10f, wMax - wMin);
            float depth = Mathf.Max(0.10f, dMax - dMin);
            float height = top - bottom;

            Vector3 zoneCenter = center + widthAxis * ((wMin + wMax) * 0.5f) + depthAxis * ((dMin + dMax) * 0.5f);
            zoneCenter.y = (bottom + top) * 0.5f;

            _zone.SetPositionAndRotation(zoneCenter, Quaternion.LookRotation(depthAxis, Vector3.up));
            _zone.localScale = new Vector3(width, height, depth);

            // O SDF usa o tamanho da face; aproximação: a maior face visível (topo) = largura × profundidade.
            _props.SetVector(FaceSizeId, new Vector4(width, depth, 0f, 0f));
            _zoneRenderer.SetPropertyBlock(_props);
        }

        private void UpdateLabel()
        {
            if (statusLabel == null) return;
            string kb = _keyboard == null ? "teclado: não rastreado" : (_keyboard.IsTracked ? "teclado: rastreado" : "teclado: perdido (zona congelada)");
            string detected = _detectedAfter < 0f ? "detectado: —" : $"detectado em {_detectedAfter:F1} s";
            string depth = occlusionTestOverlay == null ? "" : $"\noverlay depth test: {(occlusionTestOverlay.noDepthBufferTesting ? "OFF" : "ON")} (B)";
            statusLabel.text = $"{kb}\n{detected} | perdas: {_trackingLosses}\n" +
                               $"ajuste: frente {_deltaFront * 100f:+0;-0} cm, mouse {_deltaMouseSide * 100f:+0;-0} cm (A reseta){depth}";
        }
    }
}
