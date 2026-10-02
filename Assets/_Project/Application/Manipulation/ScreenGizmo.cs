using System;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Manipulation
{
    public enum GizmoPart
    {
        None,
        MoveX,
        MoveY,
        RotateX,
        RotateY,
        RotateZ,
        Scale,
    }

    /// <summary>Onde o gizmo está: centro da tela, eixos da tela (X direita, Y cima, Z para quem olha) e o tamanho.</summary>
    public readonly struct GizmoFrame
    {
        public readonly Vec3 Origin;
        public readonly Vec3 Right;
        public readonly Vec3 Up;
        public readonly Vec3 Back;
        /// <summary>Comprimento das setas (m); anéis com raio <see cref="ScreenGizmo.RingRadius"/> × isto.</summary>
        public readonly float Size;

        public GizmoFrame(Vec3 origin, Vec3 right, Vec3 up, Vec3 back, float size)
        {
            Origin = origin;
            Right = right;
            Up = up;
            Back = back;
            Size = size;
        }

        public Vec3 Axis(GizmoPart part)
        {
            switch (part)
            {
                case GizmoPart.MoveX:
                case GizmoPart.RotateX: return Right;
                case GizmoPart.MoveY:
                case GizmoPart.RotateY: return Up;
                default: return Back;
            }
        }
    }

    /// <summary>
    /// Gizmo universal do modo edição (como o da Unity): setas X/Y movem, anéis X/Y/Z giram, o cubo do centro escala.
    /// Geometria pura (pick e matemática do arraste), sem alocação. A profundidade fica na roda/analógico.
    /// </summary>
    public static class ScreenGizmo
    {
        /// <summary>Tamanho angular das setas (o gizmo tem o mesmo tamanho na visão a qualquer distância).</summary>
        public const float SizePerMeter = 0.12f;
        public const float RingRadius = 1.3f; // maior que as setas: anel e seta nunca se cruzam
        public const float PickTolerance = 0.07f; // × Size
        public const float ScaleCube = 0.12f;      // × Size (raio de pick)
        public const float RotationStepDeg = 5f;
        // Roda segurando uma parte: ajuste fino (passo curto) e, com Shift, passo longo.
        public const float WheelMoveFineM = 0.005f, WheelMoveCoarseM = 0.05f;
        public const float WheelRotateFineDeg = 1f, WheelRotateCoarseDeg = 15f;
        public const float WheelScaleFine = 0.01f, WheelScaleCoarse = 0.10f;

        /// <summary>Quanto um clique da roda muda a parte: metros (setas), graus (anéis) ou fração do tamanho (cubo).</summary>
        public static float WheelStep(GizmoPart part, bool coarse)
        {
            switch (part)
            {
                case GizmoPart.MoveX:
                case GizmoPart.MoveY: return coarse ? WheelMoveCoarseM : WheelMoveFineM;
                case GizmoPart.Scale: return coarse ? WheelScaleCoarse : WheelScaleFine;
                case GizmoPart.None: return 0f;
                default: return coarse ? WheelRotateCoarseDeg : WheelRotateFineDeg;
            }
        }
        private const int RingSamples = 48;

        public static GizmoFrame Frame(VirtualScreen screen, LayoutSurface surface) => Frame(surface.ToPose(screen.Placement), surface);

        /// <summary>Gizmo de uma pose de tela (também a da prévia, durante o arraste).</summary>
        public static GizmoFrame Frame(Pose pose, LayoutSurface surface)
        {
            var back = -(pose.Rotation * Vec3.Forward);
            float distance = (pose.Position - surface.Center).Length;
            float size = Math.Max(0.05f, distance * SizePerMeter);
            // um pouco à frente da tela (do lado de quem olha) para não sumir dentro dela
            return new GizmoFrame(pose.Position + back * (size * 0.1f), pose.Rotation * Vec3.Right, pose.Rotation * Vec3.Up, back, size);
        }

        public static GizmoPart Pick(in GizmoFrame f, Vec3 origin, Vec3 direction)
        {
            var dir = direction.Normalized;
            float tol = PickTolerance * f.Size;
            if (DistanceToPoint(origin, dir, f.Origin) <= ScaleCube * f.Size) return GizmoPart.Scale;
            if (DistanceToSegment(origin, dir, f.Origin, f.Origin + f.Right * f.Size) <= tol) return GizmoPart.MoveX;
            if (DistanceToSegment(origin, dir, f.Origin, f.Origin + f.Up * f.Size) <= tol) return GizmoPart.MoveY;
            var best = GizmoPart.None;
            float bestD = tol;
            for (int k = 0; k < 3; k++)
            {
                var part = k == 0 ? GizmoPart.RotateZ : k == 1 ? GizmoPart.RotateX : GizmoPart.RotateY;
                float d = DistanceToRing(origin, dir, f.Origin, f.Axis(part), f.Size * RingRadius);
                if (d > bestD) continue;
                bestD = d;
                best = part;
            }
            return best;
        }

        /// <summary>Parâmetro (m) do ponto da reta do eixo mais perto do raio: arrastar uma seta = a diferença disso.</summary>
        public static bool AxisParameter(Vec3 axisOrigin, Vec3 axis, Vec3 origin, Vec3 direction, out float s)
        {
            // pontos mais próximos entre duas retas: eixo (P + s·a) e raio (O + t·d)
            var d = direction.Normalized;
            var w = axisOrigin - origin;
            float b = Vec3.Dot(axis, d);
            float denom = 1f - b * b;
            s = 0f;
            if (denom < 1e-4f) return false; // raio paralelo ao eixo
            s = (b * Vec3.Dot(d, w) - Vec3.Dot(axis, w)) / denom;
            return true;
        }

        /// <summary>Ângulo (graus, em volta de <paramref name="axis"/>) do ponto onde o raio cruza o plano do anel.</summary>
        public static bool RingAngle(Vec3 center, Vec3 axis, Vec3 reference, Vec3 origin, Vec3 direction, out float degrees)
        {
            degrees = 0f;
            var d = direction.Normalized;
            float denom = Vec3.Dot(d, axis);
            if (Math.Abs(denom) < 0.05f) return false; // anel de perfil: sem ângulo confiável
            float t = Vec3.Dot(center - origin, axis) / denom;
            if (t <= 0f) return false;
            var v = origin + d * t - center;
            var x = reference;
            var y = Vec3.Cross(axis, x);
            degrees = (float)(Math.Atan2(Vec3.Dot(v, y), Vec3.Dot(v, x)) * Angles.RadToDeg);
            return true;
        }

        public static float SnapAngle(float degrees) => (float)Math.Round(degrees / RotationStepDeg) * RotationStepDeg;

        /// <summary>Um vetor qualquer perpendicular ao eixo (referência do ângulo dos anéis).</summary>
        public static Vec3 Perpendicular(Vec3 axis)
        {
            var p = Vec3.Cross(axis, Math.Abs(axis.Y) < 0.9f ? Vec3.Up : Vec3.Right);
            return p.Normalized;
        }

        private static float DistanceToPoint(Vec3 origin, Vec3 dir, Vec3 point)
        {
            float t = Vec3.Dot(point - origin, dir);
            if (t < 0f) return float.MaxValue;
            return (origin + dir * t - point).Length;
        }

        private static float DistanceToSegment(Vec3 origin, Vec3 dir, Vec3 a, Vec3 b)
        {
            float best = float.MaxValue;
            const int steps = 16;
            for (int i = 0; i <= steps; i++)
            {
                var p = a + (b - a) * (i / (float)steps);
                float d = DistanceToPoint(origin, dir, p);
                if (d < best) best = d;
            }
            return best;
        }

        private static float DistanceToRing(Vec3 origin, Vec3 dir, Vec3 center, Vec3 axis, float radius)
        {
            var x = Perpendicular(axis);
            var y = Vec3.Cross(axis, x);
            float best = float.MaxValue;
            for (int i = 0; i < RingSamples; i++)
            {
                double a = i * (2.0 * Math.PI / RingSamples);
                var p = center + x * (float)(Math.Cos(a) * radius) + y * (float)(Math.Sin(a) * radius);
                float d = DistanceToPoint(origin, dir, p);
                if (d < best) best = d;
            }
            return best;
        }
    }
}
