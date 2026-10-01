using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Layout;

namespace ScreenPortal.App.Menu
{
    /// <summary>
    /// Na abertura, centraliza a superfície na cabeça assim que o tracking vale. Sem isso a superfície nasce no centro
    /// padrão do tracking space (1,2 m do chão, frente do Guardian), longe de onde o usuário está e olha. Até os
    /// perfis (Fase 3b) não há âncora no mundo: mesmo com a última sessão restaurada (ADR 0014), o tracking space é novo a
    /// cada abertura, então a superfície recentra e as telas voltam na mesma disposição em volta do usuário. Não entra no
    /// undo.
    /// </summary>
    public sealed class StartupRecenter
    {
        /// <summary>Antes do tracking a cabeça fica na origem (no chão): abaixo disso a pose ainda não vale.</summary>
        public const float MinHeadHeightM = 0.3f;

        private readonly ICommandBus _bus;
        private readonly IHeadPoseSource _head;

        public StartupRecenter(ICommandBus bus, IHeadPoseSource head)
        {
            _bus = bus;
            _head = head;
        }

        public bool Done { get; private set; }

        public void Tick()
        {
            if (Done) return;
            var head = _head.HeadPose;
            if (head.Position.Y < MinHeadHeightM) return;
            _bus.Send(new RecenterLayout(head.Position, LayoutSurface.HeadingOf(head.Rotation), undoable: false));
            Done = true;
        }
    }
}
