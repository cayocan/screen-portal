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

            var screensRoot = new GameObject("Screens");

            var scopeGo = new GameObject("AppLifetimeScope");
            var scope = scopeGo.AddComponent<AppLifetimeScope>();
            var so = new SerializedObject(scope);
            so.FindProperty("screensRoot").objectReferenceValue = screensRoot.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[ScreenPortal] Cena Main gerada: " + ScenePath);
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
