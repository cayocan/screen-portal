using NUnit.Framework;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Tests
{
    public class CurvaturePresetsTests
    {
        [Test]
        public void FourPresets_InMenuOrder_MediumIsTheDefault()
        {
            Assert.That(CurvaturePresets.Count, Is.EqualTo(4));
            Assert.That(CurvaturePresets.Name(0), Is.EqualTo("Plana"));
            Assert.That(CurvaturePresets.Name(1), Is.EqualTo("Suave"));
            Assert.That(CurvaturePresets.Name(2), Is.EqualTo("Média"));
            Assert.That(CurvaturePresets.Name(3), Is.EqualTo("Envolvente"));
            Assert.That(CurvaturePresets.Value(0), Is.EqualTo(0f));
            Assert.That(CurvaturePresets.Value(1), Is.EqualTo(0.3f));
            Assert.That(CurvaturePresets.Value(2), Is.EqualTo(ScreenLimits.DefaultCurvature));
            Assert.That(CurvaturePresets.Value(3), Is.EqualTo(1f));
        }

        [TestCase(0f, 0)]
        [TestCase(0.3f, 1)]
        [TestCase(0.302f, 1)]
        [TestCase(0.6f, 2)]
        [TestCase(1f, 3)]
        [TestCase(0.45f, -1)]
        [TestCase(0.01f, -1)]
        public void IndexOf_FindsThePresetWithinTolerance(float curvature, int expected) =>
            Assert.That(CurvaturePresets.IndexOf(curvature), Is.EqualTo(expected));
    }
}
