using NUnit.Framework;
using ScreenPortal.Domain.Layout;

namespace ScreenPortal.Tests
{
    public class SurfaceSnapperTests
    {
        private static readonly AngularRect Other = new AngularRect(0f, 0f, 10f, 6f); // bordas em ±10°
        private static readonly AngularRect[] Others = { Other };
        private static readonly SnapOptions NeighborsOnly = new SnapOptions(true, false, 5f);
        private static readonly SnapOptions Off = new SnapOptions(false, false, 5f);

        [Test]
        public void SnapsLeftEdgeToNeighborsRightEdge_WithinThreshold()
        {
            // borda esquerda da tela movida a 11° (1° do encaixe perfeito em 10°)
            var moving = new AngularRect(19f, 3f, 8f, 5f);
            var r = SurfaceSnapper.Snap(moving, Others, NeighborsOnly);
            Assert.That(r.SnappedAzimuth, Is.True);
            Assert.That(r.AzimuthDeg, Is.EqualTo(18f).Within(1e-4f));
            Assert.That(r.GuideAzimuthDeg, Is.EqualTo(10f).Within(1e-4f));
        }

        [Test]
        public void DoesNotSnap_BeyondThreshold()
        {
            var moving = new AngularRect(21f, 17f, 8f, 5f); // borda a 13°, 3° do encaixe; altura longe de qualquer alinhamento
            var r = SurfaceSnapper.Snap(moving, Others, NeighborsOnly);
            Assert.That(r.SnappedAzimuth, Is.False);
            Assert.That(r.AzimuthDeg, Is.EqualTo(21f));
            Assert.That(r.SnappedElevation, Is.False);
        }

        [Test]
        public void AlignsTopsAndCenters()
        {
            var moving = new AngularRect(40f, 0.8f, 8f, 6f); // centro a 0,8° do centro da vizinha
            var r = SurfaceSnapper.Snap(moving, Others, NeighborsOnly);
            Assert.That(r.SnappedElevation, Is.True);
            Assert.That(r.ElevationDeg, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void OptionsOff_NeverSnap()
        {
            var r = SurfaceSnapper.Snap(new AngularRect(19f, 0.5f, 8f, 6f), Others, Off);
            Assert.That(r.SnappedAzimuth || r.SnappedElevation, Is.False);
        }

        [Test]
        public void AngularGrid_SnapsToTheStep()
        {
            var grid = new SnapOptions(false, true, 5f);
            var r = SurfaceSnapper.Snap(new AngularRect(9f, -4.2f, 8f, 5f), new AngularRect[0], grid);
            Assert.That(r.AzimuthDeg, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(r.ElevationDeg, Is.EqualTo(-5f).Within(1e-4f));
            var far = SurfaceSnapper.Snap(new AngularRect(7.5f, 0f, 8f, 5f), new AngularRect[0], grid);
            Assert.That(far.SnappedAzimuth, Is.False, "2,5° do passo: fora do limiar");
        }
    }
}
