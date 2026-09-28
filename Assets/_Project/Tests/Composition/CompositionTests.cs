using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.Composition;
using UnityEngine;
using VContainer;

namespace ScreenPortal.Tests
{
    /// <summary>
    /// Regressão: no Quest, o VContainer falhava ao resolver CommandJournal (parâmetro opcional int no
    /// construtor). Monta o container real e resolve o grafo inteiro, sem precisar de headset.
    /// </summary>
    public class CompositionTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("ScreensRootForTest");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void AppGraph_ResolvesEntryPointAndBus()
        {
            var builder = new ContainerBuilder();
            AppInstaller.Install(builder, _root.transform, seedDemoScreen: false);

            using (var container = builder.Build())
            {
                Assert.That(container.Resolve<AppEntryPoint>(), Is.Not.Null);
                Assert.That(container.Resolve<ICommandBus>(), Is.Not.Null);
                Assert.That(container.Resolve<CommandJournal>(), Is.Not.Null);
            }
        }
    }
}
