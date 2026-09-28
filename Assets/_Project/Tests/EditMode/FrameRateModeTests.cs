using NUnit.Framework;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Tests
{
    public class FrameRateModeTests
    {
        [Test]
        public void Default_IsAuto() => Assert.That(default(FrameRateMode), Is.EqualTo(FrameRateMode.Auto));

        [TestCase(15)]
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(90)]
        public void Fixed_AcceptsSupportedRates(int fps)
        {
            var mode = FrameRateMode.Fixed(fps);
            Assert.That(mode.Kind, Is.EqualTo(FrameRateKind.Fixed));
            Assert.That(mode.FixedFps, Is.EqualTo(fps));
        }

        [TestCase(0)]
        [TestCase(45)]
        [TestCase(120)]
        public void Fixed_RejectsOtherRates(int fps) =>
            Assert.Throws<System.ArgumentOutOfRangeException>(() => FrameRateMode.Fixed(fps));

        [Test]
        public void Modes_AreDistinct()
        {
            Assert.That(FrameRateMode.Video, Is.Not.EqualTo(FrameRateMode.Eco));
            Assert.That(FrameRateMode.Fixed(60), Is.Not.EqualTo(FrameRateMode.Fixed(30)));
        }
    }
}
