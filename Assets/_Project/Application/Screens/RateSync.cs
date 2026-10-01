using System;
using System.Collections.Generic;
using ScreenPortal.App.Connection;
using ScreenPortal.App.Menu;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Screens
{
    /// <summary>
    /// Reconciliador de taxa (spec 2F): a cada quadro calcula o fps de cada tela com stream (modo → foco pelo olhar →
    /// orçamento) e manda <see cref="SetStreamRateMessage"/> só quando (fps, kbps) muda. Quando a tela sai de Live, o valor
    /// enviado é esquecido: o pipeline novo do host começa no padrão e recebe a taxa de novo. Sem alocação depois do
    /// aquecimento (listas e dicionários reaproveitados).
    /// </summary>
    public sealed class RateSync
    {
        private readonly WorkspaceStore _store;
        private readonly IScreenLinks _links;
        private readonly Func<bool> _isLinked;
        private readonly Action<ControlMessage> _send;
        private readonly IHeadPoseSource _head;
        private readonly IClock _clock;
        private readonly GazeFocus _gaze = new GazeFocus();
        private readonly List<RateRequest> _requests = new List<RateRequest>();
        private readonly Dictionary<ScreenId, (int Fps, int Kbps)> _sent = new Dictionary<ScreenId, (int, int)>();
        private readonly Dictionary<ScreenId, int> _granted = new Dictionary<ScreenId, int>();
        private readonly List<ScreenId> _gone = new List<ScreenId>();
        private int[] _grantedFps = new int[8];

        public RateSync(WorkspaceStore store, IScreenLinks links, Func<bool> isLinked, Action<ControlMessage> send,
            IHeadPoseSource head, IClock clock)
        {
            _store = store;
            _links = links;
            _isLinked = isLinked;
            _send = send;
            _head = head;
            _clock = clock;
        }

        public ScreenId Focus { get; private set; }
        /// <summary>O orçamento rebaixou um modo manual ("Essa configuração pode causar travadas").</summary>
        public bool Warning { get; private set; }

        public bool TryGetGranted(ScreenId screen, out int fps) => _granted.TryGetValue(screen, out fps);

        public void Tick()
        {
            var ws = _store.State;
            Focus = _gaze.Tick(_clock.NowSeconds, _head.HeadPose, ws);
            if (!_isLinked())
            {
                _sent.Clear();
                _granted.Clear();
                Warning = false;
                return;
            }

            _requests.Clear();
            var screens = ws.Screens;
            for (int i = 0; i < screens.Count; i++)
            {
                var screen = screens[i];
                if (!screen.Source.HasVideo || !_links.TryGetLink(screen.Id, out var state, out _) || state != ScreenLinkState.Live)
                    continue;
                _requests.Add(new RateRequest
                {
                    Screen = screen.Id,
                    Mode = screen.FrameRateMode,
                    Fps = FrameRates.RequestedFps(screen.FrameRateMode, screen.Id.Equals(Focus)),
                    Order = screen.Id.Value,
                });
            }
            if (_grantedFps.Length < _requests.Count) _grantedFps = new int[_requests.Count * 2];
            Warning = BandwidthBudget.Allocate(_requests, Focus, _grantedFps);

            _granted.Clear();
            for (int i = 0; i < _requests.Count; i++)
            {
                var request = _requests[i];
                int fps = _grantedFps[i];
                int kbps = FrameRates.Kbps(request.Mode, fps);
                _granted[request.Screen] = fps;
                if (_sent.TryGetValue(request.Screen, out var last) && last.Fps == fps && last.Kbps == kbps) continue;
                _sent[request.Screen] = (fps, kbps);
                _send(new SetStreamRateMessage((uint)request.Screen.Value, (byte)fps, (uint)kbps));
            }

            // fora de Live (ou removida): esquece o enviado
            _gone.Clear();
            foreach (var id in _sent.Keys)
                if (!_granted.ContainsKey(id)) _gone.Add(id);
            for (int i = 0; i < _gone.Count; i++) _sent.Remove(_gone[i]);
        }
    }
}
