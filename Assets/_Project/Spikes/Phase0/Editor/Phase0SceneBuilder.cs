using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScreenPortal.Spikes.Phase0.Editor
{
    /// <summary>
    /// Monta de forma reproduzível as cenas e configurações da Fase 0.
    /// Menu: Screen Portal/Phase 0/*. Batchmode:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod ScreenPortal.Spikes.Phase0.Editor.Phase0SceneBuilder.BuildAll
    /// </summary>
    public static class Phase0SceneBuilder
    {
        private const string Root = "Assets/_Project/Spikes/Phase0";
        private const string ScenesDir = Root + "/Scenes";
        private const string GeneratedDir = Root + "/Generated";
        public const string VideoScenePath = ScenesDir + "/Phase0_Video.unity";
        public const string PassthroughScenePath = ScenesDir + "/Phase0_Passthrough.unity";

        private const string CameraRigPrefab = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        private const string MrukPrefab = "Packages/com.meta.xr.mrutilitykit/Core/Tools/MRUK.prefab";

        [MenuItem("Screen Portal/Phase 0/Build All (project config + scenes)")]
        public static void BuildAll()
        {
            ConfigureProject();
            BuildVideoScene();
            BuildPassthroughScene();
            SetBootScene(VideoScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[ScreenPortal] Fase 0: cenas e configuração prontas.");
        }

        [MenuItem("Screen Portal/Phase 0/Configure Project (Meta features)")]
        public static void ConfigureProject()
        {
            var config = OVRProjectConfig.CachedProjectConfig;
            config.anchorSupport = OVRProjectConfig.AnchorSupport.Enabled;
            config.sceneSupport = OVRProjectConfig.FeatureSupport.Required;          // MRUK Keyboard Tracking (brief §15.2)
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
            config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            OVRProjectConfig.CommitProjectConfig(config);

            PlayerSettings.productName = "Screen Portal";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.screenportal.app");
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // O vídeo chega por UDP: o app precisa da permissão INTERNET (o .aar também declara).
            PlayerSettings.Android.forceInternetPermission = true;
        }

        [MenuItem("Screen Portal/Phase 0/Boot scene: Video")]
        public static void BootVideo() => SetBootScene(VideoScenePath);

        [MenuItem("Screen Portal/Phase 0/Boot scene: Passthrough")]
        public static void BootPassthrough() => SetBootScene(PassthroughScenePath);

        public static void BuildVideoScene()
        {
            EnsureFolders();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var rig = InstantiateRig(passthrough: false);

            var spikeGo = new GameObject("Phase0Video");
            var spike = spikeGo.AddComponent<Phase0VideoSpike>();
            var label = CreateLabel("StatsHUD", new Vector3(0f, 0.45f, 1.1f), 0.012f);
            SetPrivate(spike, "statsLabel", label);

            EditorSceneManager.SaveScene(scene, VideoScenePath);
        }

        public static void BuildPassthroughScene()
        {
            EnsureFolders();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var rig = InstantiateRig(passthrough: true);

            var mrukAsset = AssetDatabase.LoadAssetAtPath<GameObject>(MrukPrefab);
            if (mrukAsset != null) PrefabUtility.InstantiatePrefab(mrukAsset);
            else Debug.LogError("[ScreenPortal] MRUK.prefab não encontrado em " + MrukPrefab);

            CreateBackdrop();
            var material = GetOrCreateWindowMaterial();
            var overlay = CreateOcclusionOverlay();
            var label = CreateLabel("StatusHUD", new Vector3(0f, 1.45f, 1.0f), 0.01f);

            var spikeGo = new GameObject("Phase0Passthrough");
            var spike = spikeGo.AddComponent<Phase0PassthroughSpike>();
            SetPrivate(spike, "head", rig.centerEyeAnchor);
            SetPrivate(spike, "windowMaterial", material);
            SetPrivate(spike, "statusLabel", label);
            SetPrivate(spike, "occlusionTestOverlay", overlay);

            EditorSceneManager.SaveScene(scene, PassthroughScenePath);
        }

        private static OVRCameraRig InstantiateRig(bool passthrough)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CameraRigPrefab);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var rig = go.GetComponent<OVRCameraRig>();
            var manager = go.GetComponent<OVRManager>();
            manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            manager.isInsightPassthroughEnabled = passthrough;

            // Mesmos valores que o Project Setup Tool aplica (evita que o Fix All reabra estas tarefas).
            var so = new SerializedObject(manager);
            SetBool(so, "_enableDynamicResolution", true);
            SetBool(so, "requestScenePermissionOnStartup", passthrough);
            so.ApplyModifiedPropertiesWithoutUndo();

            // No SDK 207 o OVRPassthroughLayer é sempre underlay.
            if (passthrough) go.AddComponent<OVRPassthroughLayer>();

            // Passthrough: fundo transparente (exigência do Project Setup Tool); o "vazio" virtual
            // opaco vem da esfera de CreateBackdrop, recortada só pela Passthrough Window.
            var cam = rig.centerEyeAnchor.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = passthrough ? new Color(0f, 0f, 0f, 0f) : Color.black;
            return rig;
        }

        private static TextMesh CreateLabel(string name, Vector3 position, float characterSize)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            var text = go.AddComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.fontSize = 48;
            text.characterSize = characterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Left;
            text.color = new Color(0.85f, 1f, 0.85f);
            text.text = "Screen Portal — Fase 0";
            return text;
        }

        /// <summary>Esfera opaca em volta do usuário: o "mundo virtual" que a Passthrough Window recorta.</summary>
        private static void CreateBackdrop()
        {
            string path = GeneratedDir + "/UnlitBackdrop.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("ScreenPortal/UnlitBackdrop"));
                AssetDatabase.CreateAsset(material, path);
            }

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "VirtualBackdrop";
            Object.DestroyImmediate(sphere.GetComponent<Collider>());
            sphere.transform.position = new Vector3(0f, 1.2f, 0f);
            sphere.transform.localScale = Vector3.one * 30f;
            var renderer = sphere.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static Material GetOrCreateWindowMaterial()
        {
            string path = GeneratedDir + "/PassthroughWindowFeather.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("ScreenPortal/PassthroughWindowFeather"));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>Overlay com textura xadrez ~logo acima da mesa, cruzando a zona de passthrough na visão.</summary>
        private static OVROverlay CreateOcclusionOverlay()
        {
            string texPath = GeneratedDir + "/Checker.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                tex = new Texture2D(256, 128, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                for (int y = 0; y < 128; y++)
                for (int x = 0; x < 256; x++)
                    tex.SetPixel(x, y, ((x / 32 + y / 32) & 1) == 0 ? new Color(0.9f, 0.4f, 0.1f) : Color.white);
                tex.Apply();
                AssetDatabase.CreateAsset(tex, texPath);
            }

            var go = new GameObject("OcclusionTestOverlay");
            go.SetActive(false);
            go.transform.position = new Vector3(0f, 0.95f, 0.6f);
            go.transform.localScale = new Vector3(0.4f, 0.2f, 1f);
            var overlay = go.AddComponent<OVROverlay>();
            overlay.currentOverlayType = OVROverlay.OverlayType.Overlay;
            overlay.currentOverlayShape = OVROverlay.OverlayShape.Quad;
            overlay.textures = new Texture[] { tex, tex };
            overlay.noDepthBufferTesting = true;
            go.SetActive(true);
            return overlay;
        }

        private static void SetBootScene(string first)
        {
            string other = first == VideoScenePath ? PassthroughScenePath : VideoScenePath;
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(first, true),
                new EditorBuildSettingsScene(other, true),
            };
            Debug.Log("[ScreenPortal] Cena inicial do build: " + Path.GetFileNameWithoutExtension(first));
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(ScenesDir)) AssetDatabase.CreateFolder(Root, "Scenes");
            if (!AssetDatabase.IsValidFolder(GeneratedDir)) AssetDatabase.CreateFolder(Root, "Generated");
        }

        private static void SetBool(SerializedObject so, string field, bool value)
        {
            var prop = so.FindProperty(field);
            if (prop != null) prop.boolValue = value;
            else Debug.LogWarning($"[ScreenPortal] Campo '{field}' não encontrado em {so.targetObject.GetType().Name}");
        }

        private static void SetPrivate(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[ScreenPortal] Campo '{field}' não encontrado em {target.GetType().Name}");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
