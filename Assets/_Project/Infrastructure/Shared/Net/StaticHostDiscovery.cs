using ScreenPortal.App.Connection;

namespace ScreenPortal.Infrastructure.Net
{
    /// <summary>"Descoberta" com endereço fixo: Editor da Unity e testes de integração no PC.</summary>
    public sealed class StaticHostDiscovery : IHostDiscovery
    {
        private readonly HostEndpoint _host;
        private bool _running;

        public StaticHostDiscovery(HostEndpoint host) => _host = host;

        public void Start() => _running = true;
        public void Stop() => _running = false;

        public bool TryGetHost(out HostEndpoint host)
        {
            host = _host;
            return _running && _host.IsValid;
        }
    }
}
