using ScreenPortal.App.Connection;
using ScreenPortal.App.Manipulation;
using ScreenPortal.App.Menu;
using ScreenPortal.Presentation.Connection;
using UnityEngine;

namespace ScreenPortal.Composition
{
    /// <summary>
    /// O que varia por plataforma/teste: descoberta, pareamento, raiz das telas, painel de status, e (2B) ponteiros,
    /// cabeça, recentrar do sistema, menu de pulso e o material unlit. Os da 2B são opcionais (testes/Editor sem rig).
    /// A raiz das telas fica na origem do mundo: o acerto do raio usa coordenadas de mundo.
    /// </summary>
    public sealed class AppEnvironment
    {
        public Transform ScreensRoot { get; }
        public ConnectionStatusView StatusView { get; }
        public IHostDiscovery Discovery { get; }
        public IPairingStore Pairing { get; }
        public ConnectionOptions Options { get; }
        public IManipulationInput RightPointer { get; }
        public IManipulationInput LeftPointer { get; }
        public IHeadPoseSource Head { get; }
        public IRecenterEvents Recenter { get; }
        public IWristMenuPanel MenuPanel { get; }
        public Material UnlitMaterial { get; }

        public AppEnvironment(Transform screensRoot, ConnectionStatusView statusView, IHostDiscovery discovery,
            IPairingStore pairing, ConnectionOptions options,
            IManipulationInput rightPointer = null, IManipulationInput leftPointer = null, IHeadPoseSource head = null,
            IRecenterEvents recenter = null, IWristMenuPanel menuPanel = null, Material unlitMaterial = null)
        {
            ScreensRoot = screensRoot;
            StatusView = statusView;
            Discovery = discovery;
            Pairing = pairing;
            Options = options;
            RightPointer = rightPointer;
            LeftPointer = leftPointer;
            Head = head;
            Recenter = recenter;
            MenuPanel = menuPanel;
            UnlitMaterial = unlitMaterial;
        }
    }
}
