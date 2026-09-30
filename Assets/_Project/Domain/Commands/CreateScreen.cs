using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain.Commands
{
    /// <summary>
    /// Cria um monitor com resolução fixa (brief §10.3). A nova tela recebe o foco.
    /// Sem placement: nasce no espaço livre mais perto de <see cref="Near"/> (onde o usuário olha, spec 2C §3) ou, sem
    /// ele, no primeiro espaço livre da superfície (spec superfície §6).
    /// </summary>
    public sealed class CreateScreen : ICommand
    {
        public Resolution Resolution { get; }
        public Placement? Placement { get; }
        public float WidthMeters { get; }
        public float InitialCurvature { get; }
        public string Label { get; }
        public ScreenSource Source { get; }
        public Placement? Near { get; }
        public bool IsUndoable => true;

        public CreateScreen(Resolution resolution, Placement? placement, float widthMeters, float initialCurvature = 0f, string label = "", ScreenSource source = default,
            Placement? near = null)
        {
            Resolution = resolution;
            Placement = placement;
            WidthMeters = widthMeters;
            InitialCurvature = initialCurvature;
            Label = label;
            Source = source;
            Near = near;
        }

        public CommandResult Apply(Workspace state, out Workspace next)
        {
            if (!Resolution.IsValid)
            {
                next = state;
                return CommandResult.Rejected("Resolução inválida.");
            }
            var id = state.AllocateId();
            float width = ScreenLimits.ClampWidth(WidthMeters);
            float height = width / Resolution.AspectRatio;
            var placement = Placement
                ?? (Near is Placement near ? SurfaceSlots.NearestFree(state, width, height, near) : SurfaceSlots.FirstFree(state, width, height));
            var screen = new VirtualScreen(id, Resolution, placement, width, InitialCurvature, label: Label, source: Source);
            next = state.WithScreenAdded(screen).WithFocus(id);
            return CommandResult.Ok;
        }

        public override string ToString() => $"CreateScreen({Source}, {Resolution}, {WidthMeters:0.00} m)";
    }
}
