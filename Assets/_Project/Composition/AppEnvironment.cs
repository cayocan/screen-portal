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
        /// <summary>Build de desenvolvimento: o menu mostra "+ Tela de teste".</summary>
        public bool ShowTestScreens { get; }
        /// <summary>Mostra/esconde os modelos das mãos (tchau desliga as mãos).</summary>
        public System.Action<bool> SetHandsVisible { get; }
        /// <summary>Última sessão (a workspace volta se o app reiniciar).</summary>
        public ScreenPortal.App.Session.ISessionStore Session { get; }
        /// <summary>2G: áudio nativo (null = sem áudio, Editor/Simulator).</summary>
        public ScreenPortal.App.Audio.IAudioLink Audio { get; }

        public AppEnvironment(Transform screensRoot, ConnectionStatusView statusView, IHostDiscovery discovery,
            IPairingStore pairing, ConnectionOptions options,
            IManipulationInput rightPointer = null, IManipulationInput leftPointer = null, IHeadPoseSource head = null,
            IRecenterEvents recenter = null, IWristMenuPanel menuPanel = null, Material unlitMaterial = null,
            bool showTestScreens = false, System.Action<bool> setHandsVisible = null,
            ScreenPortal.App.Session.ISessionStore session = null, ScreenPortal.App.Audio.IAudioLink audio = null)
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
            ShowTestScreens = showTestScreens;
            SetHandsVisible = setHandsVisible;
            Session = session;
            Audio = audio;
        }
    }
}
