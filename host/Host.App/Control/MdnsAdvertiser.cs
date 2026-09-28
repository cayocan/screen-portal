using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using ScreenPortal.Infrastructure.Protocol;

namespace ScreenPortal.Host.App.Control;

/// <summary>
/// Anuncia "&lt;PC&gt;._screenportal._tcp.local" pela API nativa DNS-SD do Windows (dnsapi, Win10 1809+),
/// sem biblioteca de terceiros (spec 1B, decisão 4).
/// </summary>
public sealed class MdnsAdvertiser(Action<string> log) : IDisposable
{
    private const uint DnsQueryRequestVersion1 = 1;
    private const uint DnsRequestPending = 9506;

    private IntPtr _request;
    private IntPtr _instance;
    private IntPtr _ip4;
    private RegisterComplete? _callback; // mantém o delegate vivo enquanto o Windows pode chamá-lo

    public void Start(string instanceName, int port, string txtVersion)
    {
        var address = GetPrimaryIPv4();
        _ip4 = Marshal.AllocHGlobal(4);
        Marshal.Copy(address.GetAddressBytes(), 0, _ip4, 4); // IP4_ADDRESS em ordem de rede

        string service = $"{instanceName}.{ControlProtocol.ServiceType}.local";
        string host = $"{Environment.MachineName}.local";
        _instance = DnsServiceConstructInstance(service, host, _ip4, IntPtr.Zero, (ushort)port, 0, 0, 1,
            new[] { "v" }, new[] { txtVersion });
        if (_instance == IntPtr.Zero) throw new InvalidOperationException("DnsServiceConstructInstance falhou.");

        _callback = (status, _, _) => log(status == 0 ? $"mDNS: anunciado {service} ({address}:{port})" : $"mDNS: erro {status}");
        var request = new DnsServiceRegisterRequest
        {
            Version = DnsQueryRequestVersion1,
            InterfaceIndex = 0,
            ServiceInstance = _instance,
            RegisterCompletionCallback = Marshal.GetFunctionPointerForDelegate(_callback),
        };
        _request = Marshal.AllocHGlobal(Marshal.SizeOf<DnsServiceRegisterRequest>());
        Marshal.StructureToPtr(request, _request, false);

        uint result = DnsServiceRegister(_request, IntPtr.Zero);
        if (result != DnsRequestPending) throw new InvalidOperationException($"DnsServiceRegister retornou {result}.");
    }

    /// <summary>
    /// IP da interface que o Windows usa na rota padrão. "Conectar" um socket UDP não envia pacote algum, só
    /// escolhe a rota. Evita anunciar o IP de adaptadores de VPN (ex.: Radmin 26.x) que também têm gateway.
    /// </summary>
    private static IPAddress GetPrimaryIPv4()
    {
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect(new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53));
            if (probe.LocalEndPoint is IPEndPoint local && !IPAddress.IsLoopback(local.Address)) return local.Address;
        }
        catch (SocketException) { /* sem rota padrão (rede isolada): cai na busca por interface */ }

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            var props = nic.GetIPProperties();
            if (!props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork)) continue;
            var ip = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            if (ip != null) return ip.Address;
        }
        throw new InvalidOperationException("Nenhuma interface IPv4 com gateway encontrada.");
    }

    public void Dispose()
    {
        if (_request != IntPtr.Zero)
        {
            DnsServiceDeRegister(_request, IntPtr.Zero);
            // _request/_instance/_ip4 ficam alocados até o fim do processo: o DeRegister é assíncrono e
            // ainda pode ler a estrutura (vazamento único, na saída do host).
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceRegisterRequest
    {
        public uint Version;
        public uint InterfaceIndex;
        public IntPtr ServiceInstance;
        public IntPtr RegisterCompletionCallback;
        public IntPtr QueryContext;
        public IntPtr Credentials;
        public int UnicastEnabled;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void RegisterComplete(uint status, IntPtr context, IntPtr instance);

    [DllImport("dnsapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DnsServiceConstructInstance(string serviceName, string hostName, IntPtr ip4, IntPtr ip6,
        ushort port, ushort priority, ushort weight, uint propertiesCount,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] keys,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] values);

    [DllImport("dnsapi.dll")]
    private static extern uint DnsServiceRegister(IntPtr request, IntPtr cancel);

    [DllImport("dnsapi.dll")]
    private static extern uint DnsServiceDeRegister(IntPtr request, IntPtr cancel);
}
