using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Domain.Commands
{
    public sealed class MoveScreen : ScreenCommand
    {
        public Pose Pose { get; }

        public MoveScreen(ScreenId screenId, Pose pose) : base(screenId) => Pose = pose;

        protected override VirtualScreen Transform(VirtualScreen screen) => screen.WithPose(Pose);
    }
}
