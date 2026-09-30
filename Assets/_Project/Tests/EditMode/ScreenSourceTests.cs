using NUnit.Framework;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Tests
{
    public class ScreenSourceTests
    {
        [Test]
        public void Default_IsVirtual_WithVideo()
        {
            var source = default(ScreenSource);
            Assert.That(source.Kind, Is.EqualTo(ScreenSourceKind.Virtual));
            Assert.That(source.HasVideo, Is.True);
            Assert.That(source, Is.EqualTo(ScreenSource.Virtual));
        }

        [Test]
        public void Mirror_KeepsThePhysicalId_TestHasNoVideo()
        {
            var mirror = ScreenSource.Mirror(3);
            Assert.That(mirror.Kind, Is.EqualTo(ScreenSourceKind.Mirror));
            Assert.That(mirror.PhysicalMonitorId, Is.EqualTo(3u));
            Assert.That(mirror.HasVideo, Is.True);
            Assert.That(mirror, Is.Not.EqualTo(ScreenSource.Mirror(4)));
            Assert.That(ScreenSource.Test.HasVideo, Is.False);
        }

        [Test]
        public void CreateScreen_CarriesTheSource_AndWithMethodsKeepIt()
        {
            var result = new CreateScreen(Resolution.Create(2560, 1080), null, 1f, 0f, "DISPLAY1", ScreenSource.Mirror(1))
                .Apply(Workspace.Empty, out var ws);
            Assert.That(result.Accepted, Is.True);
            var screen = ws.Screens[0];
            Assert.That(screen.Source, Is.EqualTo(ScreenSource.Mirror(1)));
            Assert.That(screen.WithWidth(1.5f).Source, Is.EqualTo(ScreenSource.Mirror(1)));
            Assert.That(screen.WithResolution(Resolution.Create(1920, 1080)).Source, Is.EqualTo(ScreenSource.Mirror(1)));
        }

        [Test]
        public void InitialWidth_Is2560PixelsPerMeter_Clamped()
        {
            Assert.That(ScreenSizing.InitialWidthMeters(Resolution.Create(2560, 1440)), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(ScreenSizing.InitialWidthMeters(Resolution.Create(3840, 2160)), Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(ScreenSizing.InitialWidthMeters(Resolution.Create(1440, 2560)), Is.EqualTo(0.5625f).Within(1e-4f));
        }

        [Test]
        public void TestScreens_AreTestSource()
        {
            TestScreens.Next(0).Apply(Workspace.Empty, out var ws);
            Assert.That(ws.Screens[0].Source, Is.EqualTo(ScreenSource.Test));
            Assert.That(TestScreens.IsTestScreen(ws.Screens[0]), Is.True);
        }
    }
}
