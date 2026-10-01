using System;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;

namespace ScreenPortal.App.Screens
{
    /// <summary>View passiva de uma tela (brief §8.3): só reflete o estado; nunca o altera.</summary>
    public interface IScreenView : IDisposable
    {
        /// <summary>Estado confirmado. <paramref name="pose"/> = superfície.ToPose(screen.Placement);
        /// <paramref name="radiusM"/> = <c>CurvedGeometry.Of(screen, surface).RadiusM</c> (0 = plana).</summary>
        void Apply(VirtualScreen screen, Pose pose, float radiusM);

        /// <summary>Só posição/tamanho/raio, para a pré-visualização do arraste (não é estado; nunca troca a forma da layer).</summary>
        void ApplyTransform(Pose pose, float widthMeters, float heightMeters, float radiusM);
    }
}
