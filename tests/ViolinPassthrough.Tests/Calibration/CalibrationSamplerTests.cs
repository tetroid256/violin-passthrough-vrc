using ViolinPassthrough.Anchor;
using ViolinPassthrough.Calibration;

namespace ViolinPassthrough.Tests.Calibration;

public class CalibrationSamplerTests
{
    private static readonly Pose3x4 Hmd = Tracking.StandingHmd(yawDeg: 25, rollDeg: 10);
    private static readonly Vec3 LeftLocal = new(-0.35, -0.10, -0.35);
    private static readonly Vec3 RightLocal = new(0.0, -0.25, -0.30);

    [Fact]
    public void AveragesThePositionsInHmdCoordinates()
    {
        var sampler = new CalibrationSampler();
        var wobble = new Vec3(0.01, -0.02, 0.005);
        sampler.Add(Tracking.Make(Hmd, LeftLocal + wobble, RightLocal), HandDevices.Default, null);
        sampler.Add(Tracking.Make(Hmd, LeftLocal - wobble, RightLocal - wobble), HandDevices.Default, null);
        sampler.Add(Tracking.Make(Hmd, null, RightLocal + wobble), HandDevices.Default, null);

        var input = sampler.TryGetInput(HandDevices.Default, needsAnchor: false, out var error);

        Assert.True(input is not null, error);
        AssertVec(LeftLocal, input!.LeftHand);
        AssertVec(RightLocal, input.RightHand);
        AssertVec(Hmd.Inverse().TransformDirection(new Vec3(0, 1, 0)), input.HeadUp);
        Assert.Null(input.AnchorInHmd);
        Assert.Equal(3, sampler.HmdSamples);
        Assert.Equal(2, sampler.LeftSamples);
    }

    [Fact]
    public void HeadMovementBetweenFrames_DoesNotMoveTheResult()
    {
        // 手の位置は HMD から見た位置で平均するので、頭と一緒に動いた分は打ち消される。
        var sampler = new CalibrationSampler();
        sampler.Add(Tracking.Make(Tracking.StandingHmd(yawDeg: 0), LeftLocal, RightLocal), HandDevices.Default, null);
        sampler.Add(Tracking.Make(Tracking.StandingHmd(yawDeg: 20), LeftLocal, RightLocal), HandDevices.Default, null);

        var input = sampler.TryGetInput(HandDevices.Default, needsAnchor: false, out _)!;

        AssertVec(LeftLocal, input.LeftHand);
    }

    [Theory]
    [InlineData(false, null, "NO LEFT HAND")]
    [InlineData(false, "LHR-1", "NO LEFT TRACKER")]
    [InlineData(true, null, "NO RIGHT HAND")]
    public void MissingHand_IsReported(bool leftVisible, string? leftSerial, string expected)
    {
        var sampler = new CalibrationSampler();
        var hands = new HandDevices(leftSerial, null);
        sampler.Add(Tracking.Make(Hmd, leftVisible ? LeftLocal : null, null), hands, null);

        Assert.Null(sampler.TryGetInput(hands, needsAnchor: false, out var error));
        Assert.Equal(expected, error);
    }

    [Fact]
    public void WithoutHmd_NothingIsCollected()
    {
        var sampler = new CalibrationSampler();
        sampler.Add(Tracking.Make(Hmd, LeftLocal, RightLocal, hmdValid: false), HandDevices.Default, null);

        Assert.Null(sampler.TryGetInput(HandDevices.Default, needsAnchor: false, out var error));
        Assert.Equal("NO HMD", error);
    }

    [Fact]
    public void TrackerSerial_IsUsedInsteadOfTheHand()
    {
        var sampler = new CalibrationSampler();
        var hands = new HandDevices("LHR-1", null);
        var trackerLocal = LeftLocal + new Vec3(0.05, 0, 0.1);
        // 手（ロール）は見えていても使わない。
        sampler.Add(Tracking.WithTracker(Tracking.Make(Hmd, LeftLocal, RightLocal), "LHR-1", Tracking.AtLocal(Hmd, trackerLocal)), hands, null);

        var input = sampler.TryGetInput(hands, needsAnchor: false, out _)!;

        AssertVec(trackerLocal, input.LeftHand);
    }

    [Fact]
    public void Anchor_UsesTheLastVisiblePose_AndIsRequiredWhenAsked()
    {
        var sampler = new CalibrationSampler();
        var anchor = new DeviceAnchorSpec(null, "VIOLIN", LostBehavior.Hold);
        var first = Tracking.AtLocal(Hmd, new Vec3(-0.2, -0.1, -0.3));
        var second = Tracking.AtLocal(Hmd, new Vec3(-0.21, -0.1, -0.3));

        sampler.Add(Tracking.Make(Hmd, LeftLocal, RightLocal), HandDevices.Default, anchor);
        Assert.Null(sampler.TryGetInput(HandDevices.Default, needsAnchor: true, out var error));
        Assert.Equal("NO ANCHOR DEVICE", error);

        sampler.Add(Tracking.WithTracker(Tracking.Make(Hmd, LeftLocal, RightLocal), "VIOLIN", first), HandDevices.Default, anchor);
        sampler.Add(Tracking.WithTracker(Tracking.Make(Hmd, LeftLocal, RightLocal), "VIOLIN", second), HandDevices.Default, anchor);
        sampler.Add(Tracking.WithTracker(Tracking.Make(Hmd, LeftLocal, RightLocal), "VIOLIN", null), HandDevices.Default, anchor);

        var input = sampler.TryGetInput(HandDevices.Default, needsAnchor: true, out _)!;
        AssertVec(new Vec3(-0.21, -0.1, -0.3), input.AnchorInHmd!.Value.Translation);
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var sampler = new CalibrationSampler();
        sampler.Add(Tracking.Make(Hmd, LeftLocal, RightLocal), HandDevices.Default, null);

        sampler.Clear();

        Assert.Null(sampler.TryGetInput(HandDevices.Default, needsAnchor: false, out var error));
        Assert.Equal("NO HMD", error);
    }

    private static void AssertVec(Vec3 expected, Vec3 actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
        Assert.Equal(expected.Z, actual.Z, 9);
    }

    [Fact]
    public void PostureDevice_IsAveragedSeparately_AndRequiredWhenSet()
    {
        var sampler = new CalibrationSampler();
        var violinLocal = new Vec3(-0.2, -0.12, -0.25);

        sampler.Add(Tracking.Make(Hmd, LeftLocal, RightLocal), HandDevices.Default, null, "VIOLIN");
        Assert.Null(sampler.TryGetInput(HandDevices.Default, needsAnchor: false, out var error, "VIOLIN"));
        Assert.Equal("NO AUTO TRACKER", error);

        sampler.Add(Tracking.WithTracker(Tracking.Make(Hmd, LeftLocal, RightLocal), "VIOLIN", Tracking.AtLocal(Hmd, violinLocal)), HandDevices.Default, null, "VIOLIN");
        var input = sampler.TryGetInput(HandDevices.Default, needsAnchor: false, out _, "VIOLIN")!;

        AssertVec(violinLocal, input.PostureDevice!.Value);
        AssertVec(LeftLocal, input.LeftHand);
    }
}
