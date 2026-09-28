using System;
using System.Collections.Generic;
using ScreenPortal.App.Connection;
using ScreenPortal.Domain.Screens;
using Resolution = ScreenPortal.Domain.Screens.Resolution;

namespace ScreenPortal.Infrastructure.Unity
{
    /// <summary>Liga o decoder nativo à Surface da compositor layer da tela (adapter de IVideoStreamSink).</summary>
    public sealed class NativeVideoSink : IVideoStreamSink
    {
        private readonly IScreenSurfaceProvider _surfaces;
        private readonly Dictionary<ushort, long> _handles = new Dictionary<ushort, long>();

        public NativeVideoSink(IScreenSurfaceProvider surfaces) => _surfaces = surfaces;

        public bool TryStart(ScreenId screen, ushort streamId, int udpPort, Resolution resolution)
        {
            if (_handles.ContainsKey(streamId)) return true;
            if (!NativeVideoJni.IsAvailable)
            {
                _handles[streamId] = 0; // Editor: sem decoder, mas o protocolo segue
                return true;
            }
            if (!_surfaces.TryGetSurface(screen, out IntPtr surface) || surface == IntPtr.Zero) return false;
            long handle = NativeVideoJni.Start(streamId, udpPort, surface, resolution.Width, resolution.Height);
            if (handle == 0) return false;
            _handles[streamId] = handle;
            return true;
        }

        public void Stop(ushort streamId)
        {
            if (!_handles.TryGetValue(streamId, out long handle)) return;
            if (NativeVideoJni.IsAvailable) NativeVideoJni.Stop(handle);
            _handles.Remove(streamId);
        }
    }
}
