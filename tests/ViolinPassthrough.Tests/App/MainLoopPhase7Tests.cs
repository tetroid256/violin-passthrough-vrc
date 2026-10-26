using ViolinPassthrough.Anchor;
using ViolinPassthrough.App;
using ViolinPassthrough.Feedback;
using ViolinPassthrough.Mask;
using ViolinPassthrough.Osc;
using ViolinPassthrough.State;

namespace ViolinPassthrough.Tests.App;

/// <summary>左手のトラッキングが外れる問題への対策（DESIGN.md §16）。</summary>
public class MainLoopPhase7Tests
{
    private const string Enabled = "/avatar/parameters/HandPassthrough";
    private const string Auto = "/avatar/parameters/HandPassthroughAuto";
    private const string Calibrate = "/avatar/parameters/HandPassthroughCalibrate";
    private const string DefaultTracking = "\"leftHandSerial\": null";
    private const string Serial = "LHR-WRIST01";

    private static readonly Pose3x4 Hmd = Tracking.StandingHmd(yawDeg: 40);
    private static readonly Vec3 LeftLocal = new(-0.35, -0.10, -0.35);
    private static readonly Vec3 RightLocal = new(0.0, -0.25, -0.30);
    private static readonly Vec3 Lowered = new(-0.25, -0.65, -0.1);

    private readonly FakeLifecycle _lifecycle = new();
    private readonly FakeConfigSource _config;
    private readonly FakeOverlayFactory _factory = new();
    private readonly ManualTimeProvider _time = new();
    private readonly FakeStatusOverlay _statusOverlay = new();
    private readonly List<FakeOscEndpoint> _endpoints = new();
    private readonly PassthroughState _state;
    private readonly StatusDisplay _status;
    private readonly MainLoop _loop;

    public MainLoopPhase7Tests()
        : this(null)
    {
    }

    private MainLoopPhase7Tests(string? json)
    {
        _config = new FakeConfigSource(json is null ? TestConfigs.Default : TestConfigs.Parse(json));
        _state = MainLoop.CreateInitialState(_config.Current);
        _status = new StatusDisplay(_statusOverlay, _time);
        _loop = new MainLoop(_lifecycle, _config, _state, new MaskOverlaySet(_factory.Create), CreateOsc, _time, _status, new FakeBeeper());
        _lifecycle.Snapshot = Tracking.Make(Hmd, LeftLocal, RightLocal);
        _loop.Tick();
    }

    private FakeOscEndpoint Osc => _endpoints.Last();

    private FakeOverlay Fingerboard => _factory["fingerboard"];

    private static MainLoopPhase7Tests WithWristTracker() =>
        new(TestConfigs.DefaultWith(DefaultTracking, $"\"leftHandSerial\": \"{Serial}\""));

    private void Receive(string address, object value)
    {
        Osc.Incoming.Enqueue(new OscMessage(address, value));
        _loop.Tick();
    }

    private void Run(TimeSpan duration)
    {
        var step = TimeSpan.FromMilliseconds(100);
        for (var t = TimeSpan.Zero; t < duration; t += step)
        {
            _time.Advance(step);
            _loop.Tick();
        }
    }

    [Fact]
    public void Calibration_Succeeds_WhenTheLeftHandIsLostAtTheLastMoment()
    {
        Receive(Calibrate, true);
        Run(TimeSpan.FromSeconds(4.5));

        // 最後の 0.5 秒は左手を見失っている（指板を押さえた手のトラッキングが外れた）。
        _lifecycle.Snapshot = Tracking.Make(Hmd, null, RightLocal);
        Run(TimeSpan.FromSeconds(0.6));

        var result = Assert.Single(_config.Calibrations);
        Assert.Equal(LeftLocal.X, result.LeftHandPosition.X, 9);
        Assert.Equal("CALIBRATED", _status.CurrentText);
    }

    [Fact]
    public void Calibration_IgnoresPositionsBeforeTheLastSecond()
    {
        // 構える前（最後の 1 秒より前）の位置は使わない。
        _lifecycle.Snapshot = Tracking.Make(Hmd, Lowered, RightLocal);
        Receive(Calibrate, true);
        Run(TimeSpan.FromSeconds(3.8));

        _lifecycle.Snapshot = Tracking.Make(Hmd, LeftLocal, RightLocal);
        Run(TimeSpan.FromSeconds(1.3));

        var result = Assert.Single(_config.Calibrations);
        Assert.Equal(LeftLocal.Y, result.LeftHandPosition.Y, 9);
    }

