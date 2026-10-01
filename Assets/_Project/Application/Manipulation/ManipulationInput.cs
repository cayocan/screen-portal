using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Manipulation
{
    /// <summary>
    /// Um ponteiro num quadro (controle, mão ou, na Fase 3, o mouse). Adapters traduzem o hardware para isto
    /// (brief §8.3: mouse e mãos geram os mesmos Commands). Struct mutável: preenchida sem alocação.
    /// </summary>
    public struct ManipulationInput
    {
        public bool HasRay;
        public Vec3 Origin;
        public Vec3 Direction;
        /// <summary>Gatilho (controle) ou pinça do indicador (mão).</summary>
        public bool SelectHeld;
        /// <summary>Grip (controle) ou mão fechada: arrastar solta a tela da superfície.</summary>
        public bool GripHeld;
        /// <summary>Quanto afastar (+) ou aproximar (−) neste quadro: analógico ou puxar/empurrar a mão.</summary>
        public float DistanceDeltaM;
        public bool CancelPressed;
        /// <summary>Desliga ímãs neste quadro (Shift no mouse): princípio Liberdade.</summary>
        public bool NoSnap;
        public bool UndoPressed;
        public bool RedoPressed;

        /// <summary>Mão rastreada (não controle): entra no modo das mãos e no detector de tchau.</summary>
        public bool IsHand;
        public Vec3 PalmPosition;
        /// <summary>Normal da palma (para onde a palma aponta).</summary>
        public Vec3 PalmNormal;
        /// <summary>Mão aberta: sem pinça e sem punho.</summary>
        public bool HandOpen;

        public bool AnyButton => SelectHeld || GripHeld || CancelPressed || UndoPressed || RedoPressed || DistanceDeltaM != 0f;
    }

    public interface IManipulationInput
    {
        /// <summary>false = dispositivo ausente neste quadro.</summary>
        bool Read(out ManipulationInput input);
    }

    public sealed class NullManipulationInput : IManipulationInput
    {
        public static readonly NullManipulationInput Instance = new NullManipulationInput();

        public bool Read(out ManipulationInput input)
        {
            input = default;
            return false;
        }
    }
}
