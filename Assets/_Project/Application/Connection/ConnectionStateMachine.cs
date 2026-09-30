using System;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Connection
{
    /// <summary>
    /// Ciclo de conexão do headset (brief §8.5, spec 1B §3):
    /// Idle → Discovering → Connecting → Negotiating → Streaming ⇄ Degraded → Reconnecting.
    /// Puro: tempo e I/O entram por portas; chamar <see cref="Tick"/> uma vez por frame na thread principal.
    /// </summary>
    public sealed class ConnectionStateMachine
    {
        public const string MonitorLabelPrefix = "monitor:";

        private readonly IHostDiscovery _discovery;
        private readonly IControlChannel _channel;
        private readonly IPairingStore _pairing;
        private readonly IClock _clock;
        private readonly ICommandBus _bus;
        private readonly WorkspaceStore _store;
        private readonly IVideoStreamSink _video;
        private readonly ConnectionOptions _options;

        private HostEndpoint _host;
        private double _stateSince;
        private double _lastPingAt;
        private double _lastPongAt;
        private double _lastAttemptAt;
        private bool _attemptInFlight;
        private bool _awaitingPairing;
        private bool _welcomed;
        private bool _streamRequested;
        private bool _videoStarted;
        private bool _hasConnectedBefore;
        private MonitorDescriptor _monitor;
        private ScreenId _screen;
        private string _blockedHost;
        private double _blockedUntil;

        public ConnectionState State { get; private set; } = ConnectionState.Idle;
        public ConnectionStatus Status { get; private set; } = new ConnectionStatus(ConnectionStatusKind.Idle);
        public event Action<ConnectionStatus> StatusChanged;

        public ConnectionStateMachine(
            IHostDiscovery discovery,
            IControlChannel channel,
            IPairingStore pairing,
            IClock clock,
            ICommandBus bus,
            WorkspaceStore store,
            IVideoStreamSink video,
            ConnectionOptions options)
        {
            _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _pairing = pairing ?? throw new ArgumentNullException(nameof(pairing));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _video = video ?? throw new ArgumentNullException(nameof(video));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public void Start()
        {
            if (State != ConnectionState.Idle) return;
            _hasConnectedBefore = _pairing.TryGetLastHost(out _);
            _discovery.Start();
            EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
        }

        public void Tick()
        {
            ChannelEventKind kind;
            while ((kind = _channel.Poll(out var message)) != ChannelEventKind.None)
                Handle(kind, message);

            double now = _clock.NowSeconds;
            switch (State)
            {
                case ConnectionState.Discovering:
                    TickDiscovering(now);
                    break;
                case ConnectionState.Connecting:
                    if (now - _stateSince > _options.ConnectTimeoutSeconds)
                    {
                        _channel.Close();
                        EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
                    }
                    break;
                case ConnectionState.Negotiating:
                    TickNegotiating(now);
                    break;
                case ConnectionState.Streaming:
                case ConnectionState.Degraded:
                    TickStreaming(now);
                    break;
                case ConnectionState.Reconnecting:
                    TickReconnecting(now);
                    break;
            }
        }

        private void TickDiscovering(double now)
        {
            if (_discovery.TryGetHost(out var host) && !IsBlocked(host, now))
            {
                _host = host;
                _channel.Connect(host);
                Enter(ConnectionState.Connecting, new ConnectionStatus(ConnectionStatusKind.Connecting, host.Name));
                return;
            }
            if (!_hasConnectedBefore
                && Status.Kind == ConnectionStatusKind.Searching
                && now - _stateSince > _options.InstallHintAfterSeconds)
            {
                SetStatus(new ConnectionStatus(ConnectionStatusKind.InstallHost));
            }
        }

        private void TickNegotiating(double now)
        {
            if (!_welcomed && !_awaitingPairing && now - _stateSince > _options.NegotiateTimeoutSeconds)
            {
                _channel.Close();
                EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
                return;
            }
            if (_welcomed && !_streamRequested) TryRequestStream();
        }

        private void TickStreaming(double now)
        {
            if (now - _lastPingAt >= _options.PingIntervalSeconds)
            {
                _lastPingAt = now;
                _channel.Send(new PingMessage((ulong)(now * 1_000_000.0)));
            }

            double silence = now - _lastPongAt;
            if (State == ConnectionState.Streaming && silence > _options.DegradedAfterSeconds)
            {
                Enter(ConnectionState.Degraded, new ConnectionStatus(ConnectionStatusKind.Unstable, _host.Name));
            }
            else if (State == ConnectionState.Degraded
                     && silence > _options.DegradedAfterSeconds + _options.ReconnectAfterDegradedSeconds)
            {
                _channel.Close();
                EnterReconnecting();
            }
        }

        private void TickReconnecting(double now)
        {
            if (now - _stateSince > _options.ReconnectGiveUpSeconds)
            {
                StopVideo();
                EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
                return;
            }
            if (!_attemptInFlight && now - _lastAttemptAt >= _options.RetryIntervalSeconds)
            {
                _lastAttemptAt = now;
                _attemptInFlight = true;
                _channel.Connect(_host);
            }
        }

        private void Handle(ChannelEventKind kind, ControlMessage message)
        {
            switch (kind)
            {
                case ChannelEventKind.Connected:
                    if (State == ConnectionState.Connecting || State == ConnectionState.Reconnecting)
                    {
                        _attemptInFlight = false;
                        EnterNegotiating();
                    }
                    break;
                case ChannelEventKind.ConnectFailed:
                    if (State == ConnectionState.Connecting)
                        EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
                    else if (State == ConnectionState.Reconnecting)
                        _attemptInFlight = false;
                    break;
                case ChannelEventKind.Disconnected:
                    if (State == ConnectionState.Streaming || State == ConnectionState.Degraded
                        || (State == ConnectionState.Negotiating && _welcomed))
                        EnterReconnecting();
                    else if (State == ConnectionState.Connecting || State == ConnectionState.Negotiating)
                        EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
                    break;
                case ChannelEventKind.Message:
                    if (message != null) HandleMessage(message);
                    break;
            }
        }

        private void HandleMessage(ControlMessage message)
        {
            double now = _clock.NowSeconds;
            switch (message)
            {
                case PairingRequiredMessage pairing:
                    _awaitingPairing = true;
                    SetStatus(new ConnectionStatus(ConnectionStatusKind.ConfirmOnPc, _host.Name, pairing.Code));
                    break;
                case PairingResultMessage result:
                    if (result.Accepted)
                    {
                        _pairing.SaveToken(_host.Name, result.PairingToken);
                        _awaitingPairing = false;
                        _stateSince = now; // o timeout da negociação recomeça: o tempo do usuário decidindo não conta
                        SetStatus(new ConnectionStatus(ConnectionStatusKind.Connecting, _host.Name));
                    }
                    else
                    {
                        Block(ConnectionStatusKind.Rejected, _options.RejectCooldownSeconds);
                    }
                    break;
                case WelcomeMessage welcome:
                    OnWelcome(welcome);
                    break;
                case StreamStartedMessage _:
                    if (State == ConnectionState.Negotiating)
                    {
                        _lastPongAt = now;
                        _lastPingAt = now;
                        _hasConnectedBefore = true;
                        Enter(ConnectionState.Streaming, new ConnectionStatus(ConnectionStatusKind.Connected, _host.Name));
                    }
                    break;
                case StreamStoppedMessage _:
                    _streamRequested = false; // tenta de novo no próximo Tick
                    break;
                case PingMessage ping:
                    _channel.Send(new PongMessage(ping.SentUs));
                    break;
                case PongMessage _:
                    _lastPongAt = now;
                    if (State == ConnectionState.Degraded)
                        Enter(ConnectionState.Streaming, new ConnectionStatus(ConnectionStatusKind.Connected, _host.Name));
                    break;
                case ByeMessage bye:
                    OnBye(bye.Reason);
                    break;
            }
        }

        private void OnWelcome(WelcomeMessage welcome)
        {
            _welcomed = true;
            _awaitingPairing = false;
            _pairing.SaveLastHost(_host);

            _monitor = null;
            for (int i = 0; i < welcome.Monitors.Count; i++)
                if (welcome.Monitors[i].IsPrimary) _monitor = welcome.Monitors[i];
            if (_monitor == null && welcome.Monitors.Count > 0) _monitor = welcome.Monitors[0];
            if (_monitor == null)
            {
                _channel.Close();
                EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
                return;
            }

            _screen = EnsureScreenFor(_monitor);
            TryRequestStream();
        }

        /// <summary>Só cria a tela se ainda não existir (spec 1B §3: reconexão não duplica nem mexe no layout).</summary>
        private ScreenId EnsureScreenFor(MonitorDescriptor monitor)
        {
            string label = MonitorLabelPrefix + monitor.Id;
            var screens = _store.State.Screens;
            for (int i = 0; i < screens.Count; i++)
                if (screens[i].Label == label) return screens[i].Id;

            var resolution = ResolutionFit.FromCapture((int)monitor.Width, (int)monitor.Height);
            if (!resolution.IsValid) resolution = Resolution.Create(1920, 1080);
            var result = _bus.Send(new CreateScreen(resolution, null,_options.DefaultScreenWidthMeters, label: label));
            return result.Accepted ? _store.State.FocusedScreenId : ScreenId.None;
        }

        private void TryRequestStream()
        {
            if (_screen.IsNone || !_store.State.TryGetScreen(_screen, out var screen)) return;
            if (!_videoStarted)
                _videoStarted = _video.TryStart(_screen, _options.StreamId, _options.VideoPort, screen.Resolution);
            if (!_videoStarted) return; // Surface da layer ainda não existe: tenta no próximo Tick

            _channel.Send(new StartStreamMessage(_monitor.Id, (ushort)_options.VideoPort, _options.StreamId));
            _streamRequested = true;
        }

        private void OnBye(ByeReason reason)
        {
            switch (reason)
            {
                case ByeReason.Shutdown:
                    // Bloqueio curto: o anúncio mDNS demora a sumir; sem isso reconectaríamos na hora e a
                    // mensagem "encerrou" piscaria e sumiria.
                    Block(ConnectionStatusKind.HostShutdown, _options.ShutdownCooldownSeconds);
                    break;
                case ByeReason.IncompatibleVersion:
                    Block(ConnectionStatusKind.IncompatibleVersion, _options.IncompatibleCooldownSeconds);
                    break;
                case ByeReason.PairingRejected:
                    Block(ConnectionStatusKind.Rejected, _options.RejectCooldownSeconds);
                    break;
                default:
                    _channel.Close();
                    StopVideo();
                    EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
                    break;
            }
        }

        private void Block(ConnectionStatusKind statusKind, double cooldownSeconds)
        {
            _channel.Close();
            StopVideo();
            _blockedHost = _host.Name;
            _blockedUntil = _clock.NowSeconds + cooldownSeconds;
            EnterDiscovering(new ConnectionStatus(statusKind, _host.Name));
        }

        private bool IsBlocked(HostEndpoint host, double now) => host.Name == _blockedHost && now < _blockedUntil;

        private void StopVideo()
        {
            if (_videoStarted) _video.Stop(_options.StreamId);
            _videoStarted = false;
            _streamRequested = false;
        }

        private void EnterNegotiating()
        {
            _welcomed = false;
            _awaitingPairing = false;
            _streamRequested = false;
            Enter(ConnectionState.Negotiating, new ConnectionStatus(ConnectionStatusKind.Connecting, _host.Name));
            _channel.Send(new HelloMessage(_pairing.DeviceId, _options.DeviceName, _pairing.GetToken(_host.Name)));
        }

        private void EnterReconnecting()
        {
            _attemptInFlight = false;
            _lastAttemptAt = double.NegativeInfinity; // primeira tentativa no próximo Tick
            _streamRequested = false;
            Enter(ConnectionState.Reconnecting, new ConnectionStatus(ConnectionStatusKind.Reconnecting, _host.Name));
        }

        private void EnterDiscovering(ConnectionStatus status)
        {
            _welcomed = false;
            _awaitingPairing = false;
            _streamRequested = false;
            _attemptInFlight = false;
            Enter(ConnectionState.Discovering, status);
        }

        private void Enter(ConnectionState state, ConnectionStatus status)
        {
            State = state;
            _stateSince = _clock.NowSeconds;
            SetStatus(status);
        }

        private void SetStatus(ConnectionStatus status)
        {
            if (status.Equals(Status)) return;
            Status = status;
            StatusChanged?.Invoke(status);
        }
    }
}
