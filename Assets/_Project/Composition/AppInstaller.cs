using ScreenPortal.App;
using ScreenPortal.App.Screens;
using ScreenPortal.Presentation.Screens;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace ScreenPortal.Composition
{
    /// <summary>Registrações do app, separadas do MonoBehaviour para serem testáveis (CompositionTests).</summary>
    public static class AppInstaller
    {
        public static void Install(IContainerBuilder builder, Transform screensRoot, bool seedDemoScreen)
        {
            builder.RegisterInstance(new AppOptions(seedDemoScreen));
            builder.RegisterInstance(new WorkspaceStore());
            // Instância pronta: o construtor tem "int capacity" opcional, e o VContainer tentaria injetar um int.
            builder.RegisterInstance(new CommandJournal()).As<ICommandObserver>().AsSelf();
            builder.Register<CommandBus>(Lifetime.Singleton).As<ICommandBus>();
            builder.RegisterInstance(new OverlayScreenViewFactory(screensRoot)).As<IScreenViewFactory>();
            builder.Register<ScreenViewSynchronizer>(Lifetime.Singleton);
            builder.RegisterEntryPoint<AppEntryPoint>().AsSelf();
        }
    }
}
