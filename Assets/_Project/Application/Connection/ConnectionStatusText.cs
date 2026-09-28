namespace ScreenPortal.App.Connection
{
    /// <summary>Textos pt-BR do status da conexão (brief §1.1). Localização fica para depois do MVP.</summary>
    public static class ConnectionStatusText
    {
        /// <summary>Instrução até existir instalador + link/QR code (Fase 5, brief §1.1).</summary>
        public const string HostInstallHint = "Abra o ScreenPortalHost.exe no seu PC\n(mesma rede Wi-Fi do Quest).";

        public static string Format(ConnectionStatus status)
        {
            string pc = status.HostName.Length > 0 ? status.HostName : "seu PC";
            switch (status.Kind)
            {
                case ConnectionStatusKind.Idle: return "";
                case ConnectionStatusKind.Searching: return "Procurando seu PC…";
                case ConnectionStatusKind.InstallHost:
                    return "Não encontramos seu PC.\n" + HostInstallHint;
                case ConnectionStatusKind.Connecting: return "Conectando a " + pc + "…";
                case ConnectionStatusKind.ConfirmOnPc: return "Confirme no PC:\ncódigo " + Group(status.PairingCode);
                case ConnectionStatusKind.Connected: return "Conectado a " + pc;
                case ConnectionStatusKind.Unstable: return "Conexão instável…";
                case ConnectionStatusKind.Reconnecting: return "Reconectando a " + pc + "…";
                case ConnectionStatusKind.Rejected: return "Conexão recusada no PC.";
                case ConnectionStatusKind.IncompatibleVersion: return "Versões incompatíveis.\nAtualize o app ou o host.";
                case ConnectionStatusKind.HostShutdown: return pc + " encerrou o Screen Portal.";
                default: return status.Kind.ToString();
            }
        }

        private static string Group(string code) =>
            code != null && code.Length == 6 ? code.Substring(0, 3) + " " + code.Substring(3) : code;
    }
}
