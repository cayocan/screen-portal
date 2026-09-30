using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Manipulation;
using ScreenPortal.Composition;
using ScreenPortal.Infrastructure.Net;
using UnityEngine;
using VContainer;

namespace ScreenPortal.Tests
{
    /// <summary>Regressão da Fase 1A (VContainer e primitivos) + o grafo da 1B resolve sem headset.</summary>
    public class CompositionTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("ScreensRootForTest");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void AppGraph_ResolvesEntryPointBusAndConnection()
        {
            var builder = new ContainerBuilder();
            AppInstaller.Install(builder, new AppEnvironment(_root.transform, null,
                new StaticHostDiscovery(new HostEndpoint("PC", "127.0.0.1", 9200)), new InMemoryPairingStore("t"), new ConnectionOptions()));

            using (var container = builder.Build())
            {
                Assert.That(container.Resolve<AppEntryPoint>(), Is.Not.Null);
                Assert.That(container.Resolve<ICommandBus>(), Is.Not.Null);
                Assert.That(container.Resolve<ConnectionStateMachine>(), Is.Not.Null);
                Assert.That(container.Resolve<ScreenManipulationController>(), Is.Not.Null);
                Assert.That(container.Resolve<InteractionRouter>(), Is.Not.Null);
                Assert.That(container.Resolve<ScreenPortal.App.Screens.HostSync>(), Is.Not.Null);
                Assert.That(container.Resolve<ScreenPortal.App.Menu.WristMenu>(), Is.Not.Null);
            }
        }
    }
}
