using System;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Screens
{
    /// <summary>View passiva de uma tela (brief §8.3): só reflete o estado; nunca o altera.</summary>
    public interface IScreenView : IDisposable
    {
        void Apply(VirtualScreen screen);
    }
}
