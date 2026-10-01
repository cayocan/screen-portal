using System.Collections.Generic;
using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class LayoutSyncTests
    {
        private sealed class Clock : IClock
        {
            public double Now;
            public double NowSeconds => Now;
        }

        private sealed class Rig
        {
            public readonly WorkspaceStore Store = new WorkspaceStore();
            public readonly Clock Clock = new Clock();
            public readonly List<LayoutChangedMessage> Sent = new List<LayoutChangedMessage>();
            public readonly LayoutSyncSettings Settings = new LayoutSyncSettings();
            public bool Linked = true;
            public readonly LayoutSync Sync;

            public Rig() => Sync = new LayoutSync(Store, () => Linked, m => Sent.Add((LayoutChangedMessage)m), Clock, Settings);

            public ScreenId Add(float az, ScreenSource source = default)
            {
                Store.Dispatch(new CreateScreen(Resolution.Create(2560, 1440), Placement.OnSurface(az, 0f, 1.3f), 1f, 0f, "", source));
                return Store.State.Screens[Store.State.Screens.Count - 1].Id;
            }

            public void Run(double seconds)
            {
                double end = Clock.Now + seconds;
                for (; Clock.Now < end; Clock.Now += 0.05) Sync.Tick();
            }
        }

        [Test]
        public void SendsTheAnglesOfTheVideoScreens_HalfASecondAfterTheLastChange()
        {
            var r = new Rig();
            var a = r.Add(0f);
            r.Add(30f, ScreenSource.Test);
            r.Run(0.3);
            Assert.That(r.Sent, Is.Empty, "ainda dentro da espera");
            r.Run(0.4);
            Assert.That(r.Sent.Count, Is.EqualTo(1));
            Assert.That(r.Sent[0].Screens.Count, Is.EqualTo(1), "tela de teste fica de fora");
            Assert.That(r.Sent[0].Screens[0].ScreenKey, Is.EqualTo((uint)a.Value));
            Assert.That(r.Sent[0].Screens[0].AzimuthDeg, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(r.Sent[0].Screens[0].HalfWidthDeg, Is.GreaterThan(10f));
        }

        [Test]
        public void MovingAScreen_SendsAgain_ButNothingChangedSendsNothing()
        {
            var r = new Rig();
            var a = r.Add(0f);
            r.Run(1);
            r.Store.Dispatch(new SetFocus(a)); // não muda a disposição
            r.Run(1);
            Assert.That(r.Sent.Count, Is.EqualTo(1));
            r.Store.Dispatch(new MoveScreenOnSurface(a, 25f, 5f));
            r.Run(1);
            Assert.That(r.Sent.Count, Is.EqualTo(2));
            Assert.That(r.Sent[1].Screens[0].AzimuthDeg, Is.EqualTo(25f).Within(1e-3f));
        }

        [Test]
        public void LooseScreens_AreLeftOut()
        {
            var r = new Rig();
            var a = r.Add(0f);
            r.Add(30f);
            r.Store.Dispatch(new SetScreenLoose(a, Pose.At(new Vec3(0f, 1f, 1f))));
            r.Run(1);
            Assert.That(r.Sent[0].Screens.Count, Is.EqualTo(1));
        }

        [Test]
        public void TurnedOff_SendsNothing_TurningOnSendsTheCurrentLayout()
        {
            var r = new Rig();
            r.Settings.Toggle();
            r.Add(0f);
            r.Run(1);
            Assert.That(r.Sent, Is.Empty);
            r.Settings.Toggle();
            r.Run(1);
            Assert.That(r.Sent.Count, Is.EqualTo(1));
        }

        [Test]
        public void MenuToggle_TurnsTheArrangementOffAndOn()
        {
            var r = new Rig();
            var menu = new ScreenPortal.App.Menu.WristMenu(r.Store, new CommandBus(r.Store, new ICommandObserver[0]),
                new ScreenPortal.App.Menu.FixedHeadPose(Pose.Identity), arrange: r.Settings);
            int index = -1;
            for (int i = 0; i < menu.ItemCount; i++)
                if (menu.Label(i) == ScreenPortal.App.Menu.WristMenu.AutoArrangeOn) index = i;
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "item no menu principal, começa ligado");
            menu.Invoke(index);
            Assert.That(r.Settings.Enabled, Is.False);
            Assert.That(menu.Label(index), Is.EqualTo(ScreenPortal.App.Menu.WristMenu.AutoArrangeOff));
        }

        [Test]
        public void Reconnecting_SendsTheLayoutAgain()
        {
            var r = new Rig();
            r.Add(0f);
            r.Run(1);
            r.Linked = false;
            r.Run(0.2);
            r.Linked = true;
            r.Run(1);
            Assert.That(r.Sent.Count, Is.EqualTo(2), "host novo não conhece o arranjo");
        }
    }
}
