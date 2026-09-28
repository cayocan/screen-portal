using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Screens
{
    /// <summary>Porta implementada pela Presentation (ex.: OVROverlay). A view recebe Apply logo após criada.</summary>
    public interface IScreenViewFactory
    {
        IScreenView Create(VirtualScreen screen);
    }
}
