using ScreenPortal.Host.Domain.Video;

namespace ScreenPortal.Host.Tests.Video;

public class H264NalTests
{
    private static readonly byte[] Sps = { 0, 0, 0, 1, 0x67, 0x4D, 0x00, 0x2A };
    private static readonly byte[] Pps = { 0, 0, 0, 1, 0x68, 0xEE, 0x3C, 0x80 };
    private static readonly byte[] Idr = { 0, 0, 1, 0x65, 0x88, 0x84, 0x00 };
    private static readonly byte[] PSlice = { 0, 0, 0, 1, 0x41, 0x9A, 0x00 };

    [Fact]
    public void Scan_DetectsIdrAndParameterSets()
    {
        var au = Sps.Concat(Pps).Concat(Idr).ToArray();
        var s = H264Nal.Scan(au);
        Assert.True(s.HasIdr);
        Assert.True(s.HasSps);
        Assert.True(s.HasPps);
        Assert.Equal(3, s.NalCount);
    }

    [Fact]
    public void Scan_DeltaFrameIsNotIdr()
    {
        var s = H264Nal.Scan(PSlice);
        Assert.False(s.HasIdr);
        Assert.False(s.HasSps);
    }

    [Fact]
    public void ExtractParameterSets_CopiesOnlySpsAndPps()
    {
        var au = Sps.Concat(Pps).Concat(Idr).ToArray();
        var dst = new byte[64];
        int n = H264Nal.ExtractParameterSets(au, dst);

        Assert.Equal(Sps.Concat(Pps).ToArray(), dst[..n]);
    }

    [Fact]
    public void ExtractParameterSets_ReturnsZeroWhenMissing() =>
        Assert.Equal(0, H264Nal.ExtractParameterSets(PSlice, new byte[64]));
}
