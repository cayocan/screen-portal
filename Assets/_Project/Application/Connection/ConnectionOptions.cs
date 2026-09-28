using ScreenPortal.Domain.Spatial;

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
        public int VideoPort { get; set; } = 9000;
        public ushort StreamId { get; set; } = 0;
        public Pose DefaultScreenPose { get; set; } = Pose.At(new Vec3(0f, 1.2f, 1.3f));
        public float DefaultScreenWidthMeters { get; set; } = 1.4f;
    }
}
