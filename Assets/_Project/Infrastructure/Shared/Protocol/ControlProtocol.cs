namespace ScreenPortal.Infrastructure.Protocol
{
    /// <summary>Constantes do canal de controle v1 (protocol/control-v1.md).</summary>
    public static class ControlProtocol
    {
        public const ushort Version = 1;
        public const int Port = 9200;
        public const string ServiceType = "_screenportal._tcp";
        public const int MaxFrameBytes = 64 * 1024;
    }
}
