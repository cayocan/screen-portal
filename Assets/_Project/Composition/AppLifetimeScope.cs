using ScreenPortal.App.Connection;
using ScreenPortal.Infrastructure.Net;
using ScreenPortal.Infrastructure.Protocol;
using ScreenPortal.Infrastructure.Unity;
using ScreenPortal.Presentation.Connection;
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

        protected override void Configure(IContainerBuilder builder)
        {
            bool android = Application.platform == RuntimePlatform.Android;
            IHostDiscovery discovery = android
                ? new AndroidHostDiscovery()
                : (IHostDiscovery)new StaticHostDiscovery(new HostEndpoint("Editor", editorHostAddress, ControlProtocol.Port));
            IPairingStore pairing = android ? new PlayerPrefsPairingStore() : (IPairingStore)new InMemoryPairingStore("editor");
            var options = new ConnectionOptions { DeviceName = SystemInfo.deviceName };

            AppInstaller.Install(builder, new AppEnvironment(
                screensRoot != null ? screensRoot : transform, statusView, discovery, pairing, options));
        }
    }
}