    [Fact]
    public void Calibration_Fails_WhenTheLeftHandIsLostForTheWholeLastSecond()
    {
        Receive(Calibrate, true);
        Run(TimeSpan.FromSeconds(3.8));

        _lifecycle.Snapshot = Tracking.Make(Hmd, null, RightLocal);
        Run(TimeSpan.FromSeconds(1.3));

        Assert.Empty(_config.Calibrations);
        Assert.Equal("CAL FAIL: NO LEFT HAND", _status.CurrentText);
    }

    [Fact]
    public void Calibration_WithAWristTracker_UsesTheTracker()
    {
        var t = WithWristTracker();
        var trackerLocal = LeftLocal + new Vec3(0.04, -0.03, 0.08);
        t._lifecycle.Snapshot = Tracking.WithTracker(Tracking.Make(Hmd, null, RightLocal), Serial, Tracking.AtLocal(Hmd, trackerLocal));

        t.Receive(Calibrate, true);
        t.Run(TimeSpan.FromSeconds(5.05));

        var result = Assert.Single(t._config.Calibrations);
        Assert.Equal(trackerLocal.X, result.LeftHandPosition.X, 9);
        Assert.Equal(trackerLocal.Z, result.LeftHandPosition.Z, 9);
    }

    [Fact]
    public void Calibration_WithAMissingTracker_SaysSo()
    {
        var t = WithWristTracker();

        t.Receive(Calibrate, true);
        t.Run(TimeSpan.FromSeconds(5.05));

        Assert.Empty(t._config.Calibrations);
        Assert.Equal("CAL FAIL: NO LEFT TRACKER", t._status.CurrentText);
    }

    [Fact]
    public void Calibration_OfADeviceRegion_IsStoredInTheDeviceFrame()
    {
        // 楽器に付けたトラッカーに追従する指板の窓。
        const string violin = "LHR-VIOLIN";
        var t = new MainLoopPhase7Tests(TestConfigs.DefaultWith(
            "\"anchor\": { \"type\": \"hmd\" },\n      \"shape\": { \"type\": \"ellipse\" }",
            $"\"anchor\": {{ \"type\": \"device\", \"serial\": \"{violin}\" }},\n      \"shape\": {{ \"type\": \"ellipse\" }}"));
        var violinPose = Hmd * Pose3x4.FromPositionEuler(new Vec3(-0.15, -0.12, -0.25), new Vec3(-20, 35, 60));
        t._lifecycle.Snapshot = Tracking.WithTracker(Tracking.Make(Hmd, LeftLocal, RightLocal), violin, violinPose);

        t.Receive(Calibrate, true);
        t.Run(TimeSpan.FromSeconds(5.05));

        var result = Assert.Single(t._config.Calibrations);
        var expected = ViolinPassthrough.Calibration.Calibrator.Compute(
            Hmd, Tracking.AtLocal(Hmd, LeftLocal), Tracking.AtLocal(Hmd, RightLocal), TestConfigs.Default.Regions[0], 0.05, out _)!;
        var placed = violinPose * Pose3x4.FromPositionEuler(result.Position, result.RotationEulerDeg);
        var wanted = Hmd * Pose3x4.FromPositionEuler(expected.Position, expected.RotationEulerDeg);
        Assert.Equal(wanted.Translation.X, placed.Translation.X, 6);
        Assert.Equal(wanted.Translation.Y, placed.Translation.Y, 6);
        Assert.Equal(wanted.Translation.Z, placed.Translation.Z, 6);
    }

    [Fact]
    public void Calibration_OfADeviceRegion_FailsWithoutTheDevice()
    {
        var t = new MainLoopPhase7Tests(TestConfigs.DefaultWith(
            "\"anchor\": { \"type\": \"hmd\" },\n      \"shape\": { \"type\": \"ellipse\" }",
            "\"anchor\": { \"type\": \"device\", \"serial\": \"LHR-VIOLIN\" },\n      \"shape\": { \"type\": \"ellipse\" }"));

        t.Receive(Calibrate, true);
        t.Run(TimeSpan.FromSeconds(5.05));

        Assert.Empty(t._config.Calibrations);
        Assert.Equal("CAL FAIL: NO ANCHOR DEVICE", t._status.CurrentText);
    }

