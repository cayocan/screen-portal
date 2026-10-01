using System;
using System.Collections.Generic;
using NUnit.Framework;
using ScreenPortal.App;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Menu;
using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class RateSyncTests
    {
        private sealed class Links : IScreenLinks
        {
            public readonly Dictionary<ScreenId, ScreenLinkState> State = new Dictionary<ScreenId, ScreenLinkState>();
#pragma warning disable 67
            public event Action<ScreenId> LinkChanged;
#pragma warning restore 67
            public bool TryGetLink(ScreenId screen, out ScreenLinkState state, out string detail)
            {
                detail = string.Empty;
                return State.TryGetValue(screen, out state);
            }
        }

        private sealed class Head : IHeadPoseSource
        {
            public Pose HeadPose { get; set; } = new Pose(LayoutSurface.DefaultCenter, Quat.Identity);
            public void Look(float yawDeg) => HeadPose = new Pose(LayoutSurface.DefaultCenter, Quat.AngleAxis(yawDeg, Vec3.Up));
        }

        private sealed class Clock : IClock
        {
            public double Now;
            public double NowSeconds => Now;
        }

        private sealed class Rig
        {
            public readonly WorkspaceStore Store = new WorkspaceStore();
            public readonly Links Links = new Links();
            public readonly Head Head = new Head();
            public readonly Clock Clock = new Clock();
            public readonly List<SetStreamRateMessage> Sent = new List<SetStreamRateMessage>();
            public bool Linked = true;
            public readonly RateSync Sync;

            public Rig() => Sync = new RateSync(Store, Links, () => Linked, m => Sent.Add((SetStreamRateMessage)m), Head, Clock);

            public ScreenId Add(float azimuth, FrameRateMode mode = default)
            {
                Store.Dispatch(new CreateScreen(Resolution.Create(2560, 1440), Placement.OnSurface(azimuth, 0f, 1.3f), 1f));
                var id = Store.State.Screens[Store.State.Screens.Count - 1].Id;
                if (!mode.Equals(default(FrameRateMode))) Store.Dispatch(new SetFrameRateMode(id, mode));
                Links.State[id] = ScreenLinkState.Live;
                return id;
            }

            public void Run(double seconds)
            {
                double end = Clock.Now + seconds;
                for (; Clock.Now < end; Clock.Now += 0.05) Sync.Tick();
            }

            public SetStreamRateMessage LastFor(ScreenId id) => Sent.FindLast(m => m.ScreenKey == (uint)id.Value);
        }

        [Test]
        public void LookingAtAScreen_GivesIt60_TheOtherGets15_EachSentOnce()
        {
            var r = new Rig();
            var front = r.Add(0f);
            var right = r.Add(40f);
            r.Run(1.0);
            Assert.That(r.LastFor(front).Fps, Is.EqualTo((byte)60));
            Assert.That(r.LastFor(front).Kbps, Is.EqualTo(20000u));
            Assert.That(r.LastFor(right).Fps, Is.EqualTo((byte)15));
            Assert.That(r.LastFor(right).Kbps, Is.EqualTo(5000u));
            int count = r.Sent.Count;
            r.Run(1.0);
            Assert.That(r.Sent.Count, Is.EqualTo(count), "nada muda: nada é enviado");
            Assert.That(r.Sync.Focus, Is.EqualTo(front));
        }

        [Test]
        public void TurningTheHead_SwapsTheRates()
        {
            var r = new Rig();
            var front = r.Add(0f);
            var right = r.Add(40f);
            r.Run(1.0);
            r.Head.Look(40f);
            r.Run(1.0);
            Assert.That(r.LastFor(right).Fps, Is.EqualTo((byte)60));
            Assert.That(r.LastFor(front).Fps, Is.EqualTo((byte)15));
            Assert.That(r.Sync.TryGetGranted(right, out int fps) && fps == 60, Is.True);
        }

        [Test]
        public void ManualModeWins_VideoStaysAt60_EcoStaysAt5_WhileLookedAt()
        {
            var r = new Rig();
            var eco = r.Add(0f, FrameRateMode.Eco);
            var video = r.Add(40f, FrameRateMode.Video);
            r.Run(1.0);
            Assert.That(r.LastFor(eco).Fps, Is.EqualTo((byte)5));
            Assert.That(r.LastFor(video).Fps, Is.EqualTo((byte)60));
            Assert.That(r.LastFor(video).Kbps, Is.EqualTo(30000u));
        }

        [Test]
        public void StreamRestart_ResendsTheRate()
        {
            var r = new Rig();
            var right = r.Add(40f);
            r.Run(0.5);
            int sent = r.Sent.FindAll(m => m.ScreenKey == (uint)right.Value).Count;
            r.Links.State[right] = ScreenLinkState.Connecting; // pipeline novo no host, começa no padrão
            r.Run(0.2);
            r.Links.State[right] = ScreenLinkState.Live;
            r.Run(0.2);
            Assert.That(r.Sent.FindAll(m => m.ScreenKey == (uint)right.Value).Count, Is.EqualTo(sent + 1));
        }

        [Test]
        public void FiveVideos_Warn()
        {
            var r = new Rig();
            for (int i = 0; i < 5; i++) r.Add(-90f + i * 30f, FrameRateMode.Video);
            r.Run(0.2);
            Assert.That(r.Sync.Warning, Is.True);
        }

        [Test]
        public void NotLinked_SendsNothing()
        {
            var r = new Rig { Linked = false };
            r.Add(0f);
            r.Run(1.0);
            Assert.That(r.Sent, Is.Empty);
        }
    }
}
