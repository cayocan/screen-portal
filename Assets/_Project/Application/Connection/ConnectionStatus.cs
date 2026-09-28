using System;

namespace ScreenPortal.App.Connection
{
    public enum ConnectionState
    {
        Idle,
        Discovering,
        Connecting,
        Negotiating,
        Streaming,
        Degraded,
        Reconnecting,
    }

    public enum ConnectionStatusKind
    {
        Idle,
        Searching,
        InstallHost,
        Connecting,
        ConfirmOnPc,
        Connected,
        Unstable,
        Reconnecting,
        Rejected,
        IncompatibleVersion,
        HostShutdown,
    }

    /// <summary>O que mostrar ao usuário (o texto em si fica em <see cref="ConnectionStatusText"/>).</summary>
    public readonly struct ConnectionStatus : IEquatable<ConnectionStatus>
    {
        public ConnectionStatusKind Kind { get; }
        public string HostName { get; }
        public string PairingCode { get; }

        public ConnectionStatus(ConnectionStatusKind kind, string hostName = "", string pairingCode = "")
        {
            Kind = kind;
            HostName = hostName ?? string.Empty;
            PairingCode = pairingCode ?? string.Empty;
        }

        public bool Equals(ConnectionStatus other) =>
            Kind == other.Kind && HostName == other.HostName && PairingCode == other.PairingCode;
        public override bool Equals(object obj) => obj is ConnectionStatus other && Equals(other);
        public override int GetHashCode() => (Kind, HostName, PairingCode).GetHashCode();
        public override string ToString() => Kind + " " + HostName + " " + PairingCode;
    }
}
