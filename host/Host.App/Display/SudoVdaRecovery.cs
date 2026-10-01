using System.Diagnostics;

namespace ScreenPortal.Host.App.Display;

/// <summary>
/// O driver do SudoVDA (UMDF) pode cair (Windows: código 43, "CM_PROB_FAILED_POST_START") — visto com a memória do PC
/// esgotada e depois de builds pesados. O host roda como administrador, então reinicia o dispositivo sozinho em vez de
/// pedir ao usuário um <c>pnputil /restart-device</c>.
/// </summary>
public static class SudoVdaRecovery
{
    public const string FriendlyName = "SudoMaker Virtual Display Adapter";
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);

    /// <returns>true se o dispositivo foi reiniciado (o chamador abre o driver de novo).</returns>
    public static bool TryRestartDevice(Action<string> log)
    {
        string script =
            $"$d = Get-PnpDevice -FriendlyName '{FriendlyName}' -ErrorAction SilentlyContinue | Select-Object -First 1; " +
            "if (-not $d) { exit 2 }; " +
            "pnputil /restart-device $d.InstanceId | Out-Null; exit $LASTEXITCODE";
        try
        {
            using var process = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"{script}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            if (!process.WaitForExit(30_000))
            {
                process.Kill();
                log("telas: reinício do SudoVDA não terminou em 30 s");
                return false;
            }
            if (process.ExitCode != 0)
            {
                log(process.ExitCode == 2
                    ? "telas: SudoVDA não está instalado (dispositivo não encontrado)"
                    : $"telas: reinício do SudoVDA falhou (pnputil {process.ExitCode}; o host precisa rodar como administrador)");
                return false;
            }
            log("telas: dispositivo do SudoVDA reiniciado");
            Thread.Sleep(Settle);
            return true;
        }
        catch (Exception e)
        {
            log($"telas: não deu para reiniciar o SudoVDA: {e.Message}");
            return false;
        }
    }
}
