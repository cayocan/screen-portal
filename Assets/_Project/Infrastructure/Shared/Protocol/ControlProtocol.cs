namespace ScreenPortal.Infrastructure.Protocol
{
    /// <summary>Constantes do canal de controle v2 (protocol/control-v2.md).</summary>
    public static class ControlProtocol
    {
        public const ushort Version = 2;
        public const int Port = 9200;
        public const string ServiceType = "_screenportal._tcp";
        public const int MaxFrameBytes = 64 * 1024;
    }
}
