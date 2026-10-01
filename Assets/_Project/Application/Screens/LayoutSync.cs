using System;
using System.Collections.Generic;
using ScreenPortal.App.Connection;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Layout;

namespace ScreenPortal.App.Screens
{
    /// <summary>"Arranjo automático" (spec 3A §1, princípio Liberdade): começa ligado.</summary>
    public sealed class LayoutSyncSettings
    {
        public bool Enabled { get; private set; } = true;
        public event Action Changed;

        public void Toggle()
        {
            Enabled = !Enabled;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Manda ao host a disposição das telas com vídeo na superfície (spec 3A §3) para o arranjo do Windows seguir o Quest:
    /// <see cref="DebounceSeconds"/> depois da última mudança, só se a disposição mudou, e de novo a cada reconexão. Telas
    /// soltas ficam de fora. Desligado, não manda nada (o host deixa o arranjo como está).
    /// </summary>
    public sealed class LayoutSync : IDisposable
    {
        public const double DebounceSeconds = 0.5;

        private readonly WorkspaceStore _store;
        private readonly Func<bool> _isLinked;
        private readonly Action<ControlMessage> _send;
        private readonly IClock _clock;
        private readonly LayoutSyncSettings _settings;
        private readonly List<ScreenAnglesInfo> _scratch = new List<ScreenAnglesInfo>();
        private ScreenAnglesInfo[] _sent;
        private bool _dirty = true;
        private double _changedAt = double.NegativeInfinity;
        private bool _wasLinked;

        public LayoutSync(WorkspaceStore store, Func<bool> isLinked, Action<ControlMessage> send, IClock clock, LayoutSyncSettings settings)
        {
            _store = store;
            _isLinked = isLinked;
            _send = send;
            _clock = clock;
            _settings = settings;
            _store.StateChanged += OnChanged;
            _settings.Changed += OnSettingsChanged;
        }

        private void OnChanged(Workspace before, Workspace after) => MarkDirty();

        private void OnSettingsChanged()
        {
            _sent = null; // religado: manda o atual mesmo que igual ao último
            MarkDirty();
        }

        private void MarkDirty()
        {
            _dirty = true;
            _changedAt = _clock.NowSeconds;
        }

        public void Tick()
        {
            bool linked = _isLinked();
            if (linked && !_wasLinked)
            {
                _sent = null; // host novo (ou reconexão): manda de novo
                MarkDirty();
            }
            _wasLinked = linked;
            if (!linked || !_settings.Enabled || !_dirty || _clock.NowSeconds - _changedAt < DebounceSeconds) return;
            _dirty = false;

            var ws = _store.State;
            _scratch.Clear();
            foreach (var screen in ws.Screens)
            {
                if (!screen.Source.HasVideo || !ScreenAngles.TryOf(screen, ws.Surface, out var rect)) continue;
                _scratch.Add(new ScreenAnglesInfo((uint)screen.Id.Value, rect.AzimuthDeg, rect.ElevationDeg, rect.HalfWidthDeg, rect.HalfHeightDeg));
            }
            if (_scratch.Count == 0 || Same(_sent, _scratch)) return;
            _sent = _scratch.ToArray();
            _send(new LayoutChangedMessage(_sent));
        }

        private static bool Same(ScreenAnglesInfo[] sent, List<ScreenAnglesInfo> now)
        {
            if (sent == null || sent.Length != now.Count) return false;
            for (int i = 0; i < sent.Length; i++)
            {
                var a = sent[i];
                var b = now[i];
                if (a.ScreenKey != b.ScreenKey || a.AzimuthDeg != b.AzimuthDeg || a.ElevationDeg != b.ElevationDeg
                    || a.HalfWidthDeg != b.HalfWidthDeg || a.HalfHeightDeg != b.HalfHeightDeg) return false;
            }
            return true;
        }

        public void Dispose()
        {
            _store.StateChanged -= OnChanged;
            _settings.Changed -= OnSettingsChanged;
        }
    }
}
