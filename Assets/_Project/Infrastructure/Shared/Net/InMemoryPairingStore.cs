using System.Collections.Generic;
using ScreenPortal.App.Connection;

namespace ScreenPortal.Infrastructure.Net
{
    /// <summary>Pareamento sem persistência (testes e Editor).</summary>
    public sealed class InMemoryPairingStore : IPairingStore
    {
        private readonly Dictionary<string, string> _tokens = new Dictionary<string, string>();
        private HostEndpoint? _lastHost;

        public InMemoryPairingStore(string deviceId) => DeviceId = deviceId;

        public string DeviceId { get; }
        public string GetToken(string hostName) => _tokens.TryGetValue(hostName, out var token) ? token : string.Empty;
        public void SaveToken(string hostName, string token) => _tokens[hostName] = token ?? string.Empty;
        public void SaveLastHost(HostEndpoint host) => _lastHost = host;

        public bool TryGetLastHost(out HostEndpoint host)
        {
            host = _lastHost ?? default;
            return _lastHost.HasValue;
        }
    }
}
