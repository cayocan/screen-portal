using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Manipulation;
using ScreenPortal.App.Menu;
using ScreenPortal.App.Screens;
using ScreenPortal.App.Session;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Spatial;
using ScreenPortal.Infrastructure.Net;
using ScreenPortal.Infrastructure.Unity;
using ScreenPortal.Presentation.Manipulation;
using ScreenPortal.Presentation.Screens;
using VContainer;
using VContainer.Unity;

namespace ScreenPortal.Composition
{
    /// <summary>
    /// Registrações do app (testáveis em CompositionTests). Regra: classes com parâmetro primitivo no construtor
    /// entram por RegisterInstance (o VContainer tentaria injetar o primitivo — lição da Fase 1A).
    /// </summary>
    public static class AppInstaller
    {
        public static void Install(IContainerBuilder builder, AppEnvironment env)
        {
            builder.RegisterInstance(env);
            builder.RegisterInstance(env.Options);
            // Última sessão: se o app reiniciar, a workspace volta (e o host religa os monitores que ainda aluga).
            var session = env.Session ?? new NullSessionStore();
            bool restored = SessionCodec.TryDecode(session.Load(), out var lastSession);
            builder.RegisterInstance(new WorkspaceStore(restored ? lastSession : null));
            builder.RegisterInstance(session).As<ISessionStore>();
            builder.Register<SessionAutosave>(Lifetime.Singleton);
            // Sessão restaurada também recentra: cada abertura do app tem um tracking space novo (origem e frente onde o
            // óculos estava), então só as posições relativas à superfície valem — o layout volta igual em volta do usuário.
            builder.RegisterInstance(new CommandJournal()).As<ICommandObserver>().AsSelf();
            builder.Register<CommandBus>(Lifetime.Singleton).As<ICommandBus>();

            var views = new OverlayScreenViewFactory(env.ScreensRoot, env.UnlitMaterial);
            builder.RegisterInstance(views).AsSelf().As<IScreenViewFactory>().As<IScreenSurfaceProvider>();
            builder.Register<ScreenViewSynchronizer>(Lifetime.Singleton);

            builder.RegisterInstance(new MonotonicClock()).As<IClock>();
            builder.RegisterInstance(new TcpControlChannel(2000)).As<IControlChannel>();
            builder.RegisterInstance(env.Discovery).As<IHostDiscovery>();
            builder.RegisterInstance(env.Pairing).As<IPairingStore>();
            builder.Register<ConnectionStateMachine>(Lifetime.Singleton);

            // 2C: telas com vídeo sob comando do headset (spec 2026-09-30-fase2c-varias-telas-design.md)
            builder.Register<NativeScreenVideo>(Lifetime.Singleton).As<IScreenVideo>();
            builder.Register<HostSync>(Lifetime.Singleton).AsSelf().As<IHostCatalog>().As<IScreenLinks>();
            // 2G: som do PC e microfone (nativo no Quest; no Editor, sem áudio)
            builder.RegisterInstance(new ScreenPortal.App.Audio.AudioSettings());
            builder.Register(r => new ScreenPortal.App.Audio.AudioSync(r.Resolve<ConnectionStateMachine>(),
                env.Audio ?? new ScreenPortal.App.Audio.NullAudioLink(), r.Resolve<ScreenPortal.App.Audio.AudioSettings>()), Lifetime.Singleton);
            builder.RegisterBuildCallback(c => c.Resolve<ScreenPortal.App.Audio.AudioSync>()); // assina a conexão já na montagem
            // 2F: fps/bitrate por tela (modo → olhar → orçamento) mandados ao host só quando mudam
            builder.Register(r =>
            {
                var machine = r.Resolve<ConnectionStateMachine>();
                return new RateSync(r.Resolve<WorkspaceStore>(), r.Resolve<IScreenLinks>(), () => machine.IsLinked, machine.Send,
                    r.Resolve<IHeadPoseSource>(), r.Resolve<IClock>());
            }, Lifetime.Singleton);

            // 2B: manipulação por controle/mãos + menu de pulso (spec 2026-09-29-fase2b-manipulacao-design.md)
            builder.RegisterInstance(env.Head ?? new FixedHeadPose(Pose.At(LayoutSurface.DefaultCenter))).As<IHeadPoseSource>();
            builder.Register<ScreenManipulationController>(Lifetime.Singleton);
            builder.Register<StartupRecenter>(Lifetime.Singleton);
            builder.Register(r => new WristMenu(r.Resolve<WorkspaceStore>(), r.Resolve<ICommandBus>(), r.Resolve<IHeadPoseSource>(),
                r.Resolve<IHostCatalog>(), env.ShowTestScreens, r.Resolve<RateSync>(), r.Resolve<ScreenPortal.App.Audio.AudioSync>()), Lifetime.Singleton);
            // Controle parado perde o laser; tchau liga/desliga as mãos (laser, interação e modelo).
            builder.Register<HandsMode>(Lifetime.Singleton);
            builder.RegisterBuildCallback(c =>
            {
                if (env.SetHandsVisible != null) c.Resolve<HandsMode>().Changed += env.SetHandsVisible;
            });
            IManipulationInput Filtered(IManipulationInput pointer, IObjectResolver r) => pointer == null ? null
                : new PointerFilter(pointer, r.Resolve<HandsMode>(), r.Resolve<IHeadPoseSource>(), r.Resolve<IClock>());
            builder.Register(r => new InteractionRouter(Filtered(env.RightPointer, r), Filtered(env.LeftPointer, r),
                r.Resolve<ScreenManipulationController>(), r.Resolve<WristMenu>(), env.MenuPanel, r.Resolve<ICommandBus>()), Lifetime.Singleton);
            builder.Register(r => new ManipulationPresenter(env.ScreensRoot, env.UnlitMaterial,
                r.Resolve<ScreenManipulationController>(), r.Resolve<WorkspaceStore>(), views, r.Resolve<InteractionRouter>()), Lifetime.Singleton);

            builder.RegisterEntryPoint<AppEntryPoint>().AsSelf();
        }
    }
}
