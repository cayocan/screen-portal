using System;
using System.Globalization;

namespace ScreenPortal.Infrastructure.Video
{
    /// <summary>
    /// Linha CSV do decoder nativo (<c>NativeVideo.takeStats</c>, headset-native stream_session.cpp):
    /// <c>SPSTAT,streamId,recvFps,decodedFps,Mbps,dropNet,dropDec,avgMs,minMs,maxMs,rttMs</c>. Lida ~1×/s, fora do
    /// caminho de vídeo.
    /// </summary>
    public static class SpStatLine
    {
        public static bool TryParseDecodedFps(string line, out float decodedFps)
        {
            decodedFps = 0f;
            if (string.IsNullOrEmpty(line) || !line.StartsWith("SPSTAT,", StringComparison.Ordinal)) return false;
            var parts = line.Split(',');
            return parts.Length > 3 && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out decodedFps);
        }
    }
}
