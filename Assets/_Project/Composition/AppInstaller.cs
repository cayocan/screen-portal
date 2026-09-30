using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Manipulation;
using ScreenPortal.App.Menu;
using ScreenPortal.App.Screens;
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
            builder.RegisterInstance(new WorkspaceStore());
            builder.RegisterInstance(new CommandJournal()).As<ICommandObserver>().AsSelf();
            builder.Register<CommandBus>(Lifetime.Singleton).As<ICommandBus>();

            var views = new OverlayScreenViewFactory(env.ScreensRoot, env.UnlitMaterial);
            builder.RegisterInstance(views).As<IScreenViewFactory>().As<IScreenSurfaceProvider>();
            builder.Register<ScreenViewSynchronizer>(Lifetime.Singleton);

            builder.RegisterInstance(new MonotonicClock()).As<IClock>();
            builder.RegisterInstance(new TcpControlChannel(2000)).As<IControlChannel>();
            builder.RegisterInstance(env.Discovery).As<IHostDiscovery>();
            builder.RegisterInstance(env.Pairing).As<IPairingStore>();
            builder.Register<ConnectionStateMachine>(Lifetime.Singleton);

            // 2B: manipulação por controle/mãos + menu de pulso (spec 2026-09-29-fase2b-manipulacao-design.md)
            builder.RegisterInstance(env.Head ?? new FixedHeadPose(Pose.At(LayoutSurface.DefaultCenter))).As<IHeadPoseSource>();
            builder.Register<ScreenManipulationController>(Lifetime.Singleton);
            builder.Register<WristMenu>(Lifetime.Singleton);
            builder.Register(r => new InteractionRouter(env.RightPointer, env.LeftPointer,
                r.Resolve<ScreenManipulationController>(), r.Resolve<WristMenu>(), env.MenuPanel, r.Resolve<ICommandBus>()), Lifetime.Singleton);
            builder.Register(r => new ManipulationPresenter(env.ScreensRoot, env.UnlitMaterial,
                r.Resolve<ScreenManipulationController>(), r.Resolve<WorkspaceStore>(), views, r.Resolve<InteractionRouter>()), Lifetime.Singleton);

            builder.RegisterEntryPoint<AppEntryPoint>().AsSelf();
        }
    }
}
