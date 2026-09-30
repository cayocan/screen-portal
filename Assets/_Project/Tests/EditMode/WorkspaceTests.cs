using NUnit.Framework;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class WorkspaceTests
    {
        private static VirtualScreen Screen(int id) =>
            new VirtualScreen(new ScreenId(id), Resolution.Create(1920, 1080), Placement.Default, 1f);

        [Test]
        public void Empty_HasNoScreensAndNoFocus()
        {
            Assert.That(Workspace.Empty.Screens, Is.Empty);
            Assert.That(Workspace.Empty.FocusedScreenId.IsNone, Is.True);
            Assert.That(Workspace.Empty.AllocateId(), Is.EqualTo(new ScreenId(1)));
        }

        [Test]
        public void AddingScreens_AdvancesIdsAndKeepsOriginalUnchanged()
        {
            var ws = Workspace.Empty.WithScreenAdded(Screen(1));
            Assert.That(ws.Screens.Count, Is.EqualTo(1));
            Assert.That(ws.AllocateId(), Is.EqualTo(new ScreenId(2)));
            Assert.That(Workspace.Empty.Screens, Is.Empty);
        }

        [Test]
        public void AddingDuplicateId_Throws() =>
            Assert.Throws<System.InvalidOperationException>(() =>
                Workspace.Empty.WithScreenAdded(Screen(1)).WithScreenAdded(Screen(1)));

        [Test]
        public void Replace_SwapsTheScreenWithSameId()
        {
            var ws = Workspace.Empty.WithScreenAdded(Screen(1));
            var wider = Screen(1).WithWidth(2f);
            ws = ws.WithScreenReplaced(wider);
            Assert.That(ws.TryGetScreen(new ScreenId(1), out var s), Is.True);
            Assert.That(s, Is.SameAs(wider));
        }

        [Test]
        public void Replace_MissingScreen_Throws() =>
            Assert.Throws<System.InvalidOperationException>(() => Workspace.Empty.WithScreenReplaced(Screen(9)));

        [Test]
        public void RemovingFocusedScreen_ClearsFocus()
        {
            var ws = Workspace.Empty.WithScreenAdded(Screen(1)).WithScreenAdded(Screen(2)).WithFocus(new ScreenId(2));
            ws = ws.WithScreenRemoved(new ScreenId(2));
            Assert.That(ws.Contains(new ScreenId(2)), Is.False);
            Assert.That(ws.FocusedScreenId.IsNone, Is.True);
            Assert.That(ws.AllocateId(), Is.EqualTo(new ScreenId(3)), "ids nunca são reutilizados");
        }

        [Test]
        public void Focus_OnUnknownScreen_Throws() =>
            Assert.Throws<System.InvalidOperationException>(() => Workspace.Empty.WithFocus(new ScreenId(5)));

        [Test]
        public void Focus_None_IsAllowed()
        {
            var ws = Workspace.Empty.WithScreenAdded(Screen(1)).WithFocus(new ScreenId(1)).WithFocus(ScreenId.None);
            Assert.That(ws.FocusedScreenId.IsNone, Is.True);
        }
    }
}
