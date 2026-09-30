using System;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Manipulation;
using ScreenPortal.App.Menu;
using ScreenPortal.App.Screens;
using ScreenPortal.Presentation.Manipulation;
using VContainer.Unity;

namespace ScreenPortal.Composition
{
    /// <summary>Liga views, painel de status, conexão e (2B) entrada/menu/manipulação; a tela do PC vem da conexão.</summary>
    public sealed class AppEntryPoint : IStartable, ITickable, IDisposable
    {
        private readonly ScreenViewSynchronizer _synchronizer;
        private readonly ConnectionStateMachine _connection;
        private readonly IControlChannel _channel;
        private readonly IHostDiscovery _discovery;
        private readonly AppEnvironment _env;
        private readonly InteractionRouter _router;
        private readonly ManipulationPresenter _presenter;
        private readonly WristMenu _menu;

        public AppEntryPoint(ScreenViewSynchronizer synchronizer, ConnectionStateMachine connection,
            IControlChannel channel, IHostDiscovery discovery, AppEnvironment env,
            InteractionRouter router, ManipulationPresenter presenter, WristMenu menu)
        {
            _synchronizer = synchronizer;
            _connection = connection;
            _channel = channel;
            _discovery = discovery;
            _env = env;
            _router = router;
            _presenter = presenter;
            _menu = menu;
        }

        public void Start()
        {
            _synchronizer.Start();
            if (_env.StatusView != null) _env.StatusView.Bind(_connection);
            if (_env.Recenter != null) _env.Recenter.Recentered += OnSystemRecentered;
            _connection.Start();
        }

        public void Tick()
        {
            _connection.Tick();
            _router.Tick();
            _presenter.Tick();
        }

        private void OnSystemRecentered() => _menu.Invoke(MenuItemId.Recenter);

        public void Dispose()
        {
            if (_env.Recenter != null) _env.Recenter.Recentered -= OnSystemRecentered;
            (_env.Recenter as IDisposable)?.Dispose();
            _presenter.Dispose();
            _channel.Close();
            _discovery.Stop();
            _synchronizer.Dispose();
        }
    }
}
