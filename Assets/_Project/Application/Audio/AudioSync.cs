using System;
using ScreenPortal.App.Connection;

namespace ScreenPortal.App.Audio
{
    /// <summary>Sessão de áudio nativa (Infrastructure: .aar no Quest; no-op no Editor). O áudio não passa pelo C#.</summary>
    public interface IAudioLink
    {
        bool Start(string host, int rxPort, int txPort, bool playback, bool mic);
        void Stop();
    }

    /// <summary>Editor/Simulator: sem áudio nativo.</summary>
    public sealed class NullAudioLink : IAudioLink
    {
        public bool Start(string host, int rxPort, int txPort, bool playback, bool mic) => false;
        public void Stop() { }
    }

    /// <summary>Botões do menu: som do PC e microfone (spec 2G §1). Começam ligados.</summary>
    public sealed class AudioSettings
    {
        public bool Playback { get; private set; } = true;
        public bool Mic { get; private set; } = true;
        public event Action Changed;

        public void TogglePlayback()
        {
            Playback = !Playback;
            Changed?.Invoke();
        }

        public void ToggleMic()
        {
            Mic = !Mic;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Áudio com o host (spec 2G §4): no Welcome e a cada mudança dos botões manda <see cref="StartAudioMessage"/>; com o
    /// <see cref="AudioStartedMessage"/> (re)liga a sessão nativa com o que o host oferece; na queda, desliga.
    /// </summary>
    public sealed class AudioSync : IDisposable
    {
        public const int RxPort = 9300;

        private readonly ConnectionStateMachine _connection;
        private readonly IAudioLink _link;
        private readonly AudioSettings _settings;
        private bool _hostSpeakers = true;
        private bool _hostMic = true;
        private bool _running;

        public AudioSync(ConnectionStateMachine connection, IAudioLink link, AudioSettings settings)
        {
            _connection = connection;
            _link = link;
            _settings = settings;
            _connection.Welcomed += OnWelcomed;
            _connection.Lost += OnLost;
            _connection.ScreenMessage += OnMessage;
            _settings.Changed += OnSettingsChanged;
            Status = BuildStatus();
        }

        /// <summary>Linha de status da página "Áudio" do menu (só muda em eventos, não por quadro).</summary>
        public string Status { get; private set; }

        public AudioSettings Settings => _settings;

        private void OnWelcomed(WelcomeMessage _) => Ask();

        private void OnSettingsChanged()
        {
            Status = BuildStatus();
            if (_connection.IsLinked) Ask();
        }

        private void Ask() => _connection.Send(new StartAudioMessage((ushort)RxPort, _settings.Playback, _settings.Mic));

        private void OnMessage(ControlMessage message)
        {
            if (!(message is AudioStartedMessage started)) return;
            _hostSpeakers = started.Speakers;
            _hostMic = started.Mic;
            Status = BuildStatus();
            _link.Stop();
            _running = false;
            bool mic = _settings.Mic && started.Mic;
            if (!_settings.Playback && !mic) return;
            _running = _link.Start(_connection.HostAddress, RxPort, started.MicPort, _settings.Playback, mic);
        }

        private void OnLost()
        {
            if (!_running) return;
            _link.Stop();
            _running = false;
        }

        private string BuildStatus()
        {
            string sound = !_settings.Playback ? "Som do PC: desligado"
                : _hostSpeakers ? "Som do PC: ligado" : "Som também nas caixas do PC";
            string mic = !_settings.Mic ? "Microfone: mudo"
                : _hostMic ? "Microfone: ligado" : "Microfone indisponível (Steam)";
            return sound + " · " + mic;
        }

        public void Dispose()
        {
            _connection.Welcomed -= OnWelcomed;
            _connection.Lost -= OnLost;
            _connection.ScreenMessage -= OnMessage;
            _settings.Changed -= OnSettingsChanged;
            _link.Stop();
        }
    }
}
