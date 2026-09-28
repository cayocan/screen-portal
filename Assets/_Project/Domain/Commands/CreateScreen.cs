using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>Cria um monitor com resolução fixa (brief §10.3). A nova tela recebe o foco.</summary>
    public sealed class CreateScreen : ICommand
    {
        public Resolution Resolution { get; }
        public Pose Pose { get; }
        public float WidthMeters { get; }
        public float InitialCurvature { get; }
        public string Label { get; }
        public bool IsUndoable => true;

        public CreateScreen(Resolution resolution, Pose pose, float widthMeters, float initialCurvature = 0f, string label = "")
        {
            Resolution = resolution;
            Pose = pose;
            WidthMeters = widthMeters;
            InitialCurvature = initialCurvature;
            Label = label;
        }

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            if (!Resolution.IsValid)
            {
                next = state;
                return CommandResult.Rejected("Resolução inválida.");
            }
            var id = state.AllocateId();
            var screen = new VirtualScreen(id, Resolution, Pose, WidthMeters, InitialCurvature, label: Label);
            next = state.WithScreenAdded(screen).WithFocus(id);
            return CommandResult.Ok;
        }

        public override string ToString() => $"CreateScreen({Resolution}, {WidthMeters:0.00} m)";
    }
}
