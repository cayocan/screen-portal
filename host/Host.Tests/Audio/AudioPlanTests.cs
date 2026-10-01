using ScreenPortal.Host.Domain.Audio;

namespace ScreenPortal.Host.Tests.Audio;

public class AudioPlanTests
{
    [Fact]
    public void WithSteamDrivers_SwitchesTheOutput_CapturesIt_AndOffersTheMic() =>
        Assert.Equal(new AudioPlan(true, true, true, true), AudioPlan.For(true, true, steamSpeakers: true, steamMic: true));

    [Fact]
    public void WithoutSteamSpeakers_CapturesTheDefault_WithoutSwitching() =>
        Assert.Equal(new AudioPlan(true, false, false, false), AudioPlan.For(true, true, steamSpeakers: false, steamMic: false));

    [Fact]
    public void PlaybackOff_DoesNotStreamNorSwitch_MicStillWorks() =>
        Assert.Equal(new AudioPlan(false, false, false, true), AudioPlan.For(false, true, steamSpeakers: true, steamMic: true));

    [Fact]
    public void MicMuted_NoMic() =>
        Assert.False(AudioPlan.For(true, false, true, true).Mic);
}
