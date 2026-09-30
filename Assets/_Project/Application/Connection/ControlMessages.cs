using System;
using System.Collections.Generic;

namespace ScreenPortal.App.Connection
{
    public enum ControlMessageKind
    {
        Hello,
        PairingRequired,
        PairingResult,
        Welcome,
        StartStream,
        StreamStarted,
        StreamStopped,
        Ping,
        Pong,
        Bye,
        AcquireScreen,
        ScreenReady,
        ScreenFailed,
        ReleaseScreen,
    }

    public enum ByeReason
    {
        Shutdown,
        IncompatibleVersion,
        PairingRejected,
        Replaced,
    }

    /// <summary>Tipo de monitor pedido ao host (spec 2C §4).</summary>
    public enum ScreenKind
    {
        Virtual = 0,
        Mirror = 1,
    }

    /// <summary>Mensagem do canal de controle, independente de FlatBuffers (o codec fica na Infrastructure).</summary>
    public abstract class ControlMessage
    {
        public abstract ControlMessageKind Kind { get; }
    }

    public sealed class HelloMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.Hello;
        public string DeviceId { get; }
        public string DeviceName { get; }
        public string PairingToken { get; }

        public HelloMessage(string deviceId, string deviceName, string pairingToken)
        {
            DeviceId = deviceId ?? string.Empty;
            DeviceName = deviceName ?? string.Empty;
            PairingToken = pairingToken ?? string.Empty;
        }
    }

    public sealed class PairingRequiredMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.PairingRequired;
        public string Code { get; }

        public PairingRequiredMessage(string code) => Code = code ?? string.Empty;
    }

    public sealed class PairingResultMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.PairingResult;
        public bool Accepted { get; }
        public string PairingToken { get; }

        public PairingResultMessage(bool accepted, string pairingToken)
        {
            Accepted = accepted;
            PairingToken = pairingToken ?? string.Empty;
        }
    }

    public sealed class MonitorDescriptor
    {
        public uint Id { get; }
        public uint Width { get; }
        public uint Height { get; }
        public string Name { get; }
        /// <summary>Nome sem o prefixo do Windows (<c>\\.\DISPLAY2</c> → <c>DISPLAY2</c>) para mostrar no headset.</summary>
        public string FriendlyName { get; }
        public bool IsPrimary { get; }

        public MonitorDescriptor(uint id, uint width, uint height, string name, bool isPrimary)
        {
            Id = id;
            Width = width;
            Height = height;
            Name = name ?? string.Empty;
            FriendlyName = Name.StartsWith(@"\\.\", StringComparison.Ordinal) ? Name.Substring(4) : Name;
            IsPrimary = isPrimary;
        }
    }

    public sealed class WelcomeMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.Welcome;
        public string HostName { get; }
        public IReadOnlyList<MonitorDescriptor> Monitors { get; }
        /// <summary>Monitores que o host ainda mantém para este headset (v2).</summary>
        public IReadOnlyList<ScreenLeaseInfo> Leases { get; }

        public WelcomeMessage(string hostName, IReadOnlyList<MonitorDescriptor> monitors, IReadOnlyList<ScreenLeaseInfo> leases = null)
        {
            HostName = hostName ?? string.Empty;
            Monitors = monitors ?? Array.Empty<MonitorDescriptor>();
            Leases = leases ?? Array.Empty<ScreenLeaseInfo>();
        }
    }

    public sealed class StartStreamMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.StartStream;
        /// <summary>Tela (= ScreenId) cujo monitor o host deve transmitir (v2).</summary>
        public uint ScreenKey { get; }
        public ushort UdpPort { get; }
        public ushort StreamId { get; }

        public StartStreamMessage(uint screenKey, ushort udpPort, ushort streamId)
        {
            ScreenKey = screenKey;
            UdpPort = udpPort;
            StreamId = streamId;
        }
    }

    public sealed class StreamStartedMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.StreamStarted;
        public ushort StreamId { get; }
        public uint Width { get; }
        public uint Height { get; }

        public StreamStartedMessage(ushort streamId, uint width, uint height)
        {
            StreamId = streamId;
            Width = width;
            Height = height;
        }
    }

    public sealed class StreamStoppedMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.StreamStopped;
        public ushort StreamId { get; }
        public string Reason { get; }

        public StreamStoppedMessage(ushort streamId, string reason)
        {
            StreamId = streamId;
            Reason = reason ?? string.Empty;
        }
    }

    public sealed class PingMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.Ping;
        public ulong SentUs { get; }

        public PingMessage(ulong sentUs) => SentUs = sentUs;
    }

    public sealed class PongMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.Pong;
        public ulong SentUs { get; }

        public PongMessage(ulong sentUs) => SentUs = sentUs;
    }

    public sealed class ByeMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.Bye;
        public ByeReason Reason { get; }

        public ByeMessage(ByeReason reason) => Reason = reason;
    }

    public sealed class ScreenLeaseInfo
    {
        public uint ScreenKey { get; }
        public ScreenKind Kind { get; }
        public uint Width { get; }
        public uint Height { get; }

        public ScreenLeaseInfo(uint screenKey, ScreenKind kind, uint width, uint height)
        {
            ScreenKey = screenKey;
            Kind = kind;
            Width = width;
            Height = height;
        }
    }

    /// <summary>Headset → host: crie (ou reate) o monitor desta tela.</summary>
    public sealed class AcquireScreenMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.AcquireScreen;
        public uint ScreenKey { get; }
        public ScreenKind ScreenKind { get; }
        public uint Width { get; }
        public uint Height { get; }
        public uint PhysicalMonitorId { get; }

        public AcquireScreenMessage(uint screenKey, ScreenKind kind, uint width, uint height, uint physicalMonitorId)
        {
            ScreenKey = screenKey;
            ScreenKind = kind;
            Width = width;
            Height = height;
            PhysicalMonitorId = physicalMonitorId;
        }
    }

    public sealed class ScreenReadyMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.ScreenReady;
        public uint ScreenKey { get; }
        public uint Width { get; }
        public uint Height { get; }

        public ScreenReadyMessage(uint screenKey, uint width, uint height)
        {
            ScreenKey = screenKey;
            Width = width;
            Height = height;
        }
    }

    public sealed class ScreenFailedMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.ScreenFailed;
        public uint ScreenKey { get; }
        public string Reason { get; }

        public ScreenFailedMessage(uint screenKey, string reason)
        {
            ScreenKey = screenKey;
            Reason = reason ?? string.Empty;
        }
    }

    /// <summary>Headset → host: pare o stream e remova o monitor depois de <see cref="GraceMs"/> (undo reata).</summary>
    public sealed class ReleaseScreenMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.ReleaseScreen;
        public uint ScreenKey { get; }
        public uint GraceMs { get; }

        public ReleaseScreenMessage(uint screenKey, uint graceMs)
        {
            ScreenKey = screenKey;
            GraceMs = graceMs;
        }
    }
}
