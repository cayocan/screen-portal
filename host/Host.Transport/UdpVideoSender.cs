using System.Net;
using System.Net.Sockets;
using ScreenPortal.Host.Domain.Video;

namespace ScreenPortal.Host.Transport;

/// <summary>Envia pacotes de vídeo prontos para um destino UDP. SendTo com SocketAddress: sem alocação por pacote.</summary>
public sealed class UdpVideoSender : IPacketSink, IDisposable
{
    private readonly Socket _socket;
    private readonly SocketAddress _destination;

    public long PacketsSent;
    public long BytesSent;
    public long SendErrors;

    public IPEndPoint Destination { get; }

    public UdpVideoSender(IPEndPoint destination)
    {
        Destination = destination;
        _socket = new Socket(destination.AddressFamily, SocketType.Dgram, ProtocolType.Udp)
        {
            SendBufferSize = 4 * 1024 * 1024,
        };
        // DSCP EF (46) → Wi-Fi WMM "voice/video". Ignorado se a rede não respeitar.
        try { _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, 46 << 2); } catch (SocketException) { }
        _destination = destination.Serialize();
    }

    public void OnPacket(ReadOnlySpan<byte> packet)
    {
        try
        {
            _socket.SendTo(packet, SocketFlags.None, _destination);
            PacketsSent++;
            BytesSent += packet.Length;
        }
        catch (SocketException)
        {
            SendErrors++; // headset ausente/porta fechada: o stream segue; o receptor pede keyframe ao voltar
        }
    }

    public void Dispose() => _socket.Dispose();
}
