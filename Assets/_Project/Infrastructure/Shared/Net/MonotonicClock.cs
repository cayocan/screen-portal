using System.Diagnostics;
using ScreenPortal.App.Connection;

namespace ScreenPortal.Infrastructure.Net
{
    public sealed class MonotonicClock : IClock
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public double NowSeconds => _stopwatch.Elapsed.TotalSeconds;
    }
}
