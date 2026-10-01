using System;

namespace ScreenPortal.App.Connection
{
    /// <summary>
    /// Ciclo de conexão do headset (brief §8.5, spec 1B §3, 2C §5):
    /// Idle → Discovering → Connecting → Negotiating → Streaming ⇄ Degraded → Reconnecting.
    /// Só conexão: telas e streams são do <c>HostSync</c>, que ouve <see cref="Welcomed"/>, <see cref="Lost"/> e
    /// <see cref="ScreenMessage"/> e envia por <see cref="Send"/>. Puro; <see cref="Tick"/> 1× por quadro.
    /// </summary>
    public sealed class ConnectionStateMachine
    {
        private readonly IHostDiscovery _discovery;
        private readonly IControlChannel _channel;
        private readonly IPairingStore _pairing;
        private readonly IClock _clock;
        private readonly ConnectionOptions _options;

        private HostEndpoint _host;
        private double _stateSince;
        private double _lastPingAt;
        private double _lastPongAt;
        private double _lastAttemptAt;
        private bool _attemptInFlight;
        private bool _awaitingPairing;
        private bool _welcomed;
        private bool _hasConnectedBefore;
        private bool _linked;
        private string _blockedHost;
        private double _blockedUntil;

        public ConnectionState State { get; private set; } = ConnectionState.Idle;
        public ConnectionStatus Status { get; private set; } = new ConnectionStatus(ConnectionStatusKind.Idle);
        /// <summary>true entre o Welcome e a queda: dá para falar com o host.</summary>
        public bool IsLinked => _linked;
        /// <summary>IP do PC conectado (2G: destino do microfone).</summary>
        public string HostAddress => _host.Address;

        public event Action<ConnectionStatus> StatusChanged;
        /// <summary>Welcome recebido (conexão nova ou reconexão). IsLinked já é true.</summary>
        public event Action<WelcomeMessage> Welcomed;
        /// <summary>A conexão caiu (IsLinked já é false).</summary>
        public event Action Lost;
        /// <summary>ScreenReady, ScreenFailed, StreamStarted e StreamStopped.</summary>
        public event Action<ControlMessage> ScreenMessage;

        public ConnectionStateMachine(IHostDiscovery discovery, IControlChannel channel, IPairingStore pairing, IClock clock,
            ConnectionOptions options)
        {
            _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _pairing = pairing ?? throw new ArgumentNullException(nameof(pairing));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public void Start()
        {
            if (State != ConnectionState.Idle) return;
            _hasConnectedBefore = _pairing.TryGetLastHost(out _);
            _discovery.Start();
            EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
        }

        /// <summary>Envia ao host se a conexão estiver de pé; senão descarta (o HostSync reenvia após o Welcome).</summary>
        public void Send(ControlMessage message)
        {
            if (_linked) _channel.Send(message);
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
                    if (!_welcomed && !_awaitingPairing && now - _stateSince > _options.NegotiateTimeoutSeconds)
                    {
                        _channel.Close();
                        EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
                    }
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
                    OnWelcome(welcome, now);
                    break;
                case ScreenReadyMessage _:
                case ScreenFailedMessage _:
                case StreamStartedMessage _:
                case StreamStoppedMessage _:
                case AudioStartedMessage _:
                case HotkeyMessage _:
                case CursorShapeMessage _:
                    ScreenMessage?.Invoke(message);
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

        private void OnWelcome(WelcomeMessage welcome, double now)
        {
            if (State != ConnectionState.Negotiating) return;
            _welcomed = true;
            _awaitingPairing = false;
            _hasConnectedBefore = true;
            _pairing.SaveLastHost(_host);
            _lastPongAt = now;
            _lastPingAt = now;
            Enter(ConnectionState.Streaming, new ConnectionStatus(ConnectionStatusKind.Connected, _host.Name));
            _linked = true;
            Welcomed?.Invoke(welcome);
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
                    EnterDiscovering(new ConnectionStatus(ConnectionStatusKind.Searching));
                    break;
            }
        }

        private void Block(ConnectionStatusKind statusKind, double cooldownSeconds)
        {
            _channel.Close();
            _blockedHost = _host.Name;
            _blockedUntil = _clock.NowSeconds + cooldownSeconds;
            EnterDiscovering(new ConnectionStatus(statusKind, _host.Name));
        }

        private bool IsBlocked(HostEndpoint host, double now) => host.Name == _blockedHost && now < _blockedUntil;

        private void Unlink()
        {
            if (!_linked) return;
            _linked = false;
            Lost?.Invoke();
        }

        private void EnterNegotiating()
        {
            _welcomed = false;
            _awaitingPairing = false;
            Enter(ConnectionState.Negotiating, new ConnectionStatus(ConnectionStatusKind.Connecting, _host.Name));
            _channel.Send(new HelloMessage(_pairing.DeviceId, _options.DeviceName, _pairing.GetToken(_host.Name)));
        }

        private void EnterReconnecting()
        {
            Unlink();
            _attemptInFlight = false;
            _lastAttemptAt = double.NegativeInfinity; // primeira tentativa no próximo Tick
            Enter(ConnectionState.Reconnecting, new ConnectionStatus(ConnectionStatusKind.Reconnecting, _host.Name));
        }

        private void EnterDiscovering(ConnectionStatus status)
        {
            Unlink();
            _welcomed = false;
            _awaitingPairing = false;
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
