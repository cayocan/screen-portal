using ScreenPortal.App.Audio;
using UnityEngine;
using UnityEngine.Android;

namespace ScreenPortal.Infrastructure.Unity
{
    /// <summary>
    /// Áudio no Quest (spec 2G §4): liga a sessão nativa do .aar (com.screenportal.video.NativeAudio); o áudio não passa
    /// pelo C#. Sem a permissão de microfone, liga só o som do PC e pede a permissão; ao ser concedida, religa com o
    /// microfone. Chamadas raras (conexão e botões do menu), fora do caminho quente.
    /// </summary>
    public sealed class NativeAudioLink : IAudioLink
    {
        private const string ClassName = "com.screenportal.video.NativeAudio";

        private long _handle;
        private string _host;
        private int _rxPort, _txPort;
        private bool _playback, _mic;

        public bool Start(string host, int rxPort, int txPort, bool playback, bool mic)
        {
            Stop();
            _host = host;
            _rxPort = rxPort;
            _txPort = txPort;
            _playback = playback;
            _mic = mic;
            bool micAllowed = Permission.HasUserAuthorizedPermission(Permission.Microphone);
            if (mic && !micAllowed) RequestMic();
            using (var native = new AndroidJavaClass(ClassName))
                _handle = native.CallStatic<long>("start", host, rxPort, txPort, playback, mic && micAllowed);
            Debug.Log($"[ScreenPortal] áudio: {(_handle != 0 ? "ligado" : "falhou")} (som {playback}, microfone {mic && micAllowed})");
            return _handle != 0;
        }

        public void Stop()
        {
            if (_handle == 0) return;
            using (var native = new AndroidJavaClass(ClassName)) native.CallStatic("stop", _handle);
            _handle = 0;
        }

        private void RequestMic()
        {
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ =>
            {
                if (_handle != 0 && _mic) Start(_host, _rxPort, _txPort, _playback, true);
            };
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
        }
    }
}
