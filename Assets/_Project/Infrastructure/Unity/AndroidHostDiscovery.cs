using System;
using ScreenPortal.App.Connection;
using UnityEngine;

namespace ScreenPortal.Infrastructure.Unity
{
    /// <summary>mDNS via NsdManager (com.screenportal.video.HostDiscovery no .aar). Consulta a cada 250 ms.</summary>
    public sealed class AndroidHostDiscovery : IHostDiscovery
    {
        private const float PollIntervalSeconds = 0.25f;
        private AndroidJavaClass _discovery;
        private float _nextPoll;
        private HostEndpoint _last;
        private bool _hasLast;

        public void Start()
        {
            if (_discovery != null) return;
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                _discovery = new AndroidJavaClass("com.screenportal.video.HostDiscovery");
                _discovery.CallStatic("start", activity);
            }
        }

        public void Stop()
        {
            if (_discovery == null) return;
            _discovery.CallStatic("stop");
            _discovery.Dispose();
            _discovery = null;
            _hasLast = false;
        }

        public bool TryGetHost(out HostEndpoint host)
        {
            if (_discovery != null && Time.realtimeSinceStartup >= _nextPoll)
            {
                _nextPoll = Time.realtimeSinceStartup + PollIntervalSeconds;
                string text = _discovery.CallStatic<string>("poll");
                _hasLast = HostEndpoint.TryParse(text, out _last);
            }
            host = _last;
            return _hasLast;
        }
    }
}
