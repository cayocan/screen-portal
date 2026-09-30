using NUnit.Framework;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Tests
{
    public class ResolutionCatalogTests
    {
        [Test]
        public void SixAspects_InTheBriefOrder()
        {
            Assert.That(ResolutionCatalog.AspectCount, Is.EqualTo(6));
            string[] names = { "16:9", "16:10", "21:9", "32:9", "1:1", "4:3" };
            for (int i = 0; i < names.Length; i++)
                Assert.That(ResolutionCatalog.Name(i, Orientation.Landscape), Is.EqualTo(names[i]));
            Assert.That(ResolutionCatalog.Name(0, Orientation.Portrait), Is.EqualTo("9:16"));
            Assert.That(ResolutionCatalog.Name(4, Orientation.Portrait), Is.EqualTo("1:1"));
        }

        [Test]
        public void EveryResolution_IsValid_MultipleOf8_AtMost4096_AndCloseToItsAspect()
        {
            for (int a = 0; a < ResolutionCatalog.AspectCount; a++)
            {
                float nominal = ResolutionCatalog.FrameAspect(a, Orientation.Landscape);
                Assert.That(ResolutionCatalog.Count(a), Is.GreaterThan(0));
                for (int i = 0; i < ResolutionCatalog.Count(a); i++)
                {
                    var r = ResolutionCatalog.Get(a, i, Orientation.Landscape);
                    Assert.That(Resolution.TryCreate(r.Width, r.Height, out _, out var error), Is.True, error);
                    Assert.That(r.Width, Is.LessThanOrEqualTo(4096));
                    Assert.That(r.AspectRatio, Is.EqualTo(nominal).Within(nominal * 0.03f), r.ToString());
                }
            }
        }

        [Test]
        public void Portrait_RotatesResolutionFrameAndLabel()
        {
            var landscape = ResolutionCatalog.Get(0, 1, Orientation.Landscape);
            var portrait = ResolutionCatalog.Get(0, 1, Orientation.Portrait);
            Assert.That(landscape, Is.EqualTo(Resolution.Create(2560, 1440)));
            Assert.That(portrait, Is.EqualTo(Resolution.Create(1440, 2560)));
            Assert.That(ResolutionCatalog.Label(0, 1, Orientation.Portrait), Is.EqualTo("1440×2560"));
            Assert.That(ResolutionCatalog.FrameAspect(0, Orientation.Portrait), Is.EqualTo(9f / 16f).Within(1e-5f));
        }

        [Test]
        public void Labels_AreCached()
        {
            Assert.That(ResolutionCatalog.Label(1, 0, Orientation.Landscape),
                Is.SameAs(ResolutionCatalog.Label(1, 0, Orientation.Landscape)));
        }
    }
}
