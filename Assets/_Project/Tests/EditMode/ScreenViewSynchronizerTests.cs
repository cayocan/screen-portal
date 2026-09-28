using System.Collections.Generic;
using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class ScreenViewSynchronizerTests
    {
        private sealed class FakeView : IScreenView
        {
            public readonly List<VirtualScreen> Applied = new List<VirtualScreen>();
            public bool Disposed;
            public void Apply(VirtualScreen screen) => Applied.Add(screen);
            public void Dispose() => Disposed = true;
        }

        private sealed class FakeFactory : IScreenViewFactory
        {
            public readonly Dictionary<ScreenId, FakeView> Views = new Dictionary<ScreenId, FakeView>();
            public IScreenView Create(VirtualScreen screen)
            {
                var view = new FakeView();
                Views[screen.Id] = view;
                return view;
            }
        }

        private static CreateScreen Create() => new CreateScreen(Resolution.Create(1920, 1080), Pose.Identity, 1f);

        [Test]
        public void Start_CreatesViewsForExistingScreens()
        {
            var store = new WorkspaceStore();
            store.Dispatch(Create());
            var factory = new FakeFactory();

            using (var sync = new ScreenViewSynchronizer(store, factory))
            {
                sync.Start();
                Assert.That(sync.ViewCount, Is.EqualTo(1));
                Assert.That(factory.Views[new ScreenId(1)].Applied.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void StateChanges_CreateApplyAndDisposeViews()
        {
            var store = new WorkspaceStore();
            var factory = new FakeFactory();
            var sync = new ScreenViewSynchronizer(store, factory);
            sync.Start();

            store.Dispatch(Create());
            store.Dispatch(Create());
            var view1 = factory.Views[new ScreenId(1)];
            var view2 = factory.Views[new ScreenId(2)];
            Assert.That(sync.ViewCount, Is.EqualTo(2));

            store.Dispatch(new ResizeScreen(new ScreenId(1), 2f));
            Assert.That(view1.Applied.Count, Is.EqualTo(2), "tela alterada recebe Apply");
            Assert.That(view1.Applied[1].WidthMeters, Is.EqualTo(2f));
            Assert.That(view2.Applied.Count, Is.EqualTo(1), "tela intacta não recebe Apply de novo");

            store.Dispatch(new RemoveScreen(new ScreenId(2)));
            Assert.That(view2.Disposed, Is.True);
            Assert.That(sync.ViewCount, Is.EqualTo(1));

            sync.Dispose();
            Assert.That(view1.Disposed, Is.True);
            store.Dispatch(Create());
            Assert.That(factory.Views.ContainsKey(new ScreenId(3)), Is.False, "após Dispose não observa mais");
        }

        [Test]
        public void Undo_RecreatesRemovedView()
        {
            var store = new WorkspaceStore();
            var factory = new FakeFactory();
            var sync = new ScreenViewSynchronizer(store, factory);
            sync.Start();
            store.Dispatch(Create());
            store.Dispatch(new RemoveScreen(new ScreenId(1)));
            Assert.That(sync.ViewCount, Is.EqualTo(0));

            store.Undo();
            Assert.That(sync.ViewCount, Is.EqualTo(1));
            Assert.That(factory.Views[new ScreenId(1)].Disposed, Is.False);
            sync.Dispose();
        }
    }
}
