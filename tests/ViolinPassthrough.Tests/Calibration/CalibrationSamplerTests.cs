using ViolinPassthrough.Anchor;
using ViolinPassthrough.Calibration;

namespace ViolinPassthrough.Tests.Calibration;

public class CalibrationSamplerTests
{
    private static readonly Pose3x4 Hmd = Tracking.StandingHmd(yawDeg: 25, rollDeg: 10);
    private static readonly Vec3 LeftLocal = new(-0.35, -0.10, -0.35);
    private static readonly Vec3 RightLocal = new(0.0, -0.25, -0.30);
    private static readonly CalibrationDevices Hands = new(HandDevices.Default, null, null);

    [Fact]
    public void AveragesThePositionsInHmdCoordinates()
    {
        var sampler = new CalibrationSampler();
        var wobble = new Vec3(0.01, -0.02, 0.005);
        sampler.Add(Tracking.Make(Hmd, LeftLocal + wobble, RightLocal), Hands);
        sampler.Add(Tracking.Make(Hmd, LeftLocal - wobble, RightLocal - wobble), Hands);
        sampler.Add(Tracking.Make(Hmd, null, RightLocal + wobble), Hands);

        var input = sampler.TryGetInput(requirePosture: true, out var error);

        Assert.True(input is not null, error);
        AssertVec(LeftLocal, input!.LeftHand);
        AssertVec(RightLocal, input.RightHand);
        AssertVec(Hmd.Inverse().TransformDirection(new Vec3(0, 1, 0)), input.HeadUp);
        Assert.Null(input.AnchorInHmd);
        // auto.deviceSerial が無ければ、左手の位置が自動モードの基準になる。
        AssertVec(LeftLocal, input.AutoReference!.Value);
        Assert.Equal(3, sampler.HmdSamples);
        Assert.Equal(2, sampler.LeftSamples);
    }

    [Fact]
    public void HeadMovementBetweenFrames_DoesNotMoveTheResult()
    {
        // 手の位置は HMD から見た位置で平均するので、頭と一緒に動いた分は打ち消される。
        var sampler = new CalibrationSampler();
        sampler.Add(Tracking.Make(Tracking.StandingHmd(yawDeg: 0), LeftLocal, RightLocal), Hands);
        sampler.Add(Tracking.Make(Tracking.StandingHmd(yawDeg: 20), LeftLocal, RightLocal), Hands);

        var input = sampler.TryGetInput(requirePosture: true, out _)!;

        AssertVec(LeftLocal, input.LeftHand);
    }

    [Theory]
    [InlineData(false, null, null, "NO LEFT HAND")]
    [InlineData(false, "LHR-1", null, "NO LEFT TRACKER")]
    [InlineData(true, null, null, "NO RIGHT HAND")]
    [InlineData(true, null, "LHR-2", "NO RIGHT TRACKER")]
    public void MissingHand_IsReported(bool leftVisible, string? leftSerial, string? rightSerial, string expected)
    {
        var sampler = new CalibrationSampler();
        var devices = new CalibrationDevices(new HandDevices(leftSerial, rightSerial), null, null);
        sampler.Add(Tracking.Make(Hmd, leftVisible ? LeftLocal : null, null), devices);

        Assert.Null(sampler.TryGetInput(requirePosture: true, out var error));
        Assert.Equal(expected, error);
    }

    [Fact]
    public void WithoutHmd_NothingIsCollected()
    {
        var sampler = new CalibrationSampler();
        sampler.Add(Tracking.Make(Hmd, LeftLocal, RightLocal, hmdValid: false), Hands);

        Assert.Null(sampler.TryGetInput(requirePosture: true, out var error));
        Assert.Equal("NO HMD", error);
    }

    [Fact]
    public void TrackerSerial_IsUsedInsteadOfTheHand()
    {
        var sampler = new CalibrationSampler();
        var devices = new CalibrationDevices(new HandDevices("LHR-1", null), null, null);
        var trackerLocal = LeftLocal + new Vec3(0.05, 0, 0.1);
        // 手（ロール）は見えていても使わない。
        sampler.Add(Tracking.WithTracker(Tracking.Make(Hmd, LeftLocal, RightLocal), "LHR-1", Tracking.AtLocal(Hmd, trackerLocal)), devices);

        var input = sampler.TryGetInput(requirePosture: true, out _)!;

        AssertVec(trackerLocal, input.LeftHand);
    }

