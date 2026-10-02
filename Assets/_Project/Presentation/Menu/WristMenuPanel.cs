using ScreenPortal.App.Menu;
using ScreenPortal.Domain.Screens;
using UnityEngine;
using Pose = ScreenPortal.Domain.Spatial.Pose;

namespace ScreenPortal.Presentation.Menu
{
    /// <summary>
    /// Menu de pulso (spec 2B §5, 2C §3): aparece com a palma esquerda virada para o rosto ou segurando Menu (≡) no
    /// controle esquerdo; fica acima da mão, virado para a cabeça. Páginas de tamanho variável; na página de
    /// proporções cada botão mostra um frame na proporção. Acionado pelo raio da mão/controle direito (InteractionRouter).
    /// </summary>
    public sealed class WristMenuPanel : MonoBehaviour, IWristMenuPanel
    {
        private const float FrameFill = 0.8f;      // o frame ocupa até 80% do botão
        private const float FrameBorderM = 0.0015f;

        [SerializeField] private Transform anchor;
        [SerializeField] private Transform head;
        [SerializeField] private OVRHand leftHand;
        [SerializeField] private Material unlit;
        [Tooltip("Normal da palma no espaço local da âncora da mão esquerda (calibrado no Simulator/Quest).")]
        [SerializeField] private Vector3 palmNormalLocal = Vector3.up;
        [SerializeField] private float palmFacingThreshold = 0.5f;
        [SerializeField] private float heightAboveHandM = 0.12f;

        private GameObject _content;
        private Transform _background;
        private TMPro.TextMeshPro _status;
        private readonly TMPro.TextMeshPro[] _labels = new TMPro.TextMeshPro[WristMenu.MaxItems];
        private readonly MeshRenderer[] _buttons = new MeshRenderer[WristMenu.MaxItems];
        private readonly Transform[] _frames = new Transform[WristMenu.MaxItems];
        private readonly Transform[] _frameInner = new Transform[WristMenu.MaxItems];
        // Último valor aplicado: TextMesh.text refaz a malha a cada set, então só atribui quando muda. O WristMenu
        // devolve strings em cache, por isso a comparação por referência basta.
        private readonly string[] _shownLabels = new string[WristMenu.MaxItems];
        private readonly Material[] _shownMaterials = new Material[WristMenu.MaxItems];
        private string _shownStatus;
        private Material _normal;
        private Material _hover;
        private Material _disabled;
        private MenuPage _layoutPage = (MenuPage)(-1);
        private int _layoutCount = -1;
        private Orientation _layoutOrientation;

        public bool Visible { get; private set; }

        private bool _floating;
        private Pose _floatPose;

        public void Float(bool on, Pose pose)
        {
            _floating = on;
            _floatPose = pose;
        }

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
            if (_floating)
            {
                Visible = true;
                _content.SetActive(true);
                transform.SetPositionAndRotation(_floatPose.Position.ToUnity(), _floatPose.Rotation.ToUnity());
                return;
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
            int count = menu.ItemCount;
            if (menu.Page != _layoutPage || count != _layoutCount || menu.Orientation != _layoutOrientation) Layout(menu, count);
            for (int i = 0; i < count; i++)
            {
                string label = menu.Label(i);
                if (!ReferenceEquals(label, _shownLabels[i]))
                {
                    _shownLabels[i] = label;
                    _labels[i].text = label;
                }
                var material = !menu.IsEnabled(i) ? _disabled : i == hoverIndex ? _hover : _normal;
                if (!ReferenceEquals(material, _shownMaterials[i]))
                {
                    _shownMaterials[i] = material;
                    _buttons[i].sharedMaterial = material;
                }
            }
            string status = menu.StatusLine;
            if (!ReferenceEquals(status, _shownStatus))
            {
                _shownStatus = status;
                _status.text = status;
            }
        }

        private void Layout(WristMenu menu, int count)
        {
            _layoutPage = menu.Page;
            _layoutCount = count;
            _layoutOrientation = menu.Orientation;
            float height = MenuLayout.PanelHeight(count);
            _background.localScale = new Vector3(MenuLayout.PanelWidth, height, 1f);
            _status.transform.localPosition = new Vector3(0f, height * 0.5f - MenuLayout.StatusHeightM * 0.5f, -0.002f);
            for (int i = 0; i < WristMenu.MaxItems; i++)
            {
                bool used = i < count;
                _buttons[i].gameObject.SetActive(used);
                _labels[i].gameObject.SetActive(used);
                float aspect = used ? menu.FrameAspect(i) : 0f;
                _frames[i].gameObject.SetActive(aspect > 0f);
                if (!used) continue;
                MenuLayout.ButtonCenter(i, count, out float x, out float y);
                _buttons[i].transform.localPosition = new Vector3(x, y, 0f);
                _labels[i].transform.localPosition = new Vector3(x, y, -0.003f);
                if (aspect > 0f) PlaceFrame(i, x, y, aspect);
            }
        }

        private void PlaceFrame(int i, float x, float y, float aspect)
        {
            float maxW = MenuLayout.ButtonWidthM * FrameFill;
            float h = MenuLayout.ButtonHeightM * FrameFill;
            float w = h * aspect;
            if (w > maxW)
            {
                w = maxW;
                h = w / aspect;
            }
            _frames[i].localPosition = new Vector3(x, y, -0.001f);
            _frames[i].localScale = new Vector3(w, h, 1f);
            _frameInner[i].localScale = new Vector3(Mathf.Max(0f, 1f - 2f * FrameBorderM / w), Mathf.Max(0f, 1f - 2f * FrameBorderM / h), 1f);
        }

        private void Build()
        {
            _content = new GameObject("Content");
            _content.transform.SetParent(transform, false);
            _normal = Tinted(new Color(0.18f, 0.2f, 0.24f));
            _hover = Tinted(new Color(0.25f, 0.55f, 0.9f));
            _disabled = Tinted(new Color(0.1f, 0.1f, 0.12f));
            var frameLine = Tinted(new Color(0.85f, 0.87f, 0.9f));
            var frameFill = Tinted(new Color(0.12f, 0.13f, 0.16f));

            _background = Quad("Background", Tinted(new Color(0.06f, 0.06f, 0.08f)), _content.transform);
            _background.localPosition = new Vector3(0f, 0f, 0.002f);
            _status = Text("Status", Vector3.zero);
            for (int i = 0; i < WristMenu.MaxItems; i++)
            {
                var button = Quad("Button" + i, _normal, _content.transform);
                button.localScale = new Vector3(MenuLayout.ButtonWidthM, MenuLayout.ButtonHeightM, 1f);
                _buttons[i] = button.GetComponent<MeshRenderer>();
                _frames[i] = Quad("Frame" + i, frameLine, _content.transform);
                _frameInner[i] = Quad("FrameInner" + i, frameFill, _frames[i]);
                _frameInner[i].localPosition = new Vector3(0f, 0f, -0.0005f);
                _labels[i] = Text("Label" + i, Vector3.zero);
            }
            _content.SetActive(false);
        }

        private Material Tinted(Color color) => unlit != null ? new Material(unlit) { color = color } : null;

        private static Transform Quad(string name, Material material, Transform parent)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.GetComponent<Collider>().enabled = false;
            if (material != null) quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            quad.transform.SetParent(parent, false);
            return quad.transform;
        }

        private TMPro.TextMeshPro Text(string name, Vector3 localPosition)
        {
            var text = UiText.Create(name, _content.transform, 0.0576f, TMPro.TextAlignmentOptions.Center, Color.white);
            text.transform.localPosition = localPosition;
            return text;
        }
    }
}
