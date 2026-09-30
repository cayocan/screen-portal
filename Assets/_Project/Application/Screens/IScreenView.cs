using System;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Screens
{
    /// <summary>View passiva de uma tela (brief §8.3): só reflete o estado; nunca o altera.</summary>
    public interface IScreenView : IDisposable
    {
        /// <summary>Estado confirmado. <paramref name="pose"/> = superfície.ToPose(screen.Placement).</summary>
        void Apply(VirtualScreen screen, Pose pose);

        /// <summary>Só posição/tamanho, para a pré-visualização do arraste (não é estado).</summary>
        void ApplyTransform(Pose pose, float widthMeters, float heightMeters);
    }
}