    [Fact]
    public void Anchor_UsesTheLastVisiblePose_InHmdCoordinates_AndIsRequired()
    {
        var sampler = new CalibrationSampler();
        var devices = new CalibrationDevices(HandDevices.Default, new DeviceAnchorSpec(null, "VIOLIN", LostBehavior.Hold), null);
        var firstLocal = Pose3x4.FromPositionEuler(new Vec3(-0.2, -0.1, -0.3), new Vec3(10, 20, 30));
        var secondLocal = Pose3x4.FromPositionEuler(new Vec3(-0.21, -0.1, -0.3), new Vec3(-25, 40, 75));

        sampler.Add(Tracking.Make(Hmd, LeftLocal, RightLocal), devices);
        Assert.Null(sampler.TryGetInput(requirePosture: true, out var error));
        Assert.Equal("NO ANCHOR DEVICE", error);

        sampler.Add(Tracking.WithTracker(Tracking.Make(Hmd, LeftLocal, RightLocal), "VIOLIN", Hmd * firstLocal), devices);
        sampler.Add(Tracking.WithTracker(Tracking.Make(Hmd, LeftLocal, RightLocal), "VIOLIN", Hmd * secondLocal), devices);
        sampler.Add(Tracking.WithTracker(Tracking.Make(Hmd, LeftLocal, RightLocal), "VIOLIN", null), devices);

        var anchor = sampler.TryGetInput(requirePosture: true, out _)!.AnchorInHmd!.Value;
        AssertVec(secondLocal.Translation, anchor.Translation);
        AssertVec(secondLocal.TransformDirection(new Vec3(1, 0, 0)), anchor.TransformDirection(new Vec3(1, 0, 0)));
        AssertVec(secondLocal.TransformDirection(new Vec3(0, 0, 1)), anchor.TransformDirection(new Vec3(0, 0, 1)));
        Assert.Equal(2, sampler.AnchorSamples);
    }

    [Fact]
    public void PostureDevice_IsAveragedSeparately_AndUsedAsTheAutoReference()
    {
        var sampler = new CalibrationSampler();
        var devices = new CalibrationDevices(HandDevices.Default, null, "VIOLIN");
        var violinLocal = new Vec3(-0.2, -0.12, -0.25);

        sampler.Add(Tracking.Make(Hmd, LeftLocal, RightLocal), devices);
        Assert.Null(sampler.TryGetInput(requirePosture: true, out var error));
        Assert.Equal("NO AUTO TRACKER", error);

        sampler.Add(Tracking.WithTracker(Tracking.Make(Hmd, LeftLocal, RightLocal), "VIOLIN", Tracking.AtLocal(Hmd, violinLocal)), devices);
        var input = sampler.TryGetInput(requirePosture: true, out _)!;

        AssertVec(violinLocal, input.AutoReference!.Value);
        AssertVec(LeftLocal, input.LeftHand);
        Assert.Equal(1, sampler.PostureSamples);
    }

    [Fact]
    public void PostureDevice_NotRequired_RecordsNoAutoReference()
    {
        // 頭の傾きだけで判定するなら、楽器のトラッカーが無くても窓は合わせられる。左手の位置を代わりに記録はしない。
        var sampler = new CalibrationSampler();
        sampler.Add(Tracking.Make(Hmd, LeftLocal, RightLocal), new CalibrationDevices(HandDevices.Default, null, "VIOLIN"));

        var input = sampler.TryGetInput(requirePosture: false, out var error);

        Assert.True(input is not null, error);
        Assert.Null(input!.AutoReference);
    }

    [Fact]
    public void ChangingDevices_DiscardsTheEarlierSamples()
    {
        // 設定の再読み込みで手首のトラッカーに変わったら、手の位置と混ぜない。
        var sampler = new CalibrationSampler();
        var tracker = new CalibrationDevices(new HandDevices("LHR-1", null), null, null);
        var trackerLocal = LeftLocal + new Vec3(0.1, 0, 0);
        sampler.Add(Tracking.Make(Hmd, LeftLocal, RightLocal), Hands);

        sampler.Add(Tracking.WithTracker(Tracking.Make(Hmd, LeftLocal, RightLocal), "LHR-1", Tracking.AtLocal(Hmd, trackerLocal)), tracker);

        var input = sampler.TryGetInput(requirePosture: true, out _)!;
        AssertVec(trackerLocal, input.LeftHand);
        Assert.Equal(1, sampler.HmdSamples);
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var sampler = new CalibrationSampler();
        sampler.Add(Tracking.Make(Hmd, LeftLocal, RightLocal), Hands);

        sampler.Clear();

        Assert.Null(sampler.TryGetInput(requirePosture: true, out var error));
        Assert.Equal("NO HMD", error);
    }

    [Theory]
    [InlineData(1, 0.5)]
    [InlineData(2, 1.0)]
    [InlineData(5, 1.0)]
    public void Window_IsHalfTheCountdown_ForShortCountdowns(int countdown, double expected)
    {
        Assert.Equal(expected, CalibrationSampler.WindowFor(countdown).TotalSeconds, 9);
    }

    private static void AssertVec(Vec3 expected, Vec3 actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
        Assert.Equal(expected.Z, actual.Z, 9);
    }
}
