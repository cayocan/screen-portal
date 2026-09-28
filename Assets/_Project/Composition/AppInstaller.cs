using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Screens;
using ScreenPortal.Infrastructure.Net;
using ScreenPortal.Infrastructure.Unity;
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

            var views = new OverlayScreenViewFactory(env.ScreensRoot);
            builder.RegisterInstance(views).As<IScreenViewFactory>().As<IScreenSurfaceProvider>();
            builder.Register<ScreenViewSynchronizer>(Lifetime.Singleton);

            builder.RegisterInstance(new MonotonicClock()).As<IClock>();
            builder.RegisterInstance(new TcpControlChannel(2000)).As<IControlChannel>();
            builder.RegisterInstance(env.Discovery).As<IHostDiscovery>();
            builder.RegisterInstance(env.Pairing).As<IPairingStore>();
            builder.Register<NativeVideoSink>(Lifetime.Singleton).As<IVideoStreamSink>();
            builder.Register<ConnectionStateMachine>(Lifetime.Singleton);

            builder.RegisterEntryPoint<AppEntryPoint>().AsSelf();
        }
    }
}
