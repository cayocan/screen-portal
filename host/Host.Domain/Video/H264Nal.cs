namespace ScreenPortal.Host.Domain.Video;

/// <summary>Leitura mínima de access units H.264 Annex-B, sem alocação.</summary>
public static class H264Nal
{
    public const int NalIdr = 5;
    public const int NalSps = 7;
    public const int NalPps = 8;

    public readonly record struct Summary(bool HasIdr, bool HasSps, bool HasPps, int NalCount);

    public static Summary Scan(ReadOnlySpan<byte> au)
    {
        bool idr = false, sps = false, pps = false;
        int count = 0;
        int i = 0;
        while ((i = NextNalStart(au, i)) >= 0)
        {
            if (i >= au.Length) break;
            int type = au[i] & 0x1F;
            count++;
            idr |= type == NalIdr;
            sps |= type == NalSps;
            pps |= type == NalPps;
            i++;
        }
        return new Summary(idr, sps, pps, count);
    }

    /// <summary>
    /// Copia os NALs SPS e PPS (com start codes) de um access unit para <paramref name="dst"/>.
    /// </summary>
    /// <returns>Bytes escritos; 0 se não houver SPS e PPS.</returns>
    public static int ExtractParameterSets(ReadOnlySpan<byte> au, Span<byte> dst)
    {
        int written = 0;
        int i = 0;
        while ((i = NextNalStart(au, i)) >= 0 && i < au.Length)
        {
            int type = au[i] & 0x1F;
            int end = NextStartCodePosition(au, i);
            if (type is NalSps or NalPps)
            {
                int len = end - i;
                if (written + 4 + len > dst.Length) return 0;
                dst[written++] = 0; dst[written++] = 0; dst[written++] = 0; dst[written++] = 1;
                au.Slice(i, len).CopyTo(dst[written..]);
                written += len;
            }
            i = end;
        }
        var check = Scan(dst[..written]);
        return check.HasSps && check.HasPps ? written : 0;
    }

    /// <summary>Índice do primeiro byte do próximo NAL (logo após o start code), ou -1.</summary>
    private static int NextNalStart(ReadOnlySpan<byte> au, int from)
    {
        for (int i = from; i + 2 < au.Length; i++)
        {
            if (au[i] == 0 && au[i + 1] == 0 && au[i + 2] == 1) return i + 3;
        }
        return -1;
    }

    /// <summary>Posição do início do próximo start code (3 ou 4 bytes) a partir de <paramref name="from"/>, ou o fim.</summary>
    private static int NextStartCodePosition(ReadOnlySpan<byte> au, int from)
    {
        for (int i = from; i + 2 < au.Length; i++)
        {
            if (au[i] == 0 && au[i + 1] == 0 && au[i + 2] == 1)
                return i > from && au[i - 1] == 0 ? i - 1 : i;
        }
        return au.Length;
    }
}
