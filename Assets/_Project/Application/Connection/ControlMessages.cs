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
    }

    public enum ByeReason
    {
        Shutdown,
        IncompatibleVersion,
        PairingRejected,
        Replaced,
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

        public WelcomeMessage(string hostName, IReadOnlyList<MonitorDescriptor> monitors)
        {
            HostName = hostName ?? string.Empty;
            Monitors = monitors ?? Array.Empty<MonitorDescriptor>();
        }
    }

    public sealed class StartStreamMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.StartStream;
        public uint MonitorId { get; }
        public ushort UdpPort { get; }
        public ushort StreamId { get; }

        public StartStreamMessage(uint monitorId, ushort udpPort, ushort streamId)
        {
            MonitorId = monitorId;
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
}
