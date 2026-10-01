using System;
using System.Collections.Generic;
using ScreenPortal.Domain.Layout;
using ScreenPortal.Domain.Screens;

namespace ScreenPortal.Domain
{
    /// <summary>
    /// Estado completo da workspace. Imutável: o <c>WorkspaceStore</c> guarda snapshots para undo/redo
    /// e perfis de layout (Memento, brief §8.2). Ids nunca são reutilizados.
    /// </summary>
    public sealed class Workspace
    {
        public static readonly Workspace Empty = new Workspace(Array.Empty<VirtualScreen>(), ScreenId.None, 1, LayoutSurface.Default);

        private readonly VirtualScreen[] _screens;

        public IReadOnlyList<VirtualScreen> Screens => _screens;
        public ScreenId FocusedScreenId { get; }
        public int NextScreenId { get; }
        /// <summary>Superfície de layout (ADR 0005): parte do snapshot do undo.</summary>
        public LayoutSurface Surface { get; }

        private Workspace(VirtualScreen[] screens, ScreenId focused, int nextScreenId, LayoutSurface surface)
        {
            _screens = screens;
            FocusedScreenId = focused;
            NextScreenId = nextScreenId;
            Surface = surface;
        }

        /// <summary>
        /// Workspace salva (última sessão): ids repetidos ficam de fora, o próximo id passa do maior existente (um id novo
        /// não pode colidir com um monitor que o host ainda aluga) e um foco que não existe vira nenhum.
        /// </summary>
        public static Workspace Restore(IReadOnlyList<VirtualScreen> screens, ScreenId focused, int nextScreenId, LayoutSurface surface)
        {
            var kept = new List<VirtualScreen>(screens.Count);
            int maxId = 0;
            foreach (var screen in screens)
            {
                if (screen == null || kept.Exists(s => s.Id.Equals(screen.Id))) continue;
                kept.Add(screen);
                maxId = Math.Max(maxId, screen.Id.Value);
            }
            var restored = kept.ToArray();
            bool focusExists = Array.Exists(restored, s => s.Id.Equals(focused));
            return new Workspace(restored, focusExists ? focused : ScreenId.None, Math.Max(nextScreenId, maxId + 1),
                surface ?? LayoutSurface.Default);
        }

        public ScreenId AllocateId() => new ScreenId(NextScreenId);

        public bool TryGetScreen(ScreenId id, out VirtualScreen screen)
        {
            int index = IndexOf(id);
            screen = index >= 0 ? _screens[index] : null;
            return index >= 0;
        }

        public bool Contains(ScreenId id) => IndexOf(id) >= 0;

        public Workspace WithScreenAdded(VirtualScreen screen)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            if (Contains(screen.Id)) throw new InvalidOperationException(screen.Id + " já existe.");
            var screens = new VirtualScreen[_screens.Length + 1];
            Array.Copy(_screens, screens, _screens.Length);
            screens[_screens.Length] = screen;
            return new Workspace(screens, FocusedScreenId, Math.Max(NextScreenId, screen.Id.Value + 1), Surface);
        }

        public Workspace WithScreenReplaced(VirtualScreen screen)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            int index = IndexOf(screen.Id);
            if (index < 0) throw new InvalidOperationException(screen.Id + " não existe.");
            var screens = (VirtualScreen[])_screens.Clone();
            screens[index] = screen;
            return new Workspace(screens, FocusedScreenId, NextScreenId, Surface);
        }

        public Workspace WithScreenRemoved(ScreenId id)
        {
            int index = IndexOf(id);
            if (index < 0) throw new InvalidOperationException(id + " não existe.");
            var screens = new VirtualScreen[_screens.Length - 1];
            Array.Copy(_screens, 0, screens, 0, index);
            Array.Copy(_screens, index + 1, screens, index, _screens.Length - index - 1);
            var focus = FocusedScreenId == id ? ScreenId.None : FocusedScreenId;
            return new Workspace(screens, focus, NextScreenId, Surface);
        }

        public Workspace WithFocus(ScreenId id)
        {
            if (!id.IsNone && !Contains(id)) throw new InvalidOperationException(id + " não existe.");
            return new Workspace(_screens, id, NextScreenId, Surface);
        }

        public Workspace WithSurface(LayoutSurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            return new Workspace(_screens, FocusedScreenId, NextScreenId, surface);
        }

        private int IndexOf(ScreenId id)
        {
            for (int i = 0; i < _screens.Length; i++)
                if (_screens[i].Id == id) return i;
            return -1;
        }
    }
}
