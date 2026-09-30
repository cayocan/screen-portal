using NUnit.Framework;
using ScreenPortal.Infrastructure.Video;

namespace ScreenPortal.Tests
{
    public class SpStatLineTests
    {
        [Test]
        public void ParsesDecodedFps_FromTheNativeLine()
        {
            // headset-native stream_session.cpp: SPSTAT,id,recvFps,decFps,Mbps,dropNet,dropDec,avg,min,max,rtt
            Assert.That(SpStatLine.TryParseDecodedFps("SPSTAT,0,60.0,59.5,18.20,0,0,16.1,12.0,22.3,3.2", out float fps), Is.True);
            Assert.That(fps, Is.EqualTo(59.5f).Within(1e-4f));
        }

        [Test]
        public void RejectsNullGarbageAndShortLines()
        {
            Assert.That(SpStatLine.TryParseDecodedFps(null, out _), Is.False);
            Assert.That(SpStatLine.TryParseDecodedFps("hello", out _), Is.False);
            Assert.That(SpStatLine.TryParseDecodedFps("SPSTAT,0,60.0", out _), Is.False);
        }
    }
}
