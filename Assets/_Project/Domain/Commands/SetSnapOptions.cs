using ScreenPortal.Domain.Layout;

namespace ScreenPortal.Domain.Commands
{
    public sealed class SetSnapOptions : ICommand
    {
        public SnapOptions Options { get; }
        public bool IsUndoable => true;

        public SetSnapOptions(SnapOptions options) => Options = options;

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            next = state.WithSurface(state.Surface.WithSnap(Options));
            return CommandResult.Ok;
        }
    }
}
