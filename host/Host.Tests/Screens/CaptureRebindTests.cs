using ScreenPortal.Host.Domain.Display;

namespace ScreenPortal.Host.Tests.Screens;

public class CaptureRebindTests
{
    [Fact]
    public void MonitorMovedAboveThePrimary_NeedsANewCapture()
    {
        // Bug 2 da 2C: 5120,0 → 0,-1440 deixava a captura antiga preta.
        Assert.True(CaptureRebind.Needed(new RectI(5120, 0, 2560, 1440), new RectI(0, -1440, 2560, 1440)));
    }

    [Fact]
    public void MonitorShiftedByAnotherMonitorRemoval_NeedsANewCapture()
    {
        Assert.True(CaptureRebind.Needed(new RectI(3200, 0, 2560, 1440), new RectI(2560, 0, 2560, 1440)));
    }

    [Fact]
    public void SameBounds_KeepsTheCapture()
    {
        // Só o HMONITOR muda quando outro monitor é criado ou removido: a captura segue válida.
        Assert.False(CaptureRebind.Needed(new RectI(0, 0, 2560, 1080), new RectI(0, 0, 2560, 1080)));
    }

    [Fact]
    public void SizeChange_IsNotHandledByARebind()
    {
        // Resolução nova muda o stream (RecreateScreen); recriar a captura aqui mandaria um tamanho que o headset não espera.
        Assert.False(CaptureRebind.Needed(new RectI(0, 0, 2560, 1080), new RectI(100, 0, 1920, 1080)));
    }
}
