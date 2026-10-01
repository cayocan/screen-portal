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
        SetStreamRate,
        StartAudio,
        AudioStarted,
        LayoutChanged,
        Hotkey,
        StartCursor,
        CursorShape,
        CursorEnter,
        SetEditMode,
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
    /// <summary>2F: fps e bitrate de uma tela com stream (headset → host), aplicados ao vivo.</summary>
    public sealed class SetStreamRateMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.SetStreamRate;
        public uint ScreenKey { get; }
        public byte Fps { get; }
        public uint Kbps { get; }

        public SetStreamRateMessage(uint screenKey, byte fps, uint kbps)
        {
            ScreenKey = screenKey;
            Fps = fps;
            Kbps = kbps;
        }
    }

    /// <summary>2G: liga/desliga o som do PC e o microfone (headset → host). <see cref="UdpPort"/> = onde o headset recebe o som.</summary>
    public sealed class StartAudioMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.StartAudio;
        public ushort UdpPort { get; }
        public bool Playback { get; }
        public bool Mic { get; }

        public StartAudioMessage(ushort udpPort, bool playback, bool mic)
        {
            UdpPort = udpPort;
            Playback = playback;
            Mic = mic;
        }
    }

    /// <summary>2G: resposta do host: porta do microfone e o que está disponível (drivers do Steam).</summary>
    public sealed class AudioStartedMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.AudioStarted;
        public ushort MicPort { get; }
        public bool Speakers { get; }
        public bool Mic { get; }

        public AudioStartedMessage(ushort micPort, bool speakers, bool mic)
        {
            MicPort = micPort;
            Speakers = speakers;
            Mic = mic;
        }
    }

    /// <summary>3A: onde uma tela está na superfície (ângulos do centro e meio-tamanho angular).</summary>
    public readonly struct ScreenAnglesInfo
    {
        public readonly uint ScreenKey;
        public readonly float AzimuthDeg;
        public readonly float ElevationDeg;
        public readonly float HalfWidthDeg;
        public readonly float HalfHeightDeg;

        public ScreenAnglesInfo(uint screenKey, float azimuthDeg, float elevationDeg, float halfWidthDeg, float halfHeightDeg)
        {
            ScreenKey = screenKey;
            AzimuthDeg = azimuthDeg;
            ElevationDeg = elevationDeg;
            HalfWidthDeg = halfWidthDeg;
            HalfHeightDeg = halfHeightDeg;
        }
    }

    /// <summary>3A: disposição das telas no Quest (headset → host), para o arranjo do Windows seguir.</summary>
    public sealed class LayoutChangedMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.LayoutChanged;
        public IReadOnlyList<ScreenAnglesInfo> Screens { get; }

        public LayoutChangedMessage(IReadOnlyList<ScreenAnglesInfo> screens) => Screens = screens;
    }

    /// <summary>3A: ações dos atalhos globais (brief §10.9).</summary>
    public enum HotkeyAction : byte
    {
        None = 0,
        MoveLeft, MoveRight, MoveUp, MoveDown,
        Bigger, Smaller, Closer, Farther,
        CurvatureToggle, CurvatureLess, CurvatureMore,
        CycleFrameRate, Undo, Redo, Recenter, ToggleLayoutMode, AlignPosture, ToggleGrid,
        ToggleEditMode,
        /// <summary>Esc: o host só o registra com o modo edição ligado (não rouba o Esc do Windows no resto do tempo).</summary>
        Escape,
        /// <summary>Cursor 3D perdido no mundo: volta para a frente dos olhos.</summary>
        FindCursor,
        /// <summary>3C: abre/fecha o menu do app onde o cursor está.</summary>
        OpenMenu,
    }

    /// <summary>3B: o headset pede o MouseState (UDP) nesta porta.</summary>
    public sealed class StartCursorMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.StartCursor;
        public ushort UdpPort { get; }

        public StartCursorMessage(ushort udpPort) => UdpPort = udpPort;
    }

    /// <summary>
    /// 3B: forma de cursor nova (host → headset), mandada uma vez. <see cref="Bgra"/> tem Width×Height pixels; o hotspot
    /// está em pixels do bitmap; <see cref="Scale"/> = quantas vezes o bitmap foi reduzido para caber no quadro (o
    /// tamanho na tela é Width×Scale).
    /// </summary>
    public sealed class CursorShapeMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.CursorShape;
        public uint ShapeId { get; }
        public ushort Width { get; }
        public ushort Height { get; }
        public ushort HotX { get; }
        public ushort HotY { get; }
        public byte Scale { get; }
        public byte[] Bgra { get; }

        public CursorShapeMessage(uint shapeId, ushort width, ushort height, ushort hotX, ushort hotY, byte scale, byte[] bgra)
        {
            ShapeId = shapeId;
            Width = width;
            Height = height;
            HotX = hotX;
            HotY = hotY;
            Scale = scale == 0 ? (byte)1 : scale;
            Bgra = bgra ?? Array.Empty<byte>();
        }
    }

    /// <summary>3B: o cursor 3D tocou a tela no pixel (X, Y) → o host põe o cursor do Windows ali.</summary>
    public sealed class CursorEnterMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.CursorEnter;
        public uint ScreenKey { get; }
        public int X { get; }
        public int Y { get; }

        public CursorEnterMessage(uint screenKey, int x, int y)
        {
            ScreenKey = screenKey;
            X = x;
            Y = y;
        }
    }

    /// <summary>3B: modo edição ligado/desligado no headset → o host engole (ou devolve) o mouse.</summary>
    public sealed class SetEditModeMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.SetEditMode;
        public bool On { get; }

        public SetEditModeMessage(bool on) => On = on;
    }

    /// <summary>3A: atalho apertado no PC (host → headset). <see cref="ScreenKey"/> = tela sob o cursor (0 = nenhuma).</summary>
    public sealed class HotkeyMessage : ControlMessage
    {
        public override ControlMessageKind Kind => ControlMessageKind.Hotkey;
        public HotkeyAction Action { get; }
        public uint ScreenKey { get; }

        public HotkeyMessage(HotkeyAction action, uint screenKey)
        {
            Action = action;
            ScreenKey = screenKey;
        }
    }

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
