using System;

namespace ScreenPortal.App.Connection
{
    /// <summary>Host encontrado (mDNS) ou lembrado. Formato texto: "nome|endereço|porta".</summary>
    public readonly struct HostEndpoint : IEquatable<HostEndpoint>
    {
        public string Name { get; }
        public string Address { get; }
        public int Port { get; }

        public HostEndpoint(string name, string address, int port)
        {
            Name = name ?? string.Empty;
            Address = address ?? string.Empty;
            Port = port;
        }

        public bool IsValid => Address.Length > 0 && Port > 0 && Port < 65536;

        public static bool TryParse(string text, out HostEndpoint host)
        {
            host = default;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Split('|');
            if (parts.Length != 3 || !int.TryParse(parts[2], out int port)) return false;
            host = new HostEndpoint(parts[0], parts[1], port);
            return host.IsValid;
        }

        public bool Equals(HostEndpoint other) => Name == other.Name && Address == other.Address && Port == other.Port;
        public override bool Equals(object obj) => obj is HostEndpoint other && Equals(other);
        public override int GetHashCode() => (Name, Address, Port).GetHashCode();
        public override string ToString() => Name + "|" + Address + "|" + Port;
    }
}
