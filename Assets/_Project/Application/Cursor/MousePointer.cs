using ScreenPortal.App.Connection;
using ScreenPortal.App.Manipulation;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Cursor
{
    /// <summary>
    /// O mouse como ponteiro de manipulação no modo edição (spec 3B §4): raio cabeça → cursor 3D; botão esquerdo =
    /// gatilho/pinça (mover, alças), direito = grip (solta da superfície). Roda: arrastando, afasta/aproxima
    /// (<see cref="WheelStepM"/> por clique); parado, afasta/aproxima a tela sob o cursor, e com <c>Shift</c> muda o tamanho
    /// (os mesmos passos dos atalhos). Esc (<see cref="EditMode.RequestCancel"/>) cancela o arraste. Sem alocação.
    /// </summary>
    public sealed class MousePointer : IManipulationInput
    {
        public const float WheelStepM = 0.05f;
        private const int WheelNotch = 120;

        private readonly CursorSync _sync;
        private readonly WorldCursor _world;
        private readonly ScreenManipulationController _manipulation;
        private readonly HotkeyDispatcher _hotkeys;
        private readonly WorkspaceStore _store;
        private readonly EditMode _edit;
        private uint _epoch;
        private int _wheel;

        public MousePointer(CursorSync sync, WorldCursor world, ScreenManipulationController manipulation, HotkeyDispatcher hotkeys,
            WorkspaceStore store, EditMode edit)
        {
            _edit = edit;
            _sync = sync;
            _world = world;
            _manipulation = manipulation;
            _hotkeys = hotkeys;
            _store = store;
        }

        public bool Read(out ManipulationInput input)
        {
            input = default;
            var s = _sync.Current;
            if (s.Mode != MouseMode.Edit || !_world.Active)
            {
                _edit.ConsumeCancel();
                return false;
            }
            if (s.Epoch != _epoch)
            {
                _epoch = s.Epoch;
                _wheel = 0; // o host zera a roda acumulada a cada epoch
            }
            int notches = (s.Wheel - _wheel) / WheelNotch;
            _wheel += notches * WheelNotch;
            input.HasRay = true;
            input.Origin = _world.Origin;
            input.Direction = _world.Direction;
            input.SelectHeld = s.Left;
            input.GripHeld = s.Right;
            input.NoSnap = s.Shift;
            input.CancelPressed = _edit.ConsumeCancel();
            if (notches == 0) return true;
            if (_manipulation.IsDragging)
            {
                input.DistanceDeltaM = -notches * WheelStepM; // roda para cima aproxima
                return true;
            }
            var hit = ScreenHitTest.Pick(_store.State, _world.Origin, _world.Direction);
            if (hit.Region == HitRegion.None) return true;
            var action = s.Shift ? (notches > 0 ? HotkeyAction.Bigger : HotkeyAction.Smaller)
                                 : (notches > 0 ? HotkeyAction.Closer : HotkeyAction.Farther);
            int count = notches > 0 ? notches : -notches;
            for (int i = 0; i < count; i++) _hotkeys.Handle(new HotkeyMessage(action, (uint)hit.Screen.Value));
            return true;
        }
    }
}
