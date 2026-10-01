using System;
using ScreenPortal.App.Connection;
using ScreenPortal.Domain;

namespace ScreenPortal.App.Session
{
    /// <summary>Onde a última sessão fica (Infrastructure: arquivo no armazenamento do app).</summary>
    public interface ISessionStore
    {
        /// <returns>null se não houver sessão salva.</returns>
        string Load();
        void Save(string text);
    }

    public sealed class NullSessionStore : ISessionStore
    {
        public string Load() => null;
        public void Save(string text) { }
    }

    /// <summary>
    /// Salva a workspace depois de cada mudança, no máximo a cada <see cref="IntervalSeconds"/> (um arraste vira um
    /// Command só ao soltar, então isso é raro). Assim, se o app reiniciar (reinstalação, crash, sistema fechou), a
    /// sessão volta e o host religa os monitores que ainda aluga (5 min).
    /// </summary>
    public sealed class SessionAutosave : IDisposable
    {
        public const double IntervalSeconds = 0.5;

        private readonly WorkspaceStore _store;
        private readonly ISessionStore _session;
        private readonly IClock _clock;
        private bool _dirty;
        private double _lastSave = double.NegativeInfinity;

        public SessionAutosave(WorkspaceStore store, ISessionStore session, IClock clock)
        {
            _store = store;
            _session = session;
            _clock = clock;
            _store.StateChanged += OnChanged;
        }

        private void OnChanged(Workspace before, Workspace after) => _dirty = true;

        public void Tick()
        {
            if (!_dirty || _clock.NowSeconds - _lastSave < IntervalSeconds) return;
            Flush();
        }

        /// <summary>Salva já, se houver mudança (app indo para segundo plano).</summary>
        public void Flush()
        {
            if (!_dirty) return;
            _dirty = false;
            _lastSave = _clock.NowSeconds;
            _session.Save(SessionCodec.Encode(_store.State));
        }

        public void Dispose() => _store.StateChanged -= OnChanged;
    }
}
