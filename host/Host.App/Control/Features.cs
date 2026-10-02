namespace ScreenPortal.Host.App.Control;

/// <summary>Chaves de funcionalidades do host.</summary>
public static class Features
{
    /// <summary>
    /// 3D, janela espelhada (ADR 0018): desligada a pedido do Cayo (02/10/2026). Sem Ctrl+Alt+D/Ctrl+Alt+Shift+D e
    /// sem destacar ao arrastar a janela para o vazio. O código fica, para religar depois.
    /// </summary>
    public const bool WindowMirrors = false;
}
