
namespace ScreenPortal.App.Connection
{
    /// <summary>Tempos e padrões da conexão (spec 1B §3). Instância registrada no container.</summary>
    public sealed class ConnectionOptions
    {
        public string DeviceName { get; set; } = "Quest";
        public double ConnectTimeoutSeconds { get; set; } = 2.0;
        public double NegotiateTimeoutSeconds { get; set; } = 5.0;
        public double PingIntervalSeconds { get; set; } = 0.5;
        public double DegradedAfterSeconds { get; set; } = 1.5;
        public double ReconnectAfterDegradedSeconds { get; set; } = 3.0;
        public double RetryIntervalSeconds { get; set; } = 0.25;
        public double ReconnectGiveUpSeconds { get; set; } = 10.0;
        public double InstallHintAfterSeconds { get; set; } = 10.0;
        public double RejectCooldownSeconds { get; set; } = 10.0;
        public double IncompatibleCooldownSeconds { get; set; } = 60.0;
        public double ShutdownCooldownSeconds { get; set; } = 3.0;
        /// <summary>Porta base do vídeo no headset: a tela com stream_id n escuta em VideoPort + n (spec 2C §4).</summary>
        public int VideoPort { get; set; } = 9000;
    }
}
