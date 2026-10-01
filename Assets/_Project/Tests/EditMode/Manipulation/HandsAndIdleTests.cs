using System;
using NUnit.Framework;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Manipulation;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class HandsAndIdleTests
    {
        // Cabeça na origem olhando para +Z: palma "virada para fora" = normal +Z; lateral = X.
        private static readonly Pose Head = new Pose(new Vec3(0f, 1.6f, 0f), Quat.Identity);
        private static readonly Vec3 Away = new Vec3(0f, 0f, 1f);
        private static readonly Vec3 TowardUser = new Vec3(0f, 0f, -1f);

        private const double Frame = 1.0 / 72.0;

        /// <summary>Balança a palma de um lado para o outro (amplitude e frequência), devolve quantas vezes disparou.</summary>
        private static int Wave(WaveDetector detector, double seconds, float amplitudeM, double hz, Vec3 normal, bool open, double start = 0)
        {
            int fired = 0;
            for (double t = 0; t < seconds; t += Frame)
            {
                float x = amplitudeM * (float)Math.Sin(2 * Math.PI * hz * t);
                var palm = new Vec3(x, 1.5f, 0.4f);
                if (detector.Feed(start + t, palm, normal, open, Head)) fired++;
            }
            return fired;
        }

        [Test]
        public void Wave_OpenPalmFacingAway_SideToSide_FiresOnce()
        {
            Assert.That(Wave(new WaveDetector(), 1.4, 0.08f, 2.0, Away, true), Is.EqualTo(1));
        }

        [Test]
        public void Wave_KeepsWaving_FiresAgainOnlyAfterTheCooldown()
        {
            Assert.That(Wave(new WaveDetector(), 4.0, 0.08f, 2.0, Away, true), Is.EqualTo(2));
        }

        [TestCase(false, 0.08f, 2.0)]   // mão fechada / pinça
        [TestCase(true, 0.02f, 2.0)]    // tremidinha: amplitude pequena
        [TestCase(true, 0.08f, 0.2)]    // arraste lento: uma ida e volta em 5 s
        public void NotAWave(bool open, float amplitude, double hz)
        {
            Assert.That(Wave(new WaveDetector(), 3.0, amplitude, hz, Away, open), Is.EqualTo(0));
        }

        [Test]
        public void PalmFacingTheUser_IsNotAWave()
        {
            Assert.That(Wave(new WaveDetector(), 3.0, 0.08f, 2.0, TowardUser, true), Is.EqualTo(0));
        }

        private sealed class FakeInput : IManipulationInput
        {
            public ManipulationInput Next;
            public bool Present = true;

            public bool Read(out ManipulationInput input)
            {
                input = Next;
                return Present;
            }
        }

        private sealed class Clock : IClock
        {
            public double Now;
            public double NowSeconds => Now;
        }

        private static ManipulationInput Controller(float x = 0f) => new ManipulationInput
        {
            HasRay = true, Origin = new Vec3(x, 1.2f, 0.3f), Direction = new Vec3(0f, 0f, 1f),
        };

        private static (PointerFilter, FakeInput, Clock, HandsMode) Rig()
        {
            var raw = new FakeInput { Next = Controller() };
            var clock = new Clock();
            var hands = new HandsMode();
            return (new PointerFilter(raw, hands, new FixedHeadPose(Head), clock), raw, clock, hands);
        }

        [Test]
        public void StillController_LosesItsRay_AfterTheIdleTime()
        {
            var (filter, _, clock, _) = Rig();
            Assert.That(filter.Read(out var input) && input.HasRay, Is.True);
            clock.Now = PointerFilter.IdleSeconds - 0.1;
            Assert.That(filter.Read(out input) && input.HasRay, Is.True);
            clock.Now = PointerFilter.IdleSeconds + 0.1;
            Assert.That(filter.Read(out input), Is.True, "o controle continua presente (botões funcionam)");
            Assert.That(input.HasRay, Is.False);
        }

        [Test]
        public void MovingOrPressing_BringsTheRayBack()
        {
            var (filter, raw, clock, _) = Rig();
            filter.Read(out _);
            clock.Now = 10;
            Assert.That(filter.Read(out var input) && input.HasRay, Is.False);

            raw.Next = Controller(0.03f); // 3 cm
            Assert.That(filter.Read(out input) && input.HasRay, Is.True);

            clock.Now = 20;
            Assert.That(filter.Read(out input) && input.HasRay, Is.False);
            var pressed = Controller(0.03f);
            pressed.SelectHeld = true;
            raw.Next = pressed;
            Assert.That(filter.Read(out input) && input.HasRay, Is.True);
        }

        [Test]
        public void SlowDrift_BelowTheThreshold_StillCountsAsStill()
        {
            var (filter, raw, clock, _) = Rig();
            filter.Read(out _);
            for (int i = 1; i <= 40; i++)
            {
                clock.Now = i * 0.1;
                raw.Next = Controller(0.0002f * i); // 8 mm em 4 s, abaixo de 1 cm
                filter.Read(out _);
            }
            Assert.That(filter.Read(out var input) && input.HasRay, Is.False);
        }

        private static ManipulationInput Hand(Vec3 palm, Vec3 normal, bool open) => new ManipulationInput
        {
            HasRay = true, Origin = palm, Direction = new Vec3(0f, 0f, 1f), IsHand = true,
            PalmPosition = palm, PalmNormal = normal, HandOpen = open,
        };

        [Test]
        public void Waving_TogglesTheHands_DisabledHandsAreAbsent_ControllersAreNot()
        {
            var (filter, raw, clock, hands) = Rig();
            bool changed = false;
            hands.Changed += _ => changed = true;
            for (double t = 0; t < 1.4; t += Frame)
            {
                clock.Now = t;
                float x = 0.08f * (float)Math.Sin(2 * Math.PI * 2.0 * t);
                raw.Next = Hand(new Vec3(x, 1.5f, 0.4f), Away, true);
                filter.Read(out _);
            }
            Assert.That(hands.Enabled, Is.False);
            Assert.That(changed, Is.True);

            raw.Next = Hand(new Vec3(0f, 1.5f, 0.4f), Away, false);
            Assert.That(filter.Read(out _), Is.False, "mãos desligadas: o ponteiro some");

            raw.Next = Controller();
            Assert.That(filter.Read(out var input) && input.HasRay, Is.True, "controle não depende do modo das mãos");
        }
    }
}
