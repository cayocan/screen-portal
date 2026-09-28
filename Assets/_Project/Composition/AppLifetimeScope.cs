using ScreenPortal.App;
using ScreenPortal.App.Screens;
using ScreenPortal.Presentation.Screens;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace ScreenPortal.Composition
{
    /// <summary>Raiz de composição (brief §8.4): o único lugar que conhece as implementações concretas.</summary>
    public sealed class AppLifetimeScope : LifetimeScope
    {
        [SerializeField] private Transform screensRoot;
        [SerializeField] private bool seedDemoScreen = true;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterInstance(new AppOptions(seedDemoScreen));
            builder.RegisterInstance(new WorkspaceStore());
            builder.Register<CommandJournal>(Lifetime.Singleton).As<ICommandObserver>().AsSelf();
            builder.Register<CommandBus>(Lifetime.Singleton).As<ICommandBus>();
            builder.RegisterInstance(new OverlayScreenViewFactory(screensRoot != null ? screensRoot : transform))
                .As<IScreenViewFactory>();
            builder.Register<ScreenViewSynchronizer>(Lifetime.Singleton);
            builder.RegisterEntryPoint<AppEntryPoint>();
        }
    }
}
