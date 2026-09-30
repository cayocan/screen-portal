using ScreenPortal.App.Menu;
using ScreenPortal.Domain.Spatial;
using UnityEngine;
using Pose = ScreenPortal.Domain.Spatial.Pose;

namespace ScreenPortal.Infrastructure.Unity
{
    public sealed class OvrHeadPose : IHeadPoseSource
    {
        private readonly Transform _head;

        public OvrHeadPose(Transform head) => _head = head;

        public Pose HeadPose
        {
            get
            {
                var p = _head.position;
                var r = _head.rotation;
                return new Pose(new Vec3(p.x, p.y, p.z), new Quat(r.x, r.y, r.z, r.w));
            }
        }
    }
}
