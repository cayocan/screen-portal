using System.Runtime.InteropServices;
using ScreenPortal.Host.Transport.Control;

namespace ScreenPortal.Host.App.Control;

/// <summary>Diálogo "Permitir / Negar" no topo da tela (o ícone na bandeja entra na Fase 5).</summary>
public sealed class MessageBoxPairingApprover : IPairingApprover
{
    public Task<bool> ApproveAsync(string deviceName, string code) => Task.Run(() =>
    {
        string grouped = code.Length == 6 ? code[..3] + " " + code[3..] : code;
        string text = $"'{deviceName}' quer ver sua tela.\n\nConfira se o headset mostra o código:\n\n        {grouped}\n\nPermitir?";
        return MessageBoxW(IntPtr.Zero, text, "Screen Portal — novo headset", MbYesNo | MbIconQuestion | MbTopMost | MbSetForeground) == IdYes;
    });

    private const uint MbYesNo = 0x4, MbIconQuestion = 0x20, MbTopMost = 0x40000, MbSetForeground = 0x10000;
    private const int IdYes = 6;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);
}
