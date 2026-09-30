using ScreenPortal.Composition;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ScreenPortal.Editor
{
    /// <summary>Gera Assets/_Project/Scenes/Main.unity: OVRCameraRig + raiz das telas + AppLifetimeScope.</summary>
    public static class MainSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        private const string CameraRigPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        private const string UnlitMaterialPath = "Assets/_Project/Presentation/Materials/Unlit.mat";

        [MenuItem("Screen Portal/Main/Build Main Scene")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Scenes")) AssetDatabase.CreateFolder("Assets/_Project", "Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var rigGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CameraRigPrefab));
            var manager = rigGo.GetComponent<OVRManager>();
            manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            var cam = rigGo.GetComponent<OVRCameraRig>().centerEyeAnchor.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;

            var rig = rigGo.GetComponent<OVRCameraRig>();
            var leftHand = AddHand(rig.leftHandAnchor, "LeftOVRHand", 0);   // OVRHand.Hand.HandLeft
            var rightHand = AddHand(rig.rightHandAnchor, "RightOVRHand", 1); // OVRHand.Hand.HandRight
            var unlit = LoadOrCreateUnlitMaterial();

            var menuGo = new GameObject("WristMenu");
            var menu = menuGo.AddComponent<ScreenPortal.Presentation.Menu.WristMenuPanel>();
            var menuSo = new SerializedObject(menu);
            menuSo.FindProperty("anchor").objectReferenceValue = rig.leftHandAnchor;
            menuSo.FindProperty("head").objectReferenceValue = rig.centerEyeAnchor;
            menuSo.FindProperty("leftHand").objectReferenceValue = leftHand;
            menuSo.FindProperty("unlit").objectReferenceValue = unlit;
            // calibrado no Simulator (30/09/2026): palma para o rosto dá +0,86; palma para baixo −0,29; para fora −0,86
            menuSo.FindProperty("palmNormalLocal").vector3Value = Vector3.down;
            menuSo.ApplyModifiedPropertiesWithoutUndo();

            var screensRoot = new GameObject("Screens");

            var statusGo = new GameObject("ConnectionStatus");
            statusGo.transform.position = new Vector3(0f, 1.25f, 1.2f);
            var text = statusGo.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            statusGo.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.fontSize = 64;
            text.characterSize = 0.012f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            var statusView = statusGo.AddComponent<ScreenPortal.Presentation.Connection.ConnectionStatusView>();
            var statusSo = new SerializedObject(statusView);
            statusSo.FindProperty("label").objectReferenceValue = text;
            statusSo.ApplyModifiedPropertiesWithoutUndo();

            var scopeGo = new GameObject("AppLifetimeScope");
            var scope = scopeGo.AddComponent<AppLifetimeScope>();
            var so = new SerializedObject(scope);
            so.FindProperty("screensRoot").objectReferenceValue = screensRoot.transform;
            so.FindProperty("statusView").objectReferenceValue = statusView;
            so.FindProperty("headAnchor").objectReferenceValue = rig.centerEyeAnchor;
            so.FindProperty("trackingSpace").objectReferenceValue = rig.trackingSpace;
            so.FindProperty("leftHand").objectReferenceValue = leftHand;
            so.FindProperty("rightHand").objectReferenceValue = rightHand;
            so.FindProperty("menuPanel").objectReferenceValue = menu;
            so.FindProperty("unlitMaterial").objectReferenceValue = unlit;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[ScreenPortal] Cena Main gerada: " + ScenePath);
        }

        /// <summary>OVRHand sem malha: só raio (PointerPose) e pinça. HandType é serializado e internal no SDK.</summary>
        private static OVRHand AddHand(Transform anchor, string name, int handType)
        {
            var go = new GameObject(name);
            go.transform.SetParent(anchor, false);
            var hand = go.AddComponent<OVRHand>();
            var so = new SerializedObject(hand);
            so.FindProperty("HandType").intValue = handType;
            so.ApplyModifiedPropertiesWithoutUndo();
            return hand;
        }

        /// <summary>Material URP/Unlit como asset: garante o shader no build (Shader.Find em runtime não garante).</summary>
        private static Material LoadOrCreateUnlitMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(UnlitMaterialPath);
            if (existing != null) return existing;
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Presentation/Materials"))
                AssetDatabase.CreateFolder("Assets/_Project/Presentation", "Materials");
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Unlit" };
            AssetDatabase.CreateAsset(material, UnlitMaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        [MenuItem("Screen Portal/Main/Boot scene: Main")]
        public static void BootMain() => BuildScenes.SetFirst(ScenePath);

        [InitializeOnLoadMethod]
        private static void RegisterFileCommands()
        {
            EditorCommands.Register("build-main-scene", () => { Build(); return "OK " + ScenePath; });
            EditorCommands.Register("boot-main", () => { BootMain(); return "OK"; });
        }
    }
}
