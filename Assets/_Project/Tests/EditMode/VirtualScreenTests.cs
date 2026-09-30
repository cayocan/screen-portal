using NUnit.Framework;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class VirtualScreenTests
    {
        private static VirtualScreen Screen(float width = 1.4f, float curvature = 0f) =>
            new VirtualScreen(new ScreenId(1), Resolution.Create(2560, 1080), Placement.Default, width, curvature);

        [Test]
        public void HeightDerivesFromAspectRatio() =>
            Assert.That(Screen(1.4f).HeightMeters, Is.EqualTo(1.4f * 1080f / 2560f).Within(1e-5f));

        [Test]
        public void OrientationComesFromResolution()
        {
            var portrait = new VirtualScreen(new ScreenId(1), Resolution.Create(1440, 2560), Placement.Default, 0.6f);
            Assert.That(portrait.Orientation, Is.EqualTo(Orientation.Portrait));
            Assert.That(portrait.HeightMeters, Is.GreaterThan(portrait.WidthMeters));
        }

        [TestCase(0.01f, ScreenLimits.MinWidthMeters)]
        [TestCase(50f, ScreenLimits.MaxWidthMeters)]
        [TestCase(1.5f, 1.5f)]
        public void WidthIsClamped(float requested, float expected) =>
            Assert.That(Screen(requested).WidthMeters, Is.EqualTo(expected));

        [Test]
        public void CurvatureIsClampedAndRemembered()
        {
            var s = Screen().WithCurvature(1.7f);
            Assert.That(s.Curvature, Is.EqualTo(1f));
            Assert.That(s.LastNonZeroCurvature, Is.EqualTo(1f));

            var flat = s.WithCurvature(0f);
            Assert.That(flat.Curvature, Is.EqualTo(0f));
            Assert.That(flat.LastNonZeroCurvature, Is.EqualTo(1f));
        }

        [Test]
        public void NewScreen_LastCurvatureDefaultsToMedium() =>
            Assert.That(Screen().LastNonZeroCurvature, Is.EqualTo(ScreenLimits.DefaultCurvature));

        [Test]
        public void WithMethods_ReturnNewInstances_AndKeepOtherFields()
        {
            var s = Screen();
            var moved = s.WithPlacement(Placement.OnSurface(20f, 5f, 2f));
            Assert.That(moved, Is.Not.SameAs(s));
            Assert.That(moved.Resolution, Is.EqualTo(s.Resolution));
            Assert.That(moved.WidthMeters, Is.EqualTo(s.WidthMeters));
            Assert.That(s.Placement, Is.EqualTo(Placement.Default));
        }

        [Test]
        public void NoneId_IsRejected() =>
            Assert.Throws<System.ArgumentException>(() =>
                new VirtualScreen(ScreenId.None, Resolution.Create(1920, 1080), Placement.Default, 1f));

        [Test]
        public void InvalidResolution_IsRejected() =>
            Assert.Throws<System.ArgumentException>(() =>
                new VirtualScreen(new ScreenId(1), default(Resolution), Placement.Default, 1f));
    }
}
