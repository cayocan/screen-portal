using NUnit.Framework;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Tests
{
    public class SpatialAndIdTests
    {
        [Test]
        public void Vec3_EqualityIsByValue()
        {
            Assert.That(new Vec3(1f, 2f, 3f), Is.EqualTo(new Vec3(1f, 2f, 3f)));
            Assert.That(new Vec3(1f, 2f, 3f), Is.Not.EqualTo(Vec3.Zero));
        }

        [Test]
        public void Pose_At_UsesIdentityRotation()
        {
            var pose = Pose.At(new Vec3(0f, 1.2f, 1.3f));
            Assert.That(pose.Position, Is.EqualTo(new Vec3(0f, 1.2f, 1.3f)));
            Assert.That(pose.Rotation, Is.EqualTo(Quat.Identity));
        }

        [Test]
        public void ScreenId_DefaultIsNone()
        {
            Assert.That(default(ScreenId).IsNone, Is.True);
            Assert.That(ScreenId.None.IsNone, Is.True);
            Assert.That(new ScreenId(3).IsNone, Is.False);
            Assert.That(new ScreenId(3) == new ScreenId(3), Is.True);
            Assert.That(new ScreenId(3) != new ScreenId(4), Is.True);
        }
    }
}
