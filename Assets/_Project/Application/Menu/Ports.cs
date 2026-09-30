using System;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Menu
{
    /// <summary>Pose da cabeça no mundo (recentrar, alinhar à postura).</summary>
    public interface IHeadPoseSource
    {
        Pose HeadPose { get; }
    }

    public sealed class FixedHeadPose : IHeadPoseSource
    {
        public FixedHeadPose(Pose pose) => HeadPose = pose;
        public Pose HeadPose { get; }
    }

    /// <summary>Recentrar do sistema (Quest): o app emite RecenterLayout.</summary>
    public interface IRecenterEvents
    {
        event Action Recentered;
    }

    /// <summary>Painel do menu de pulso (Presentation). Pose = centro do painel; face visível para −Z local.</summary>
    public interface IWristMenuPanel
    {
        bool Visible { get; }
        Pose Pose { get; }
        void Render(WristMenu menu, int hoverIndex);
    }
}
