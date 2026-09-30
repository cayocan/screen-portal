using ScreenPortal.Host.Transport.Control;

namespace ScreenPortal.Host.Tests.Control;

public class VirtualSizeValidationTests
{
    [Theory]
    [InlineData(2560u, 1440u)]
    [InlineData(1080u, 1920u)]
    [InlineData(4096u, 4096u)]
    public void ValidSizes_Pass(uint width, uint height)
    {
        Assert.True(ScreenAcquireResult.TryValidateVirtualSize(width, height, out _));
    }

    [Theory]
    [InlineData(0u, 1440u)]
    [InlineData(2561u, 1440u)]       // não múltiplo de 8
    [InlineData(320u, 240u)]         // abaixo do mínimo
    [InlineData(8192u, 1440u)]       // acima do máximo
    [InlineData(uint.MaxValue, 1440u)] // viraria -1 num cast para int
    [InlineData(2560u, 2147483656u)] // viraria negativo num cast para int
    public void InvalidSizes_FailWithAReason(uint width, uint height)
    {
        Assert.False(ScreenAcquireResult.TryValidateVirtualSize(width, height, out var failure));
        Assert.False(failure.Ok);
        Assert.StartsWith("Resolução inválida: ", failure.Error);
    }
}