    [Fact]
    public void AutoMode_WithAWristTracker_FollowsTheTracker_EvenWhenTheHandIsLost()
    {
        var t = WithWristTracker();
        t._lifecycle.Snapshot = Tracking.WithTracker(Tracking.Make(Hmd, null, null), Serial, Tracking.AtLocal(Hmd, Lowered));
        t.Receive(Enabled, true);
        t.Receive(Auto, true);
        Assert.False(t.Fingerboard.Visible);

        t._lifecycle.Snapshot = Tracking.WithTracker(Tracking.Make(Hmd, null, null), Serial, Tracking.AtLocal(Hmd, LeftLocal));
        t.Run(TimeSpan.FromSeconds(0.5));

        Assert.True(t.Fingerboard.Visible);
    }

    [Fact]
    public void AutoMode_MissingTracker_IsShownOnce_AndLoggedAgainWhenFound()
    {
        var t = WithWristTracker();
        t.Receive(Enabled, true);
        t.Receive(Auto, true);
        Assert.Equal("NO LEFT TRACKER", t._status.CurrentText);

        t.Run(TimeSpan.FromSeconds(3.5));
        Assert.Null(t._status.CurrentText);

        t._lifecycle.Snapshot = Tracking.WithTracker(Tracking.Make(Hmd, null, null), Serial, Tracking.AtLocal(Hmd, LeftLocal));
        t.Run(TimeSpan.FromSeconds(0.5));
        Assert.True(t.Fingerboard.Visible);
    }

    private IOscEndpoint CreateOsc(ViolinPassthrough.Config.OscSettings settings)
    {
        var endpoint = new FakeOscEndpoint(settings);
        _endpoints.Add(endpoint);
        return endpoint;
    }

    [Fact]
    public void Calibration_WithAnAutoDevice_RecordsTheDeviceForAutoMode_ButPlacesThePlateFromTheHands()
    {
        var t = new MainLoopPhase7Tests(TestConfigs.DefaultWith("\"deviceSerial\": null", "\"deviceSerial\": \"LHR-VIOLIN\""));
        var violinLocal = new Vec3(-0.2, -0.12, -0.25);
        t._lifecycle.Snapshot = Tracking.WithTracker(Tracking.Make(Hmd, LeftLocal, RightLocal), "LHR-VIOLIN", Tracking.AtLocal(Hmd, violinLocal));

        t.Receive(Calibrate, true);
        t.Run(TimeSpan.FromSeconds(5.05));

        var result = Assert.Single(t._config.Calibrations);
        Assert.Equal(violinLocal.X, result.LeftHandPosition.X, 9);
        var expected = ViolinPassthrough.Calibration.Calibrator.Compute(
            Hmd, Tracking.AtLocal(Hmd, LeftLocal), Tracking.AtLocal(Hmd, RightLocal), TestConfigs.Default.Regions[0], 0.05, out _)!;
        Assert.Equal(expected.Position.X, result.Position.X, 9);
    }

    [Fact]
    public void AutoMode_WithAnAutoDevice_FollowsThatDevice()
    {
        var t = new MainLoopPhase7Tests(TestConfigs.DefaultWith("\"deviceSerial\": null", "\"deviceSerial\": \"LHR-VIOLIN\""));
        t._lifecycle.Snapshot = Tracking.WithTracker(Tracking.Make(Hmd, LeftLocal, null), "LHR-VIOLIN", Tracking.AtLocal(Hmd, Lowered));
        t.Receive(Enabled, true);
        t.Receive(Auto, true);
        t.Run(TimeSpan.FromSeconds(0.5));
        Assert.False(t.Fingerboard.Visible);

        t._lifecycle.Snapshot = Tracking.WithTracker(Tracking.Make(Hmd, null, null), "LHR-VIOLIN", Tracking.AtLocal(Hmd, new Vec3(-0.35, -0.10, -0.35)));
        t.Run(TimeSpan.FromSeconds(0.5));
        Assert.True(t.Fingerboard.Visible);
    }
}
