using ScreenPortal.App.Connection;
using ScreenPortal.App.Manipulation;
using ScreenPortal.App.Menu;
using ScreenPortal.Infrastructure.Net;
using ScreenPortal.Infrastructure.Protocol;
using ScreenPortal.Infrastructure.Unity;
using ScreenPortal.Presentation.Connection;
using ScreenPortal.Presentation.Menu;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace ScreenPortal.Composition
{
    /// <summary>Raiz de composição (brief §8.4). No Quest: mDNS + PlayerPrefs; no Editor: host fixo + memória.</summary>
    public sealed class AppLifetimeScope : LifetimeScope
    {
        [SerializeField] private Transform screensRoot;
        [SerializeField] private ConnectionStatusView statusView;
        [Tooltip("Editor/PC: endereço do host (sem mDNS).")]
        [SerializeField] private string editorHostAddress = "127.0.0.1";

        [Header("2B — manipulação")]
        [SerializeField] private Transform headAnchor;
        [SerializeField] private Transform trackingSpace;
        [SerializeField] private OVRHand leftHand;
        [SerializeField] private OVRHand rightHand;
        [SerializeField] private WristMenuPanel menuPanel;
        [SerializeField] private Material unlitMaterial;

        /// <summary>Esconde/mostra a malha das mãos. O OVRMeshRenderer religaria a malha a cada quadro: desliga ele também.</summary>
        private void SetHandsVisible(bool visible)
        {
            foreach (var hand in new[] { leftHand, rightHand })
            {
                if (hand == null) continue;
                foreach (var meshRenderer in hand.GetComponentsInChildren<OVRMeshRenderer>(true)) meshRenderer.enabled = visible;
                foreach (var skinned in hand.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skinned.enabled = visible;
            }
        }

        protected override void Configure(IContainerBuilder builder)
        {
            bool android = Application.platform == RuntimePlatform.Android;
            IHostDiscovery discovery = android
                ? new AndroidHostDiscovery()
                : (IHostDiscovery)new StaticHostDiscovery(new HostEndpoint("Editor", editorHostAddress, ControlProtocol.Port));
            IPairingStore pairing = new PlayerPrefsPairingStore(); // Editor também: pareia uma vez (verificação com o host real)
            var options = new ConnectionOptions { DeviceName = SystemInfo.deviceName };

            IManipulationInput right = trackingSpace != null
                ? new OvrPointerInput(OVRInput.Controller.RTouch, rightHand, trackingSpace, headAnchor, false) : null;
            IManipulationInput left = trackingSpace != null
                ? new OvrPointerInput(OVRInput.Controller.LTouch, leftHand, trackingSpace, headAnchor, true) : null;
            IHeadPoseSource head = headAnchor != null ? new OvrHeadPose(headAnchor) : null;

            AppInstaller.Install(builder, new AppEnvironment(
                screensRoot != null ? screensRoot : transform, statusView, discovery, pairing, options,
                right, left, head, new OvrRecenterEvents(), menuPanel, unlitMaterial, Debug.isDebugBuild, SetHandsVisible,
                new FileSessionStore(System.IO.Path.Combine(Application.persistentDataPath, "last-session.txt"))));
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && Container != null && Container.TryResolve<AppEntryPoint>(out var entry)) entry.FlushSession();
        }
    }
}
