using NUnit.Framework;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Tests
{
    public class ResolutionTests
    {
        [TestCase(2560, 1080, Orientation.Landscape)]
        [TestCase(1440, 2560, Orientation.Portrait)]
        [TestCase(1440, 1440, Orientation.Landscape)]
        [TestCase(3840, 2160, Orientation.Landscape)]
        [TestCase(640, 480, Orientation.Landscape)]
        [TestCase(480, 640, Orientation.Portrait)]
        public void ValidResolutions_AreAccepted(int w, int h, Orientation expected)
        {
            Assert.That(Resolution.TryCreate(w, h, out var r, out var error), Is.True, error);
            Assert.That(r.Width, Is.EqualTo(w));
            Assert.That(r.Height, Is.EqualTo(h));
            Assert.That(r.Orientation, Is.EqualTo(expected));
            Assert.That(r.IsValid, Is.True);
        }

        [TestCase(1366, 768, "múltiplos de 8")]
        [TestCase(320, 240, "mínimo")]
        [TestCase(480, 480, "mínimo")]
        [TestCase(5120, 1440, "4096")]
        [TestCase(1440, 5120, "4096")]
        [TestCase(0, 1080, "positivas")]
        public void InvalidResolutions_AreRejectedWithReason(int w, int h, string reasonFragment)
        {
            Assert.That(Resolution.TryCreate(w, h, out _, out var error), Is.False);
            Assert.That(error, Does.Contain(reasonFragment));
        }

        [Test]
        public void Create_ThrowsOnInvalid() =>
            Assert.Throws<System.ArgumentException>(() => Resolution.Create(1366, 768));

        [Test]
        public void Rotated_SwapsWidthAndHeight()
        {
            var r = Resolution.Create(2560, 1440).Rotated();
            Assert.That(r.Width, Is.EqualTo(1440));
            Assert.That(r.Height, Is.EqualTo(2560));
            Assert.That(r.Orientation, Is.EqualTo(Orientation.Portrait));
        }

        [Test]
        public void AspectRatio_IsWidthOverHeight() =>
            Assert.That(Resolution.Create(2560, 1080).AspectRatio, Is.EqualTo(2560f / 1080f).Within(1e-5f));

        [Test]
        public void Default_IsInvalid() => Assert.That(default(Resolution).IsValid, Is.False);
    }
}
