using System;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Manipulation;
using ScreenPortal.App.Menu;
using ScreenPortal.App.Screens;
using ScreenPortal.Presentation.Manipulation;
using ScreenPortal.Presentation.Screens;
using VContainer.Unity;

namespace ScreenPortal.Composition
{
    /// <summary>Liga views, status, conexão, telas com vídeo (HostSync) e entrada/menu/manipulação.</summary>
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
        private readonly HostSync _hostSync;
        private readonly OverlayScreenViewFactory _views;

        public AppEntryPoint(ScreenViewSynchronizer synchronizer, ConnectionStateMachine connection,
            IControlChannel channel, IHostDiscovery discovery, AppEnvironment env,
            InteractionRouter router, ManipulationPresenter presenter, WristMenu menu,
            HostSync hostSync, OverlayScreenViewFactory views)
        {
            _synchronizer = synchronizer;
            _connection = connection;
            _channel = channel;
            _discovery = discovery;
            _env = env;
            _router = router;
            _presenter = presenter;
            _menu = menu;
            _hostSync = hostSync;
            _views = views;
        }

        public void Start()
        {
            _views.Bind(_hostSync);
            _synchronizer.Start();
            _hostSync.Start();
            if (_env.StatusView != null) _env.StatusView.Bind(_connection);
            if (_env.Recenter != null) _env.Recenter.Recentered += OnSystemRecentered;
            _connection.Start();
        }

        public void Tick()
        {
            _connection.Tick();
            _hostSync.Tick();
            _router.Tick();
            _presenter.Tick();
        }

        private void OnSystemRecentered() => _menu.Invoke(MenuItemId.Recenter);

        public void Dispose()
        {
            if (_env.Recenter != null) _env.Recenter.Recentered -= OnSystemRecentered;
            (_env.Recenter as IDisposable)?.Dispose();
            _presenter.Dispose();
            _hostSync.Dispose();
            _views.Bind(null);
            _channel.Close();
            _discovery.Stop();
            _synchronizer.Dispose();
        }
    }
}
