using System;
using System.Collections.Generic;
using ScreenPortal.Domain;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.App.Screens
{
    /// <summary>
    /// Observer do <see cref="WorkspaceStore"/>: cria, atualiza e descarta views para que elas espelhem
    /// o estado. Como <see cref="VirtualScreen"/> é imutável, "mudou" = outra instância (comparação por referência).
    /// Superfície nova (outra instância) reaplica todas as telas: a pose é derivada dela.
    /// </summary>
    public sealed class ScreenViewSynchronizer : IDisposable
    {
        private readonly WorkspaceStore _store;
        private readonly IScreenViewFactory _factory;
        private readonly Dictionary<ScreenId, Entry> _entries = new Dictionary<ScreenId, Entry>();
        private readonly List<ScreenId> _stale = new List<ScreenId>();
        private bool _started;

        private struct Entry
        {
            public IScreenView View;
            public VirtualScreen Applied;
            public LayoutSurface Surface;
            public bool Seen;
        }

        public int ViewCount => _entries.Count;

        public ScreenViewSynchronizer(WorkspaceStore store, IScreenViewFactory factory)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public void Start()
        {
            if (_started) return;
            _started = true;
            _store.StateChanged += OnStateChanged;
            Sync(_store.State);
        }

        private void OnStateChanged(Workspace previous, Workspace current) => Sync(current);

        private void Sync(Workspace workspace)
        {
            var screens = workspace.Screens;
            var surface = workspace.Surface;
            for (int i = 0; i < screens.Count; i++)
            {
                var screen = screens[i];
                if (_entries.TryGetValue(screen.Id, out var entry))
                {
                    if (!ReferenceEquals(entry.Applied, screen) || !ReferenceEquals(entry.Surface, surface))
                    {
                        entry.View.Apply(screen, surface.ToPose(screen.Placement), CurvedGeometry.Of(screen, surface).RadiusM);
                        entry.Applied = screen;
                        entry.Surface = surface;
                    }
                    entry.Seen = true;
                    _entries[screen.Id] = entry;
                }
                else
                {
                    var view = _factory.Create(screen);
                    view.Apply(screen, surface.ToPose(screen.Placement), CurvedGeometry.Of(screen, surface).RadiusM);
                    _entries[screen.Id] = new Entry { View = view, Applied = screen, Surface = surface, Seen = true };
                }
            }

            _stale.Clear();
            foreach (var pair in _entries)
                if (!pair.Value.Seen) _stale.Add(pair.Key);
            for (int i = 0; i < _stale.Count; i++)
            {
                _entries[_stale[i]].View.Dispose();
                _entries.Remove(_stale[i]);
            }

            _stale.Clear();
            foreach (var key in _entries.Keys) _stale.Add(key);
            for (int i = 0; i < _stale.Count; i++)
            {
                var entry = _entries[_stale[i]];
                entry.Seen = false;
                _entries[_stale[i]] = entry;
            }
        }

        public void Dispose()
        {
            if (_started) _store.StateChanged -= OnStateChanged;
            _started = false;
            foreach (var pair in _entries) pair.Value.View.Dispose();
            _entries.Clear();
        }
    }
}
