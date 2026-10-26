using ViolinPassthrough.State;

namespace ViolinPassthrough.Tests.State;

public class PassthroughStateTests
{
    [Theory]
    [InlineData(ViolinMode.Away, false, ViolinState.Away)]
    [InlineData(ViolinMode.Away, true, ViolinState.Away)]
    [InlineData(ViolinMode.Hold, false, ViolinState.Held)]
    [InlineData(ViolinMode.Hold, true, ViolinState.Playing)]
    [InlineData(ViolinMode.Play, false, ViolinState.Playing)]
    [InlineData(ViolinMode.Play, true, ViolinState.Playing)]
    public void ViolinState_FollowsTheModeAndThePosture(ViolinMode mode, bool inPosture, ViolinState expected)
    {
        var state = new PassthroughState(false, "fingerboard", false);
        state.SetViolinMode(mode);
        state.SetInPosture(inPosture);

        Assert.Equal(expected, state.ViolinState);
    }

    [Fact]
    public void ManualPlay_CountsAsPlayingForTheAutoWindow()
    {
        var state = new PassthroughState(true, "fingerboard", false, auto: true);
        Assert.False(state.IsVisible);

        state.SetViolinMode(ViolinMode.Play);

        Assert.True(state.IsPlaying);
        Assert.True(state.IsVisible);
    }

    [Theory]
    [InlineData(false, false, ViolinMode.Away, false)]
    [InlineData(true, false, ViolinMode.Away, false)]
    [InlineData(true, true, ViolinMode.Away, true)]
    [InlineData(false, false, ViolinMode.Hold, true)]
    [InlineData(false, false, ViolinMode.Play, false)]
    public void PostureDetection_IsNeededForTheAutoWindowOrWhileHolding(bool enabled, bool auto, ViolinMode mode, bool expected)
    {
        var state = new PassthroughState(enabled, "fingerboard", false, auto);
        state.SetViolinMode(mode);

        Assert.Equal(expected, state.NeedsPostureDetection);
    }

    [Fact]
    public void Setters_BumpTheVersionOnlyOnChange()
    {
        var state = new PassthroughState(false, "fingerboard", false);
        var version = state.Version;

        Assert.False(state.SetAutoMethod(AutoMethod.LeftHand));
        Assert.True(state.SetAutoMethod(AutoMethod.Head));
        Assert.True(state.SetViolinMode(ViolinMode.Hold));
        Assert.False(state.SetViolinMode(ViolinMode.Hold));

        Assert.Equal(version + 2, state.Version);
    }

    [Theory]
    [InlineData(true, false, AutoMethod.LeftHand)]
    [InlineData(true, true, AutoMethod.LeftHandAndHead)]
    [InlineData(false, true, AutoMethod.Head)]
    public void AutoMethod_FromConfigFlags(bool useLeftHand, bool useHeadTilt, AutoMethod expected)
    {
        Assert.Equal(expected, AutoMethods.FromFlags(useLeftHand, useHeadTilt));
        Assert.Equal(useLeftHand, expected.UsesLeftHand());
        Assert.Equal(useHeadTilt, expected.UsesHead());
    }
}
