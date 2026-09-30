using System;
using ScreenPortal.App.Menu;

namespace ScreenPortal.Infrastructure.Unity
{
    /// <summary>Recentrar do Quest (OVRDisplay.RecenteredPose) → o app recentra a superfície (spec superfície §6).</summary>
    public sealed class OvrRecenterEvents : IRecenterEvents, IDisposable
    {
        private Action _handlers;
        private bool _hooked;

        public event Action Recentered
        {
            add
            {
                _handlers += value;
                Hook();
            }
            remove => _handlers -= value;
        }

        private void Hook()
        {
            if (_hooked || OVRManager.display == null) return;
            OVRManager.display.RecenteredPose += Raise;
            _hooked = true;
        }

        private void Raise() => _handlers?.Invoke();

        public void Dispose()
        {
            if (_hooked && OVRManager.display != null) OVRManager.display.RecenteredPose -= Raise;
            _hooked = false;
        }
    }
}
