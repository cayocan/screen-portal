using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    public sealed class SetFrameRateMode : ScreenCommand
    {
        public FrameRateMode Mode { get; }

        public SetFrameRateMode(ScreenId screenId, FrameRateMode mode) : base(screenId) => Mode = mode;

        protected override VirtualScreen Transform(VirtualScreen screen) => screen.WithFrameRateMode(Mode);
    }
}
