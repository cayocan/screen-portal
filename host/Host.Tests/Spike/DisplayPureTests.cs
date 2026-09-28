using ScreenPortal.Host.Domain.Display;

namespace ScreenPortal.Host.Tests.Spike;

public class DisplayPureTests
{
    [Fact]
    public void Map_KeepsRelativeCenterAndSize()
    {
        var from = new RectI(2560, 0, 1920, 1080);
        var to = new RectI(4480, 0, 2560, 1440);
        var window = new RectI(2560 + 480, 270, 960, 540); // centrada em (50%, 50%)

        var mapped = WindowRelocation.Map(window, from, to);

        Assert.Equal(960, mapped.Width);
        Assert.Equal(540, mapped.Height);
        Assert.Equal(4480 + 1280 - 480, mapped.X);
        Assert.Equal(720 - 270, mapped.Y);
    }

    [Fact]
    public void Map_ShrinksAndClampsWhenDestinationIsSmaller()
    {
        var from = new RectI(0, 0, 3840, 2160);
        var to = new RectI(3840, 0, 1920, 1080);
        var window = new RectI(0, 0, 3000, 2000);

        var mapped = WindowRelocation.Map(window, from, to);

        Assert.True(mapped.Width <= 1920 && mapped.Height <= 1080);
        Assert.True(mapped.X >= 3840 && mapped.X + mapped.Width <= 3840 + 1920);
        Assert.True(mapped.Y >= 0 && mapped.Y + mapped.Height <= 1080);
    }

    private const string SampleXml = """
        <?xml version='1.0' encoding='utf-8'?>
        <vdd_settings>
            <monitors><count>2</count></monitors>
            <resolutions>
                <resolution><width>1920</width><height>1080</height><refresh_rate>60</refresh_rate></resolution>
            </resolutions>
        </vdd_settings>
        """;

    [Fact]
    public void EnsureResolution_AddsOnce()
    {
        string once = VddSettingsXml.EnsureResolution(SampleXml, 1440, 2560, 60);
        string twice = VddSettingsXml.EnsureResolution(once, 1440, 2560, 60);

        Assert.Contains("<width>1440</width>", once);
        Assert.Contains("<height>2560</height>", once);
        Assert.Equal(once, twice);
        Assert.Equal(2, VddSettingsXml.ReadMonitorCount(once));
    }

    [Fact]
    public void EnsureResolution_KeepsExistingEntry() =>
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(
            VddSettingsXml.EnsureResolution(SampleXml, 1920, 1080, 60), "<resolution>").Count);
}
