using NUnit.Framework;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Tests
{
    public class ResolutionFitTests
    {
        [TestCase(2560, 1080, 2560, 1080)]
        [TestCase(1366, 768, 1360, 768)]
        [TestCase(5120, 1440, 4096, 1152)]
        [TestCase(7680, 2160, 4096, 1152)]
        [TestCase(1440, 2560, 1440, 2560)]
        public void FitsToEncoderRules(int w, int h, int expectedW, int expectedH)
        {
            var r = ResolutionFit.FromCapture(w, h);
            Assert.That(r.IsValid, Is.True);
            Assert.That(r.Width, Is.EqualTo(expectedW));
            Assert.That(r.Height, Is.EqualTo(expectedH));
        }

        [Test]
        public void TooSmall_IsInvalid() => Assert.That(ResolutionFit.FromCapture(320, 200).IsValid, Is.False);
    }
}
