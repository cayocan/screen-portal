using System;
using System.Collections.Generic;
using ScreenPortal.App.Connection;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Infrastructure.Video;
using UnityEngine;
using Resolution = ScreenPortal.Domain.Screens.Resolution;

namespace ScreenPortal.Infrastructure.Unity
{
    /// <summary>
    /// Um decoder nativo por tela, desenhando direto na Surface da compositor layer (o quadro nunca passa pelo C#).
    /// No Editor/Simulator não há decoder: TryOpen aceita e HasFirstFrame fica false (a tela mostra "Conectando vídeo…").
    /// </summary>
    public sealed class NativeScreenVideo : IScreenVideo
    {
        private sealed class Session
        {
            public long Handle;
            public bool FirstFrame;
        }

        private readonly IScreenSurfaceProvider _surfaces;
        private readonly Dictionary<ScreenId, Session> _sessions = new Dictionary<ScreenId, Session>();

        public NativeScreenVideo(IScreenSurfaceProvider surfaces) => _surfaces = surfaces;

        public bool TryOpen(ScreenId screen, ushort streamId, int udpPort, Resolution resolution)
        {
            if (_sessions.ContainsKey(screen)) return true;
            if (!NativeVideoJni.IsAvailable)
            {
                _sessions[screen] = new Session();
                return true;
            }
            if (!_surfaces.TryGetSurface(screen, out IntPtr surface) || surface == IntPtr.Zero) return false;
            long handle = NativeVideoJni.Start(streamId, udpPort, surface, resolution.Width, resolution.Height);
            if (handle == 0) return false;
            _sessions[screen] = new Session { Handle = handle };
            return true;
        }

        public void Close(ScreenId screen)
        {
            if (!_sessions.TryGetValue(screen, out var session)) return;
            if (session.Handle != 0) NativeVideoJni.Stop(session.Handle);
            _sessions.Remove(screen);
        }

        public bool HasFirstFrame(ScreenId screen)
        {
            if (!_sessions.TryGetValue(screen, out var session) || session.Handle == 0) return false;
            if (session.FirstFrame) return true;
            string line = NativeVideoJni.TakeStats(session.Handle); // ~1×/s: o HostSync controla a cadência
            if (line != null) Debug.Log(line);
            session.FirstFrame = SpStatLine.TryParseDecodedFps(line, out float fps) && fps > 0f;
            return session.FirstFrame;
        }
    }
}
