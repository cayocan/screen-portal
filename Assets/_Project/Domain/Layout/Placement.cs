using System;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Domain.Layout
{
    /// <summary>
    /// Onde a tela está (spec superfície §3): sobre a superfície (azimute, elevação, distância a partir do centro)
    /// ou solta (pose livre). Valores fora dos limites são limitados, como a largura das telas.
    /// </summary>
    public readonly struct Placement : IEquatable<Placement>
    {
        public const float MinDistanceM = 0.4f;
        public const float MaxDistanceM = 5f;
        public const float MinElevationDeg = -60f;
        public const float MaxElevationDeg = 75f;

        /// <summary>Onde uma tela nova nasce (spec superfície §6): à frente, 5° abaixo, a 1,3 m.</summary>
        public static readonly Placement Default = OnSurface(0f, -5f, 1.3f);

        public readonly bool IsLoose;
        public readonly float AzimuthDeg;
        public readonly float ElevationDeg;
        public readonly float DistanceM;
        public readonly Pose LoosePose;

        private Placement(bool isLoose, float azimuthDeg, float elevationDeg, float distanceM, Pose loosePose)
        {
            IsLoose = isLoose;
            AzimuthDeg = azimuthDeg;
            ElevationDeg = elevationDeg;
            DistanceM = distanceM;
            LoosePose = loosePose;
        }

        public static Placement OnSurface(float azimuthDeg, float elevationDeg, float distanceM) =>
            new Placement(false, NormalizeAzimuth(azimuthDeg), ClampElevation(elevationDeg), ClampDistance(distanceM), Pose.Identity);

        public static Placement Loose(Pose pose) => new Placement(true, 0f, 0f, 0f, pose);

        /// <summary>Azimute em (−180°, 180°].</summary>
        public static float NormalizeAzimuth(float degrees)
        {
            if (float.IsNaN(degrees)) return 0f;
            float a = degrees % 360f;
            if (a > 180f) a -= 360f;
            else if (a <= -180f) a += 360f;
            return a;
        }

        public static float ClampDistance(float meters) => Clamp(meters, MinDistanceM, MaxDistanceM);
        public static float ClampElevation(float degrees) => Clamp(degrees, MinElevationDeg, MaxElevationDeg);

        internal static float Clamp(float value, float min, float max)
        {
            if (float.IsNaN(value) || value < min) return min;
            return value > max ? max : value;
        }

        public bool Equals(Placement other) =>
            IsLoose == other.IsLoose && (IsLoose
                ? LoosePose.Equals(other.LoosePose)
                : AzimuthDeg == other.AzimuthDeg && ElevationDeg == other.ElevationDeg && DistanceM == other.DistanceM);
        public override bool Equals(object obj) => obj is Placement other && Equals(other);
        public override int GetHashCode() => IsLoose ? LoosePose.GetHashCode() : (AzimuthDeg, ElevationDeg, DistanceM).GetHashCode();
        public override string ToString() =>
            IsLoose ? "Loose" + LoosePose : $"Surface(az {AzimuthDeg:0.#}°, el {ElevationDeg:0.#}°, {DistanceM:0.00} m)";
    }
}
