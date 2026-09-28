using ScreenPortal.App.Connection;
using ScreenPortal.Presentation.Connection;
using UnityEngine;

namespace ScreenPortal.Composition
{
    /// <summary>O que varia por plataforma/teste: descoberta, pareamento, raiz das telas, painel de status.</summary>
    public sealed class AppEnvironment
    {
        public Transform ScreensRoot { get; }
        public ConnectionStatusView StatusView { get; }
        public IHostDiscovery Discovery { get; }
        public IPairingStore Pairing { get; }
        public ConnectionOptions Options { get; }

        public AppEnvironment(Transform screensRoot, ConnectionStatusView statusView, IHostDiscovery discovery,
            IPairingStore pairing, ConnectionOptions options)
        {
            ScreensRoot = screensRoot;
            StatusView = statusView;
            Discovery = discovery;
            Pairing = pairing;
            Options = options;
        }
    }
}
