using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Session;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class SessionTests
    {
        private static Workspace Sample()
        {
            var ws = Workspace.Empty;
            new RecenterLayout(new Vec3(0.3f, 1.15f, -0.2f), 37.5f).Apply(ws, out ws);
            new SetLayoutMode(LayoutMode.Sphere).Apply(ws, out ws);
            new CreateScreen(Resolution.Create(2560, 1080), Placement.OnSurface(-12.25f, 3f, 1.4f), 1.2f, 0f,
                "DISPLAY1", ScreenSource.Mirror(1)).Apply(ws, out ws);
            new CreateScreen(Resolution.Create(1440, 2560), Placement.Loose(new Pose(new Vec3(1f, 1.3f, 0.8f),
                Quat.AngleAxis(25f, Vec3.Up))), 0.6f, 0.6f, "Ret | rato\nnovo", ScreenSource.Virtual).Apply(ws, out ws);
            return ws;
        }

        [Test]
        public void WindowMirrors_AreNotSaved()
        {
            var ws = Sample();
            new CreateScreen(Resolution.Create(1096, 824), null, 0.55f, 0f, "Bloco de Notas", ScreenSource.Window(77)).Apply(ws, out ws);
            Assert.That(SessionCodec.TryDecode(SessionCodec.Encode(ws), out var back), Is.True);
            Assert.That(back.Screens.Count, Is.EqualTo(2), "a janela pode não existir ao reabrir (ADR 0018)");
        }

        [Test]
        public void RoundTrip_KeepsEveryScreenField_TheSurface_TheFocus_AndTheNextId()
        {
            var ws = Sample();
            Assert.That(SessionCodec.TryDecode(SessionCodec.Encode(ws), out var back), Is.True);

            Assert.That(back.Screens.Count, Is.EqualTo(2));
            for (int i = 0; i < 2; i++)
            {
                var a = ws.Screens[i];
                var b = back.Screens[i];
                Assert.That(b.Id, Is.EqualTo(a.Id));
                Assert.That(b.Resolution, Is.EqualTo(a.Resolution));
                Assert.That(b.Placement, Is.EqualTo(a.Placement));
                Assert.That(b.WidthMeters, Is.EqualTo(a.WidthMeters));
                Assert.That(b.Curvature, Is.EqualTo(a.Curvature));
                Assert.That(b.LastNonZeroCurvature, Is.EqualTo(a.LastNonZeroCurvature));
                Assert.That(b.FrameRateMode, Is.EqualTo(a.FrameRateMode));
                Assert.That(b.Label, Is.EqualTo(a.Label));
                Assert.That(b.Source, Is.EqualTo(a.Source));
            }
            Assert.That(back.Surface.Mode, Is.EqualTo(LayoutMode.Sphere));
            Assert.That(back.Surface.Center, Is.EqualTo(ws.Surface.Center));
            Assert.That(back.Surface.HeadingDeg, Is.EqualTo(ws.Surface.HeadingDeg));
            Assert.That(back.Surface.Snap, Is.EqualTo(ws.Surface.Snap));
            Assert.That(back.FocusedScreenId, Is.EqualTo(ws.FocusedScreenId));
            Assert.That(back.NextScreenId, Is.EqualTo(ws.NextScreenId));
        }

        [TestCase("")]
        [TestCase("lixo")]
        [TestCase("screenportal-session 999\nnext 3")]
        [TestCase("screenportal-session 1\nscreen 1 abc")]
        public void Garbage_IsRejected(string text)
        {
            Assert.That(SessionCodec.TryDecode(text, out _), Is.False);
        }

        [Test]
        public void Restore_FixesANextIdBehindTheScreens_AndAFocusThatDoesNotExist()
        {
            var ws = Sample();
            var restored = Workspace.Restore(ws.Screens, new ScreenId(99), 1, ws.Surface);
            Assert.That(restored.NextScreenId, Is.GreaterThan(ws.Screens[1].Id.Value), "id novo não colide com um aluguel do host");
            Assert.That(restored.FocusedScreenId, Is.EqualTo(ScreenId.None));
        }

        private sealed class MemoryStore : ISessionStore
        {
            public string Text;
            public int Saves;
            public string Load() => Text;
            public void Save(string text)
            {
                Text = text;
                Saves++;
            }
        }

        private sealed class Clock : IClock
        {
            public double Now;
            public double NowSeconds => Now;
        }

        [Test]
        public void Autosave_SavesAfterAChange_AtMostEveryInterval_AndNotWhenNothingChanged()
        {
            var store = new WorkspaceStore();
            var session = new MemoryStore();
            var clock = new Clock();
            using (var autosave = new SessionAutosave(store, session, clock))
            {
                autosave.Tick();
                Assert.That(session.Saves, Is.EqualTo(0), "nada mudou");

                store.Dispatch(new CreateScreen(Resolution.Create(1920, 1080), null, 1f));
                autosave.Tick();
                Assert.That(session.Saves, Is.EqualTo(1));

                store.Dispatch(new CreateScreen(Resolution.Create(1920, 1080), null, 1f));
                clock.Now = SessionAutosave.IntervalSeconds * 0.5;
                autosave.Tick();
                Assert.That(session.Saves, Is.EqualTo(1), "espera o intervalo");
                clock.Now = SessionAutosave.IntervalSeconds * 1.1;
                autosave.Tick();
                Assert.That(session.Saves, Is.EqualTo(2));
                Assert.That(SessionCodec.TryDecode(session.Text, out var saved) && saved.Screens.Count == 2, Is.True);
            }
        }
    }
}
