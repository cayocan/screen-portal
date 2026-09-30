using ScreenPortal.App.Menu;
using UnityEngine;
using Pose = ScreenPortal.Domain.Spatial.Pose;

namespace ScreenPortal.Presentation.Menu
{
    /// <summary>
    /// Menu de pulso (spec 2B §5): aparece com a palma esquerda virada para o rosto ou segurando Menu (≡) no controle
    /// esquerdo; fica acima da mão, virado para a cabeça. Acionado pelo raio da mão/controle direito (InteractionRouter).
    /// </summary>
    public sealed class WristMenuPanel : MonoBehaviour, IWristMenuPanel
    {
        [SerializeField] private Transform anchor;
        [SerializeField] private Transform head;
        [SerializeField] private OVRHand leftHand;
        [SerializeField] private Material unlit;
        [Tooltip("Normal da palma no espaço local da âncora da mão esquerda (calibrado no Simulator/Quest).")]
        [SerializeField] private Vector3 palmNormalLocal = Vector3.up;
        [SerializeField] private float palmFacingThreshold = 0.5f;
        [SerializeField] private float heightAboveHandM = 0.12f;

        private GameObject _content;
        private TextMesh _status;
        private TextMesh[] _labels;
        private MeshRenderer[] _buttons;
        private Material _normal;
        private Material _hover;
        private Material _disabled;
        private Material _text;

        public bool Visible { get; private set; }

        public Pose Pose => new Pose(transform.position.ToDomain(), transform.rotation.ToDomain());

        private void Awake() => Build();

        private void Update()
        {
            bool controllerMenu = OVRInput.Get(OVRInput.Button.Start, OVRInput.Controller.LTouch);
            bool palm = false;
            if (leftHand != null && leftHand.IsTracked && anchor != null && head != null)
            {
                var normal = (anchor.rotation * palmNormalLocal).normalized;
                palm = Vector3.Dot(normal, (head.position - anchor.position).normalized) > palmFacingThreshold;
            }
            Visible = anchor != null && head != null && (controllerMenu || palm);
            _content.SetActive(Visible);
            if (!Visible) return;
            var position = anchor.position + Vector3.up * heightAboveHandM;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));
        }

        public void Render(WristMenu menu, int hoverIndex)
        {
            if (!Visible) return;
            var items = WristMenu.Items;
            for (int i = 0; i < items.Length; i++)
            {
                bool enabled = menu.IsEnabled(items[i]);
                _labels[i].text = menu.Label(items[i]);
                _buttons[i].sharedMaterial = !enabled ? _disabled : i == hoverIndex ? _hover : _normal;
            }
            _status.text = menu.StatusLine;
        }

        private void Build()
        {
            int count = WristMenu.Items.Length;
            _content = new GameObject("Content");
            _content.transform.SetParent(transform, false);
            _normal = Tinted(new Color(0.18f, 0.2f, 0.24f));
            _hover = Tinted(new Color(0.25f, 0.55f, 0.9f));
            _disabled = Tinted(new Color(0.1f, 0.1f, 0.12f));

            var background = Quad("Background", Tinted(new Color(0.06f, 0.06f, 0.08f)));
            background.localPosition = new Vector3(0f, 0f, 0.002f);
            background.localScale = new Vector3(MenuLayout.PanelWidth, MenuLayout.PanelHeight(count), 1f);

            _status = Text("Status", new Vector3(0f, MenuLayout.PanelHeight(count) * 0.5f - MenuLayout.StatusHeightM * 0.5f, -0.002f));
            _labels = new TextMesh[count];
            _buttons = new MeshRenderer[count];
            for (int i = 0; i < count; i++)
            {
                MenuLayout.ButtonCenter(i, count, out float x, out float y);
                var button = Quad("Button" + i, _normal);
                button.localPosition = new Vector3(x, y, 0f);
                button.localScale = new Vector3(MenuLayout.ButtonWidthM, MenuLayout.ButtonHeightM, 1f);
                _buttons[i] = button.GetComponent<MeshRenderer>();
                _labels[i] = Text("Label" + i, new Vector3(x, y, -0.002f));
            }
            _content.SetActive(false);
        }

        private Material Tinted(Color color) => unlit != null ? new Material(unlit) { color = color } : null;

        private Transform Quad(string name, Material material)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.GetComponent<Collider>().enabled = false;
            if (material != null) quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            quad.transform.SetParent(_content.transform, false);
            return quad.transform;
        }

        private TextMesh Text(string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_content.transform, false);
            go.transform.localPosition = localPosition;
            var text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_text == null) _text = StereoText.MaterialFor(text.font);
            go.GetComponent<MeshRenderer>().sharedMaterial = _text;
            text.fontSize = 48;
            text.characterSize = 0.0012f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            return text;
        }
    }
}
