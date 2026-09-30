using System;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Domain.Layout
{
    /// <summary>
    /// "Grade invisível" em volta do usuário (brief §10.12, spec superfície §3). Imutável; faz parte da Workspace
    /// (snapshot do undo e do perfil). A pose de uma tela é sempre derivada: <see cref="ToPose"/>.
    /// </summary>
    public sealed class LayoutSurface
    {
        public const float MinTiltDeg = 0f;
        public const float MaxTiltDeg = 60f;
        public static readonly Vec3 DefaultCenter = new Vec3(0f, 1.2f, 0f);
        public static readonly LayoutSurface Default = new LayoutSurface(LayoutMode.Cylinder, DefaultCenter, 0f, 0f, SnapOptions.Default);

        public LayoutMode Mode { get; }
        public Vec3 Center { get; }
        /// <summary>Yaw da "frente" (azimute 0°).</summary>
        public float HeadingDeg { get; }
        /// <summary>Inclinação para cima do referencial; só a Esfera usa (o valor fica guardado no Cilindro).</summary>
        public float TiltDeg { get; }
        public SnapOptions Snap { get; }

        public LayoutSurface(LayoutMode mode, Vec3 center, float headingDeg, float tiltDeg, SnapOptions snap)
        {
            Mode = mode;
            Center = center;
            HeadingDeg = Placement.NormalizeAzimuth(headingDeg);
            TiltDeg = Placement.Clamp(tiltDeg, MinTiltDeg, MaxTiltDeg);
            Snap = snap;
        }

        /// <summary>Referencial: yaw da frente e, na Esfera, a inclinação (pitch negativo = para cima).</summary>
        public Quat FrameRotation => Mode == LayoutMode.Sphere
            ? Quat.AngleAxis(HeadingDeg, Vec3.Up) * Quat.AngleAxis(-TiltDeg, Vec3.Right)
            : Quat.AngleAxis(HeadingDeg, Vec3.Up);

        /// <summary>dir(az, el) = (cos el · sin az, sin el, cos el · cos az) no referencial da superfície.</summary>
        public static Vec3 Direction(float azimuthDeg, float elevationDeg)
        {
            double az = azimuthDeg * Angles.DegToRad;
            double el = elevationDeg * Angles.DegToRad;
            return new Vec3((float)(Math.Cos(el) * Math.Sin(az)), (float)Math.Sin(el), (float)(Math.Cos(el) * Math.Cos(az)));
        }

        /// <summary>Cilindro e Esfera põem a tela no mesmo ponto; só a orientação muda.</summary>
        public Pose ToPose(Placement placement)
        {
            if (placement.IsLoose) return placement.LoosePose;
            var frame = FrameRotation;
            var dir = frame * Direction(placement.AzimuthDeg, placement.ElevationDeg);
            var position = Center + dir * placement.DistanceM;
            var rotation = Mode == LayoutMode.Sphere
                ? Quat.LookRotation(dir, frame * Vec3.Up)
                : Quat.AngleAxis(HeadingDeg + placement.AzimuthDeg, Vec3.Up);
            return new Pose(position, rotation);
        }

        /// <summary>O ponto da superfície na direção de <paramref name="world"/> (distância = até ele).</summary>
        public Placement FromWorldPosition(Vec3 world)
        {
            var local = FrameRotation.Inverse * (world - Center);
            float d = local.Length;
            if (d < 1e-6f) return Placement.OnSurface(0f, 0f, Placement.MinDistanceM);
            double el = Math.Asin(Math.Max(-1.0, Math.Min(1.0, local.Y / d))) * Angles.RadToDeg;
            double az = Math.Atan2(local.X, local.Z) * Angles.RadToDeg;
            return Placement.OnSurface((float)az, (float)el, d);
        }

        /// <summary>Distância usada pela curvatura (§10.2): raio horizontal no Cilindro, distância na Esfera.</summary>
        public float CurvatureDistance(Placement placement)
        {
            if (placement.IsLoose) return (placement.LoosePose.Position - Center).Length;
            return Mode == LayoutMode.Cylinder
                ? placement.DistanceM * (float)Math.Cos(placement.ElevationDeg * Angles.DegToRad)
                : placement.DistanceM;
        }

        public static float HeadingOf(Quat rotation)
        {
            var f = rotation * Vec3.Forward;
            return (float)(Math.Atan2(f.X, f.Z) * Angles.RadToDeg);
        }

        public static float PitchUpOf(Quat rotation)
        {
            var f = (rotation * Vec3.Forward).Normalized;
            return (float)(Math.Asin(Math.Max(-1.0, Math.Min(1.0, f.Y))) * Angles.RadToDeg);
        }

        public LayoutSurface WithMode(LayoutMode mode) => new LayoutSurface(mode, Center, HeadingDeg, TiltDeg, Snap);
        public LayoutSurface WithCenter(Vec3 center, float headingDeg) => new LayoutSurface(Mode, center, headingDeg, TiltDeg, Snap);
        public LayoutSurface WithTilt(float tiltDeg) => new LayoutSurface(Mode, Center, HeadingDeg, tiltDeg, Snap);
        public LayoutSurface WithSnap(SnapOptions snap) => new LayoutSurface(Mode, Center, HeadingDeg, TiltDeg, snap);

        public override string ToString() => $"{Mode} center {Center} heading {HeadingDeg:0.#}° tilt {TiltDeg:0.#}°";
    }
}
