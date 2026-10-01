using NUnit.Framework;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class CurvedGeometryTests
    {
        [Test]
        public void Radius_IsDistanceOverCurvature_ArcIsWidthOverRadius()
        {
            var g = CurvedGeometry.Of(0.6f, 1.3f, 1f);
            Assert.That(g.IsCurved, Is.True);
            Assert.That(g.EffectiveCurvature, Is.EqualTo(0.6f));
            Assert.That(g.ArcLimited, Is.False);
            Assert.That(g.RadiusM, Is.EqualTo(1.3f / 0.6f).Within(1e-5f));
            Assert.That(g.ArcRad, Is.EqualTo(1f / (1.3f / 0.6f)).Within(1e-5f));
        }

        [Test]
        public void Envolvente_PutsTheCylinderAxisAtTheDistance()
        {
            var g = CurvedGeometry.Of(1f, 1.3f, 1f);
            Assert.That(g.RadiusM, Is.EqualTo(1.3f).Within(1e-6f));
        }

        [Test]
        public void Flat_HasNoRadiusNorArc()
        {
            var g = CurvedGeometry.Of(0f, 1.3f, 1f);
            Assert.That(g.IsCurved, Is.False);
            Assert.That(g.RadiusM, Is.EqualTo(0f));
            Assert.That(g.ArcRad, Is.EqualTo(0f));
            Assert.That(g.Label(), Is.EqualTo("Plana"));
        }

        [Test]
        public void ArcAbove120Degrees_IsLimited()
        {
            var g = CurvedGeometry.Of(1f, 1f, 3f); // 3 m a 1 m: arco pedido 172°
            Assert.That(g.ArcLimited, Is.True);
            Assert.That(g.ArcDeg, Is.EqualTo(120f).Within(0.01f));
            Assert.That(g.EffectiveCurvature, Is.EqualTo(2.0 * System.Math.PI / 3.0 / 3.0).Within(1e-5));
            Assert.That(g.Curvature, Is.EqualTo(1f), "o valor pedido fica guardado");
        }

        [Test]
        public void CurvatureIsClampedTo01()
        {
            Assert.That(CurvedGeometry.Of(5f, 1.3f, 1f).EffectiveCurvature, Is.EqualTo(1f));
            Assert.That(CurvedGeometry.Of(-1f, 1.3f, 1f).IsCurved, Is.False);
        }

        [Test]
        public void Distance_ComesFromTheSurface()
        {
            var placement = Placement.OnSurface(0f, 30f, 1.3f);
            var cylinder = LayoutSurface.Default;
            Assert.That(CurvedGeometry.Of(0.5f, placement, 1f, cylinder).DistanceM,
                Is.EqualTo(1.3f * System.Math.Cos(30.0 * System.Math.PI / 180.0)).Within(1e-5));
            Assert.That(CurvedGeometry.Of(0.5f, placement, 1f, cylinder.WithMode(LayoutMode.Sphere)).DistanceM, Is.EqualTo(1.3f).Within(1e-6f));

            var loose = Placement.Loose(Pose.At(LayoutSurface.DefaultCenter + new Vec3(0f, 0f, 0.9f)));
            Assert.That(CurvedGeometry.Of(0.5f, loose, 1f, cylinder).DistanceM, Is.EqualTo(0.9f).Within(1e-5f), "solta: do centro da superfície");
        }

        [Test]
        public void CylinderCenter_IsTheSurfaceCenterForEnvolvente()
        {
            var surface = LayoutSurface.Default;
            var placement = Placement.OnSurface(40f, 0f, 1.3f);
            var pose = surface.ToPose(placement);
            var g = CurvedGeometry.Of(1f, placement, 1f, surface);
            var center = CurvedGeometry.CylinderCenter(pose, g.RadiusM);
            Assert.That((center.Position - surface.Center).Length, Is.LessThan(1e-4f));
            Assert.That(center.Rotation, Is.EqualTo(pose.Rotation));
        }

        [Test]
        public void SurfacePoint_LiesOnTheArc_AndOnThePlaneWhenFlat()
        {
            var pose = new Pose(new Vec3(1f, 2f, 3f), Quat.AngleAxis(30f, Vec3.Up));
            Assert.That((CurvedGeometry.SurfacePoint(pose, 2f, 0f, 0f) - pose.Position).Length, Is.LessThan(1e-6f));

            var center = CurvedGeometry.CylinderCenter(pose, 2f);
            var p = CurvedGeometry.SurfacePoint(pose, 2f, 0.5f, 0.1f);
            var fromAxis = pose.Rotation.Inverse * (p - center.Position);
            Assert.That(fromAxis.Y, Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(System.Math.Sqrt(fromAxis.X * fromAxis.X + fromAxis.Z * fromAxis.Z), Is.EqualTo(2f).Within(1e-5));
            Assert.That(System.Math.Atan2(fromAxis.X, fromAxis.Z) * 2.0, Is.EqualTo(0.5f).Within(1e-5), "x é o comprimento do arco");

            var flat = CurvedGeometry.SurfacePoint(pose, 0f, 0.5f, 0.1f);
            Assert.That((flat - (pose.Position + pose.Rotation * new Vec3(0.5f, 0.1f, 0f))).Length, Is.LessThan(1e-6f));
        }

        [Test]
        public void Label_NamesThePreset_RoundsTheRadiusTo100mm_AndWarnsWhenLimited()
        {
            Assert.That(CurvedGeometry.Of(0.6f, 0.78f, 1f).Label(), Is.EqualTo("Média · 60 % · ≈ 1300R"));
            Assert.That(CurvedGeometry.Of(0.6f, 1.3f, 1f).Label(), Is.EqualTo("Média · 60 % · ≈ 2200R"));
            Assert.That(CurvedGeometry.Of(0.45f, 1.3f, 1f).Label(), Is.EqualTo("45 % · ≈ 2900R"));
            Assert.That(CurvedGeometry.Of(1f, 1f, 3f).Label(), Is.EqualTo("70 % · ≈ 1400R · arco limitado"));
        }

        [TestCase(0.3f)]
        [TestCase(1f)]
        public void Invariant_CurvingKeepsResolutionAndWidth_ArcTimesRadiusIsTheWidth(float curvature)
        {
            var resolution = Resolution.Create(2560, 1080);
            new CreateScreen(resolution, Placement.OnSurface(0f, 0f, 1.3f), 1.4f).Apply(Workspace.Empty, out var ws);
            var id = ws.Screens[0].Id;
            new SetScreenCurvature(id, curvature).Apply(ws, out ws);
            var screen = ws.Screens[0];
            var g = CurvedGeometry.Of(screen, ws.Surface);
            Assert.That(screen.Resolution, Is.EqualTo(resolution));
            Assert.That(screen.WidthMeters, Is.EqualTo(1.4f));
            Assert.That(g.WidthM, Is.EqualTo(1.4f));
            Assert.That(g.ArcRad * g.RadiusM, Is.EqualTo(1.4f).Within(1e-5f));
        }
    }
}
