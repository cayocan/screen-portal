using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Screens;
using UnityEngine;
using Pose = ScreenPortal.Domain.Spatial.Pose; // UnityEngine.Pose também existe

namespace ScreenPortal.Presentation.Screens
{
    /// <summary>
    /// Tela de teste (sem vídeo): quad com textura + rótulo. Mesma interface da view com vídeo; na 2C a tela real
    /// troca só a origem da imagem. Não usa OVROverlay (a layer ficaria por cima do rótulo e da moldura).
    /// </summary>
    public sealed class TestPatternScreenView : IScreenView
    {
        private readonly GameObject _root;
        private readonly CurvedQuadMesh _shape; // plana ou arco (spec 2E §4); sem layer, a prévia pode curvar livremente
        private readonly TextMesh _label;
        private readonly Material _material;
        private readonly Material _labelMaterial;
        private readonly Texture2D _texture;
        private readonly System.Action<TestPatternScreenView> _onDisposed;

        public ScreenId Id { get; }

        public TestPatternScreenView(Transform parent, VirtualScreen screen, Material unlit, System.Action<TestPatternScreenView> onDisposed = null)
        {
            Id = screen.Id;
            _onDisposed = onDisposed;
            _root = new GameObject(screen.Label);
            _root.transform.SetParent(parent, false);

            _texture = TestPatternTexture.Create(screen.Id.Value);
            if (unlit != null) _material = new Material(unlit) { mainTexture = _texture };
            _shape = new CurvedQuadMesh("Pattern " + screen.Id.Value);
            ScreenMeshes.MeshObject("Pattern", _root.transform, _shape.Mesh, _material); // sem collider: acerto do raio é puro (ScreenHitTest)

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(_root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, -0.003f);
            _label = labelGo.AddComponent<TextMesh>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _labelMaterial = StereoText.MaterialFor(_label.font);
            labelGo.GetComponent<MeshRenderer>().sharedMaterial = _labelMaterial;
            _label.fontSize = 96;
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;
            _label.color = Color.black;
            _label.text = screen.Label + "\n" + screen.Resolution.Width + "×" + screen.Resolution.Height;
        }

        public void Apply(VirtualScreen screen, Pose pose, float radiusM) => ApplyTransform(pose, screen.WidthMeters, screen.HeightMeters, radiusM);

        public void ApplyTransform(Pose pose, float widthMeters, float heightMeters, float radiusM)
        {
            _root.transform.SetLocalPositionAndRotation(pose.Position.ToUnity(), pose.Rotation.ToUnity());
            _shape.Set(widthMeters, heightMeters, radiusM);
            _label.characterSize = 0.01f * widthMeters;
        }

        public void Dispose()
        {
            _onDisposed?.Invoke(this);
            Destroy(_root);
            _shape.Dispose();
            Destroy(_material);
            Destroy(_labelMaterial);
            Destroy(_texture);
        }

        private static void Destroy(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
