using NUnit.Framework;
using ScreenPortal.App.Screens;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class FrameRateRulesTests
    {
        // --- FrameRates ---

        [TestCase(true, 60)]
        [TestCase(false, 15)]
        public void Auto_FollowsTheFocus(bool focused, int fps) =>
            Assert.That(FrameRates.RequestedFps(FrameRateMode.Auto, focused), Is.EqualTo(fps));

        [TestCase(true)]
        [TestCase(false)]
        public void ManualModes_IgnoreTheFocus(bool focused)
        {
            Assert.That(FrameRates.RequestedFps(FrameRateMode.Video, focused), Is.EqualTo(60));
            Assert.That(FrameRates.RequestedFps(FrameRateMode.Fixed(30), focused), Is.EqualTo(30));
            Assert.That(FrameRates.RequestedFps(FrameRateMode.Fixed(15), focused), Is.EqualTo(15));
            Assert.That(FrameRates.RequestedFps(FrameRateMode.Eco, focused), Is.EqualTo(5));
        }

        [Test]
        public void Bitrate_IsProportionalToTheFps_WithAFloor_AndVideoIsHigher()
        {
            Assert.That(FrameRates.Kbps(FrameRateMode.Auto, 60), Is.EqualTo(20000));
            Assert.That(FrameRates.Kbps(FrameRateMode.Auto, 15), Is.EqualTo(5000));
            Assert.That(FrameRates.Kbps(FrameRateMode.Eco, 5), Is.EqualTo(2000));
            Assert.That(FrameRates.Kbps(FrameRateMode.Video, 60), Is.EqualTo(30000));
            Assert.That(FrameRates.Kbps(FrameRateMode.Video, 30), Is.EqualTo(15000), "Vídeo rebaixado pelo orçamento: proporcional");
        }

        // --- GazeFocus ---

        private static Workspace TwoScreens(out ScreenId front, out ScreenId right)
        {
            var ws = Workspace.Empty;
            new CreateScreen(Resolution.Create(2560, 1440), Placement.OnSurface(0f, 0f, 1.3f), 1f).Apply(ws, out ws);
            new CreateScreen(Resolution.Create(2560, 1440), Placement.OnSurface(40f, 0f, 1.3f), 1f).Apply(ws, out ws);
            front = ws.Screens[0].Id;
            right = ws.Screens[1].Id;
            return ws;
        }

        private static Pose Looking(float yawDeg) => new Pose(LayoutSurface.DefaultCenter, Quat.AngleAxis(yawDeg, Vec3.Up));

        [Test]
        public void Gaze_BecomesFocusOnlyAfterTheAcquireTime()
        {
            var ws = TwoScreens(out var front, out _);
            var gaze = new GazeFocus();
            Assert.That(gaze.Tick(0, Looking(0f), ws), Is.EqualTo(ScreenId.None));
            Assert.That(gaze.Tick(0.2, Looking(0f), ws), Is.EqualTo(ScreenId.None));
            Assert.That(gaze.Tick(0.31, Looking(0f), ws), Is.EqualTo(front));
        }

        [Test]
        public void Gaze_ShortGlanceAway_KeepsTheFocus_LongerSwitches()
        {
            var ws = TwoScreens(out var front, out var right);
            var gaze = new GazeFocus();
            gaze.Tick(0, Looking(0f), ws);
            gaze.Tick(0.4, Looking(0f), ws);
            Assert.That(gaze.Tick(0.6, Looking(40f), ws), Is.EqualTo(front), "olhada curta");
            Assert.That(gaze.Tick(0.95, Looking(40f), ws), Is.EqualTo(right), "0,35 s na outra: troca");
        }

        [Test]
        public void Gaze_NothingInView_ReleasesAfterTheReleaseTime()
        {
            var ws = TwoScreens(out var front, out _);
            var gaze = new GazeFocus();
            gaze.Tick(0, Looking(0f), ws);
            gaze.Tick(0.4, Looking(0f), ws);
            Assert.That(gaze.Tick(1.0, Looking(-90f), ws), Is.EqualTo(front));
            Assert.That(gaze.Tick(1.45, Looking(-90f), ws), Is.EqualTo(ScreenId.None));
        }

        [Test]
        public void Gaze_IgnoresScreensBeyondTheAngle_AndTestScreens()
        {
            var ws = Workspace.Empty;
            new CreateScreen(Resolution.Create(1920, 1080), Placement.OnSurface(0f, 0f, 1.3f), 1f, 0f, "t", ScreenSource.Test).Apply(ws, out ws);
            new CreateScreen(Resolution.Create(1920, 1080), Placement.OnSurface(40f, 0f, 1.3f), 1f).Apply(ws, out ws);
            var gaze = new GazeFocus();
            gaze.Tick(0, Looking(0f), ws);
            Assert.That(gaze.Tick(1, Looking(0f), ws), Is.EqualTo(ScreenId.None), "tela de teste na frente; a de vídeo a 40°");
        }

        // --- BandwidthBudget ---

        private static RateRequest Req(int id, FrameRateMode mode, int fps) =>
            new RateRequest { Screen = new ScreenId(id), Mode = mode, Fps = fps, Order = id };

        private static int[] Grant(ScreenId focus, out bool warning, params RateRequest[] requests)
        {
            var granted = new int[requests.Length];
            warning = BandwidthBudget.Allocate(requests, focus, granted);
            return granted;
        }

        [Test]
        public void FourAt60_Fits()
        {
            var g = Grant(new ScreenId(1), out bool warning, Req(1, FrameRateMode.Auto, 60), Req(2, FrameRateMode.Video, 60),
                Req(3, FrameRateMode.Video, 60), Req(4, FrameRateMode.Video, 60));
            Assert.That(g, Is.EqualTo(new[] { 60, 60, 60, 60 }));
            Assert.That(warning, Is.False);
        }

        [Test]
        public void FiveVideos_TheNewestDropsTo30_WithAWarning()
        {
            var g = Grant(ScreenId.None, out bool warning, Req(1, FrameRateMode.Video, 60), Req(2, FrameRateMode.Video, 60),
                Req(3, FrameRateMode.Video, 60), Req(4, FrameRateMode.Video, 60), Req(5, FrameRateMode.Video, 60));
            Assert.That(g, Is.EqualTo(new[] { 60, 60, 60, 60, 30 }));
            Assert.That(warning, Is.True);
        }

        [Test]
        public void TheFocusIsNeverCut()
        {
            var g = Grant(new ScreenId(5), out _, Req(1, FrameRateMode.Video, 60), Req(2, FrameRateMode.Video, 60),
                Req(3, FrameRateMode.Video, 60), Req(4, FrameRateMode.Video, 60), Req(5, FrameRateMode.Video, 60));
            Assert.That(g[4], Is.EqualTo(60));
            Assert.That(g, Is.EqualTo(new[] { 60, 60, 60, 30, 60 }));
        }

        [Test]
        public void AutoBeforeManual_EcoFirst_AndNoWarningWhenAutoIsCut()
        {
            // 5 telas a 60: um Auto em foco + 4 Vídeo → quem cai é um Vídeo (o Auto está em foco); com um Auto fora do foco
            // pedindo 60 (não acontece pelas regras, mas o orçamento não deve confiar nisso), ele cai antes do Vídeo.
            var g = Grant(new ScreenId(1), out bool warning, Req(1, FrameRateMode.Auto, 60), Req(2, FrameRateMode.Video, 60),
                Req(3, FrameRateMode.Video, 60), Req(4, FrameRateMode.Video, 60), Req(5, FrameRateMode.Auto, 60));
            Assert.That(g, Is.EqualTo(new[] { 60, 60, 60, 60, 30 }));
            Assert.That(warning, Is.False, "cortar Auto é o funcionamento normal");
        }

        [Test]
        public void SeventhScreen_GoesTo5_LowestPriorityAndNewestFirst()
        {
            var g = Grant(new ScreenId(1), out _, Req(1, FrameRateMode.Auto, 60), Req(2, FrameRateMode.Auto, 15),
                Req(3, FrameRateMode.Auto, 15), Req(4, FrameRateMode.Eco, 5), Req(5, FrameRateMode.Auto, 15),
                Req(6, FrameRateMode.Fixed(15), 15), Req(7, FrameRateMode.Auto, 15));
            Assert.That(g, Is.EqualTo(new[] { 60, 15, 15, 5, 15, 15, 5 }), "a 7ª (Auto, a mais nova) vai a 5; o Eco já estava");
        }
    }
}
