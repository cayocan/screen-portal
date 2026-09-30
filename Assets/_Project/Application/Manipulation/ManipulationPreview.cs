using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Manipulation
{
    /// <summary>
    /// Estado transitório do arraste (não é estado da workspace): a view desenha isto até o Commit.
    /// Uma instância reutilizada — sem alocação por quadro.
    /// </summary>
    public sealed class ManipulationPreview
    {
        public bool Active { get; private set; }
        public ScreenId Screen { get; private set; }
        public Pose Pose { get; private set; }
        public Placement Placement { get; private set; }
        public bool IsLoose { get; private set; }
        public float WidthM { get; private set; }
        public float HeightM { get; private set; }
        public bool HasGuideAzimuth { get; private set; }
        public float GuideAzimuthDeg { get; private set; }
        public bool HasGuideElevation { get; private set; }
        public float GuideElevationDeg { get; private set; }

        internal void Begin(ScreenId screen, Pose pose, Placement placement, float widthM, float heightM)
        {
            Active = true;
            Screen = screen;
            Pose = pose;
            Placement = placement;
            IsLoose = placement.IsLoose;
            WidthM = widthM;
            HeightM = heightM;
            ClearGuides();
        }

        internal void SetPlacement(Placement placement, Pose pose)
        {
            Placement = placement;
            Pose = pose;
            IsLoose = false;
        }

        internal void SetLoose(Pose pose)
        {
            Pose = pose;
            Placement = Placement.Loose(pose);
            IsLoose = true;
        }

        internal void SetSize(float widthM, float heightM)
        {
            WidthM = widthM;
            HeightM = heightM;
        }

        internal void SetGuides(bool azimuth, float azimuthDeg, bool elevation, float elevationDeg)
        {
            HasGuideAzimuth = azimuth;
            GuideAzimuthDeg = azimuthDeg;
            HasGuideElevation = elevation;
            GuideElevationDeg = elevationDeg;
        }

        internal void ClearGuides() => SetGuides(false, 0f, false, 0f);

        internal void End()
        {
            Active = false;
            Screen = ScreenId.None;
            ClearGuides();
        }
    }
}
