using System;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Screens;
using VContainer.Unity;

namespace ScreenPortal.Composition
{
    /// <summary>Liga views, painel de status e a conexão; a tela do PC é criada pela conexão (Welcome).</summary>
    public sealed class AppEntryPoint : IStartable, ITickable, IDisposable
    {
        private readonly ScreenViewSynchronizer _synchronizer;
        private readonly ConnectionStateMachine _connection;
        private readonly IControlChannel _channel;
        private readonly IHostDiscovery _discovery;
        private readonly AppEnvironment _env;

        public AppEntryPoint(ScreenViewSynchronizer synchronizer, ConnectionStateMachine connection,
            IControlChannel channel, IHostDiscovery discovery, AppEnvironment env)
        {
            _synchronizer = synchronizer;
            _connection = connection;
            _channel = channel;
            _discovery = discovery;
            _env = env;
        }

        public void Start()
        {
            _synchronizer.Start();
            if (_env.StatusView != null) _env.StatusView.Bind(_connection);
            _connection.Start();
        }

        public void Tick() => _connection.Tick();

        public void Dispose()
        {
            _channel.Close();
            _discovery.Stop();
            _synchronizer.Dispose();
        }
    }
}
