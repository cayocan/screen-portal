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

        protected override void Configure(IContainerBuilder builder) =>
            AppInstaller.Install(builder, screensRoot != null ? screensRoot : transform, seedDemoScreen);
    }
}
