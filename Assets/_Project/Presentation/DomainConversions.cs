using ScreenPortal.Domain.Spatial;
using UnityEngine;

namespace ScreenPortal.Presentation
{
    /// <summary>Única fronteira entre os tipos espaciais do domínio e os da Unity (mesma convenção de eixos).</summary>
    public static class DomainConversions
    {
        public static Vector3 ToUnity(this Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        public static Quaternion ToUnity(this Quat q) => new Quaternion(q.X, q.Y, q.Z, q.W);
        public static Vec3 ToDomain(this Vector3 v) => new Vec3(v.x, v.y, v.z);
        public static Quat ToDomain(this Quaternion q) => new Quat(q.x, q.y, q.z, q.w);
    }
}
