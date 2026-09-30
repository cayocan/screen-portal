using System;
using ScreenPortal.Domain.Layout;

namespace ScreenPortal.Domain.Screens
{
    /// <summary>
    /// Uma tela no espaço 3D. Imutável: toda alteração devolve uma nova instância (Commands, brief §8.2).
    /// Resolução é fixa desde a criação; redimensionar é só <see cref="WidthMeters"/> (zoom, brief §10.1).
    /// </summary>
    public sealed class VirtualScreen
    {
        public ScreenId Id { get; }
        public Resolution Resolution { get; }
        /// <summary>Sobre a superfície ou solta; a pose 3D é <c>surface.ToPose(Placement)</c> (ADR 0005).</summary>
        public Placement Placement { get; }
        public float WidthMeters { get; }
        /// <summary>0 = plana, 1 = envolvente (brief §10.2).</summary>
        public float Curvature { get; }
        /// <summary>Última curvatura &gt; 0, usada pelo toggle plana ↔ curva.</summary>
        public float LastNonZeroCurvature { get; }
        public FrameRateMode FrameRateMode { get; }
        public string Label { get; }
        /// <summary>De onde vem a imagem (spec 2C). Nunca muda depois de criada.</summary>
        public ScreenSource Source { get; }

        public Orientation Orientation => Resolution.Orientation;
        public float HeightMeters => WidthMeters / Resolution.AspectRatio;

        public VirtualScreen(
            ScreenId id,
            Resolution resolution,
            Placement placement,
            float widthMeters,
            float curvature = 0f,
            float lastNonZeroCurvature = ScreenLimits.DefaultCurvature,
            FrameRateMode frameRateMode = default,
            string label = "",
            ScreenSource source = default)
        {
            if (id.IsNone) throw new ArgumentException("Tela precisa de um id.", nameof(id));
            if (!resolution.IsValid) throw new ArgumentException("Resolução inválida.", nameof(resolution));

            Id = id;
            Resolution = resolution;
            Placement = placement;
            WidthMeters = ScreenLimits.ClampWidth(widthMeters);
            Curvature = ScreenLimits.Clamp01(curvature);
            float last = ScreenLimits.Clamp01(lastNonZeroCurvature);
            LastNonZeroCurvature = Curvature > 0f ? Curvature : (last > 0f ? last : ScreenLimits.DefaultCurvature);
            FrameRateMode = frameRateMode;
            Label = label ?? string.Empty;
            Source = source;
        }

        public VirtualScreen WithPlacement(Placement placement) =>
            new VirtualScreen(Id, Resolution, placement, WidthMeters, Curvature, LastNonZeroCurvature, FrameRateMode, Label, Source);

        public VirtualScreen WithWidth(float widthMeters) =>
            new VirtualScreen(Id, Resolution, Placement, widthMeters, Curvature, LastNonZeroCurvature, FrameRateMode, Label, Source);

        public VirtualScreen WithCurvature(float curvature) =>
            new VirtualScreen(Id, Resolution, Placement, WidthMeters, curvature, LastNonZeroCurvature, FrameRateMode, Label, Source);

        public VirtualScreen WithFrameRateMode(FrameRateMode mode) =>
            new VirtualScreen(Id, Resolution, Placement, WidthMeters, Curvature, LastNonZeroCurvature, mode, Label, Source);

        /// <summary>Só para <c>RecreateScreen</c> (ação explícita, brief §10.4). Mantém placement e largura física.</summary>
        public VirtualScreen WithResolution(Resolution resolution) =>
            new VirtualScreen(Id, resolution, Placement, WidthMeters, Curvature, LastNonZeroCurvature, FrameRateMode, Label, Source);

        public override string ToString() => $"{Id} {Source} {Resolution} {WidthMeters:0.00} m curv {Curvature:0.00} {FrameRateMode}";
    }
}
