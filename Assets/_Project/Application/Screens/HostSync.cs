using System;
using System.Collections.Generic;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Screens
{
    /// <summary>
    /// Reconcilia as telas com vídeo da workspace com os aluguéis de monitor do host (spec 2C §4, ADR 0009).
    /// A workspace é a fonte da verdade: undo, remoção adiada e reconexão caem no mesmo caminho.
    /// <see cref="Tick"/> 1× por quadro, depois de <see cref="ConnectionStateMachine.Tick"/>; sem alocação por quadro
    /// (mensagens só nas transições). Handlers de <see cref="LinkChanged"/> não podem enviar Commands
    /// (<c>Reconcile</c> não é reentrante): as views nunca mudam o estado.
    /// </summary>
    public sealed class HostSync : IHostCatalog, IScreenLinks, IDisposable
    {
        public const uint RemoveGraceMs = 10000;
        public const int MaxStreams = 16;
        public const double StreamRetrySeconds = 1.0;
        /// <summary>StreamStopped seguidos até a tela ir para Failed (espera entre tentativas: 1 s, 2 s, ...).</summary>
        public const int MaxStreamFailures = 3;
        public const string StreamFailedPrefix = "Falha ao iniciar o vídeo: ";
        public const double FirstFramePollSeconds = 1.0;

        private enum Phase
        {
            Unlinked,
            Requested,
            Ready,
            Starting,
            Streaming,
            Live,
            Failed,
        }

        private sealed class Entry
        {
            public ScreenId Id;
            public ScreenSource Source;
            public Resolution Resolution;
            public Phase Phase;
            public int Slot = -1;
            public bool VideoOpen;
            public double NotBefore;
            public string Detail = string.Empty;
            public int Reported = -1;
            public int StreamFailures; // StreamStopped seguidos; zera em StreamStarted, recriação e reconexão
            public bool Seen;
        }

        private readonly WorkspaceStore _store;
        private readonly ConnectionStateMachine _connection;
        private readonly IScreenVideo _video;
        private readonly ICommandBus _bus;
        private readonly IClock _clock;
        private readonly ConnectionOptions _options;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly bool[] _slots = new bool[MaxStreams];
        private IReadOnlyList<MonitorDescriptor> _monitors = Array.Empty<MonitorDescriptor>();
        private bool _started;
        private bool _autoMirrorDone;

        public event Action<ScreenId> LinkChanged;

        public HostSync(WorkspaceStore store, ConnectionStateMachine connection, IScreenVideo video, ICommandBus bus, IClock clock,
            ConnectionOptions options)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _video = video ?? throw new ArgumentNullException(nameof(video));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public bool IsConnected => _connection.IsLinked;
        public IReadOnlyList<MonitorDescriptor> PhysicalMonitors => _monitors;

        public void Start()
        {
            if (_started) return;
            _started = true;
            _store.StateChanged += OnStateChanged;
            _connection.Welcomed += OnWelcomed;
            _connection.Lost += OnLost;
            _connection.ScreenMessage += OnScreenMessage;
            Reconcile(_store.State);
        }

        public void Tick()
        {
            if (!_connection.IsLinked) return;
            double now = _clock.NowSeconds;
            for (int i = 0; i < _entries.Count; i++) Advance(_entries[i], now);
        }

        public bool TryGetLink(ScreenId screen, out ScreenLinkState state, out string detail)
        {
            var e = Find(screen);
            if (e == null)
            {
                state = ScreenLinkState.Offline;
                detail = string.Empty;
                return false;
            }
            state = Map(e);
            detail = e.Detail;
            return true;
        }

        private void Advance(Entry e, double now)
        {
            switch (e.Phase)
            {
                case Phase.Unlinked:
                    _connection.Send(new AcquireScreenMessage(Key(e.Id), KindOf(e.Source), (uint)e.Resolution.Width,
                        (uint)e.Resolution.Height, e.Source.PhysicalMonitorId));
                    SetPhase(e, Phase.Requested);
                    break;
                case Phase.Ready:
                    if (now < e.NotBefore) return;
                    if (e.Slot < 0) e.Slot = TakeSlot();
                    if (e.Slot < 0)
                    {
                        Fail(e, "Sem stream livre.");
                        return;
                    }
                    if (!e.VideoOpen) e.VideoOpen = _video.TryOpen(e.Id, (ushort)e.Slot, Port(e.Slot), e.Resolution);
                    if (!e.VideoOpen) return; // Surface da layer ainda não existe: tenta no próximo quadro
                    _video.ResetFirstFrame(e.Id); // depois de reconectar, o quadro antigo na layer não conta como Live
                    _connection.Send(new StartStreamMessage(Key(e.Id), (ushort)Port(e.Slot), (ushort)e.Slot));
                    SetPhase(e, Phase.Starting);
                    break;
                case Phase.Streaming:
                    if (now < e.NotBefore) return;
                    e.NotBefore = now + FirstFramePollSeconds;
                    if (_video.HasFirstFrame(e.Id)) SetPhase(e, Phase.Live);
                    break;
            }
        }

        private void OnStateChanged(Workspace previous, Workspace current) => Reconcile(current);

        private void Reconcile(Workspace ws)
        {
            for (int i = 0; i < _entries.Count; i++) _entries[i].Seen = false;
            var screens = ws.Screens;
            for (int i = 0; i < screens.Count; i++)
            {
                var s = screens[i];
                if (!s.Source.HasVideo) continue;
                var e = Find(s.Id);
                if (e == null)
                {
                    e = new Entry { Id = s.Id, Source = s.Source, Resolution = s.Resolution, Phase = Phase.Unlinked };
                    _entries.Add(e);
                    Report(e);
                }
                else if (!e.Resolution.Equals(s.Resolution) || !e.Source.Equals(s.Source))
                {
                    // RecreateScreen: o AcquireScreen com o tamanho novo faz o host trocar o monitor
                    CloseDecoder(e); // o slot (porta/stream_id) continua com a tela
                    e.Source = s.Source;
                    e.Resolution = s.Resolution;
                    e.StreamFailures = 0;
                    SetPhase(e, Phase.Unlinked);
                }
                e.Seen = true;
            }
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.Seen) continue;
                _connection.Send(new ReleaseScreenMessage(Key(e.Id), RemoveGraceMs));
                CloseDecoder(e);
                ReleaseSlot(e);
                _entries.RemoveAt(i);
            }
        }

        private void OnWelcomed(WelcomeMessage welcome)
        {
            _monitors = welcome.Monitors;
            var leases = welcome.Leases;
            for (int i = 0; i < leases.Count; i++)
                if (Find(new ScreenId((int)leases[i].ScreenKey)) == null)
                    _connection.Send(new ReleaseScreenMessage(leases[i].ScreenKey, RemoveGraceMs));
            for (int i = 0; i < _entries.Count; i++) // pede de novo: o host reata
            {
                _entries[i].StreamFailures = 0;
                SetPhase(_entries[i], Phase.Unlinked);
            }
            if (!_autoMirrorDone)
            {
                _autoMirrorDone = true;
                if (_store.State.Screens.Count == 0) CreatePrimaryMirror();
            }
        }

        private void CreatePrimaryMirror()
        {
            MonitorDescriptor primary = null;
            for (int i = 0; i < _monitors.Count; i++)
                if (_monitors[i].IsPrimary) primary = _monitors[i];
            if (primary == null && _monitors.Count > 0) primary = _monitors[0];
            if (primary == null) return;
            var resolution = ResolutionFit.FromCapture((int)primary.Width, (int)primary.Height);
            if (!resolution.IsValid) return;
            _bus.Send(new CreateScreen(resolution, null, ScreenSizing.InitialWidthMeters(resolution), 0f, primary.FriendlyName,
                ScreenSource.Mirror(primary.Id)));
        }

        private void OnLost()
        {
            // o decoder e a porta ficam: na reconexão o host volta a transmitir para o mesmo lugar
            for (int i = 0; i < _entries.Count; i++) SetPhase(_entries[i], Phase.Unlinked);
        }

        private void OnScreenMessage(ControlMessage message)
        {
            Entry e;
            switch (message)
            {
                case ScreenReadyMessage ready:
                    e = Find(new ScreenId((int)ready.ScreenKey));
                    if (e != null && e.Phase == Phase.Requested)
                    {
                        e.NotBefore = 0;
                        SetPhase(e, Phase.Ready);
                    }
                    break;
                case ScreenFailedMessage failed:
                    e = Find(new ScreenId((int)failed.ScreenKey));
                    if (e != null) Fail(e, failed.Reason);
                    break;
                case StreamStartedMessage started:
                    e = FindBySlot(started.StreamId);
                    if (e != null && e.Phase == Phase.Starting)
                    {
                        e.StreamFailures = 0;
                        e.NotBefore = _clock.NowSeconds;
                        SetPhase(e, Phase.Streaming);
                    }
                    break;
                case StreamStoppedMessage stopped:
                    e = FindBySlot(stopped.StreamId);
                    if (e != null && (e.Phase == Phase.Starting || e.Phase == Phase.Streaming || e.Phase == Phase.Live))
                    {
                        // Um host que nunca consegue iniciar o stream não pode ser chamado para sempre (cada tentativa
                        // segura o lock dele até ~3 s): espera dobrando e desiste após MaxStreamFailures seguidos.
                        e.StreamFailures++;
                        if (e.StreamFailures >= MaxStreamFailures)
                        {
                            Fail(e, StreamFailedPrefix + stopped.Reason); // só na transição: aloca uma vez
                            break;
                        }
                        e.NotBefore = _clock.NowSeconds + StreamRetrySeconds * (1 << (e.StreamFailures - 1));
                        SetPhase(e, Phase.Ready);
                    }
                    break;
            }
        }

        private ScreenLinkState Map(Entry e)
        {
            if (!_connection.IsLinked) return ScreenLinkState.Offline;
            switch (e.Phase)
            {
                case Phase.Unlinked:
                case Phase.Requested:
                    return ScreenLinkState.Creating;
                case Phase.Ready:
                case Phase.Starting:
                case Phase.Streaming:
                    return ScreenLinkState.Connecting;
                case Phase.Live:
                    return ScreenLinkState.Live;
                default:
                    return ScreenLinkState.Failed;
            }
        }

        private void SetPhase(Entry e, Phase phase)
        {
            e.Phase = phase;
            Report(e);
        }

        private void Fail(Entry e, string detail)
        {
            CloseDecoder(e);
            ReleaseSlot(e);
            e.Detail = detail ?? string.Empty;
            SetPhase(e, Phase.Failed);
        }

        private void Report(Entry e)
        {
            int state = (int)Map(e);
            if (state == e.Reported) return;
            e.Reported = state;
            LinkChanged?.Invoke(e.Id);
        }

        private void CloseDecoder(Entry e)
        {
            if (e.VideoOpen) _video.Close(e.Id);
            e.VideoOpen = false;
        }

        private void ReleaseSlot(Entry e)
        {
            if (e.Slot >= 0) _slots[e.Slot] = false;
            e.Slot = -1;
        }

        private int TakeSlot()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i]) continue;
                _slots[i] = true;
                return i;
            }
            return -1;
        }

        private Entry Find(ScreenId id)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Id == id) return _entries[i];
            return null;
        }

        private Entry FindBySlot(ushort streamId)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Slot == streamId) return _entries[i];
            return null;
        }

        private int Port(int slot) => _options.VideoPort + slot;

        private static uint Key(ScreenId id) => (uint)id.Value;

        private static ScreenKind KindOf(ScreenSource source) =>
            source.Kind == ScreenSourceKind.Mirror ? ScreenKind.Mirror : ScreenKind.Virtual;

        public void Dispose()
        {
            if (!_started) return;
            _started = false;
            _store.StateChanged -= OnStateChanged;
            _connection.Welcomed -= OnWelcomed;
            _connection.Lost -= OnLost;
            _connection.ScreenMessage -= OnScreenMessage;
            for (int i = 0; i < _entries.Count; i++)
            {
                CloseDecoder(_entries[i]);
                ReleaseSlot(_entries[i]);
            }
            _entries.Clear();
        }
    }
}
