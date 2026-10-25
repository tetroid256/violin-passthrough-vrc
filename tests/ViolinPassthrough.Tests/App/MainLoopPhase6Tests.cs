using ViolinPassthrough.Anchor;
using ViolinPassthrough.App;
using ViolinPassthrough.Feedback;
using ViolinPassthrough.Mask;
using ViolinPassthrough.Osc;
using ViolinPassthrough.State;

namespace ViolinPassthrough.Tests.App;

public class MainLoopPhase6Tests
{
    private const string Enabled = "/avatar/parameters/HandPassthrough";
    private const string Preset = "/avatar/parameters/HandPassthroughPreset";
    private const string Debug = "/avatar/parameters/HandPassthroughDebug";
    private const string Auto = "/avatar/parameters/HandPassthroughAuto";
    private const string Calibrate = "/avatar/parameters/HandPassthroughCalibrate";

    private static readonly Vec3 LeftLocal = new(-0.35, -0.10, -0.35);
    private static readonly Vec3 RightLocal = new(0.0, -0.25, -0.30);

    private readonly FakeLifecycle _lifecycle = new();
    private readonly FakeConfigSource _config;
    private readonly FakeOverlayFactory _factory = new();
    private readonly ManualTimeProvider _time = new();
    private readonly FakeStatusOverlay _statusOverlay = new();
    private readonly FakeBeeper _beeper = new();
    private readonly List<FakeOscEndpoint> _endpoints = new();
    private readonly PassthroughState _state;
    private readonly StatusDisplay _status;
    private readonly MainLoop _loop;

    public MainLoopPhase6Tests()
        : this(null)
    {
    }

    private MainLoopPhase6Tests(string? json)
    {
        _config = new FakeConfigSource(json is null ? TestConfigs.Default : TestConfigs.Parse(json));
        _state = MainLoop.CreateInitialState(_config.Current);
        _status = new StatusDisplay(_statusOverlay, _time);
        _loop = new MainLoop(_lifecycle, _config, _state, new MaskOverlaySet(_factory.Create), CreateOsc, _time, _status, _beeper);
        _lifecycle.Snapshot = Tracking.Make(Tracking.StandingHmd(yawDeg: 40), LeftLocal, RightLocal);
    }

    private FakeOscEndpoint Osc => _endpoints.Last();

    private FakeOverlay Fingerboard => _factory["fingerboard"];

    private static MainLoopPhase6Tests With(string oldText, string newText) =>
        new(TestConfigs.DefaultWith(oldText, newText));

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
    public void Calibration_CountsDown_Captures_AndPreviewsTheResult()
    {
        _loop.Tick();
        Osc.TakeSent();

        Receive(Calibrate, true);
        Assert.True(_loop.Calibrating);
        Assert.Equal("CALIBRATE 5", _status.CurrentText);
        Assert.Equal(new[] { BeepKind.Tick }, _beeper.Beeps);
        // VRChat 側は true のままでよいので、送り返さない。
        Assert.Empty(Osc.TakeSent());

        Run(TimeSpan.FromSeconds(5.05));

        Assert.False(_loop.Calibrating);
        var result = Assert.Single(_config.Calibrations);
        Assert.Equal("fingerboard", result.RegionId);
        Assert.Equal(LeftLocal.X, result.LeftHandPosition.X, 9);
        Assert.Equal("CALIBRATED", _status.CurrentText);
        Assert.Equal(5, _beeper.Beeps.Count(b => b == BeepKind.Tick));
        Assert.Equal(BeepKind.Success, _beeper.Beeps[^1]);
        Assert.Contains($"{Calibrate} False", Osc.TakeSent());

        // 無効の状態でも、結果をデバッグ表示で見せる。
        Assert.False(_state.Enabled);
        Assert.True(_loop.Previewing);
        Assert.True(Fingerboard.Visible);
        Assert.True(Fingerboard.Settings!.Debug);
        Assert.False(_factory["body"].Visible);

        Run(TimeSpan.FromSeconds(5.1));

        Assert.False(_loop.Previewing);
        Assert.False(Fingerboard.Visible);
        Assert.False(Fingerboard.Settings!.Debug);
    }

    [Fact]
    public void Calibration_CanBeCancelledFromTheMenu()
    {
        _loop.Tick();
        Receive(Calibrate, true);
        Run(TimeSpan.FromSeconds(2));

        Receive(Calibrate, false);
        Run(TimeSpan.FromSeconds(5));

        Assert.Empty(_config.Calibrations);
        Assert.False(_loop.Calibrating);
        Assert.Equal(BeepKind.Error, _beeper.Beeps[^1]);
    }

    [Fact]
    public void Calibration_RequestWhileRunning_DoesNotRestart()
    {
        _loop.Tick();
        Receive(Calibrate, true);
        Run(TimeSpan.FromSeconds(3));

        Receive(Calibrate, true);
        Run(TimeSpan.FromSeconds(2.1));

        Assert.Single(_config.Calibrations);
    }

    [Fact]
    public void Calibration_WithoutLeftHand_FailsWithoutChangingTheConfig()
    {
        _loop.Tick();
        _lifecycle.Snapshot = Tracking.Make(Tracking.StandingHmd(), null, RightLocal);
        Receive(Calibrate, true);
        Osc.TakeSent();

        Run(TimeSpan.FromSeconds(5.05));

        Assert.Empty(_config.Calibrations);
        Assert.Equal("CAL FAILED: NO LEFT HAND", _status.CurrentText);
        Assert.Equal(BeepKind.Error, _beeper.Beeps[^1]);
        Assert.Contains($"{Calibrate} False", Osc.TakeSent());
        Assert.False(_loop.Previewing);
    }

    [Fact]
    public void Calibration_OfANonHmdRegion_Fails()
    {
        var t = With(
            "\"anchor\": { \"type\": \"hmd\" },\n      \"shape\": { \"type\": \"ellipse\" }",
            "\"anchor\": { \"type\": \"device\", \"role\": \"leftHand\" },\n      \"shape\": { \"type\": \"ellipse\" }");
        t._loop.Tick();
        t.Receive(Calibrate, true);

        t.Run(TimeSpan.FromSeconds(5.05));

        Assert.Empty(t._config.Calibrations);
        Assert.Equal("CAL FAILED: REGION IS NOT HMD", t._status.CurrentText);
    }

    [Fact]
    public void Calibration_WriteFailure_IsReported()
    {
        _config.CalibrationError = "CANNOT WRITE CONFIG";
        _loop.Tick();
        Receive(Calibrate, true);

        Run(TimeSpan.FromSeconds(5.05));

        Assert.Equal("CAL FAILED: CANNOT WRITE CONFIG", _status.CurrentText);
        Assert.False(_loop.Previewing);
    }

    [Fact]
    public void Calibration_BeepCanBeDisabled()
    {
        var t = With("\"beep\": true", "\"beep\": false");
        t._loop.Tick();
        t.Receive(Calibrate, true);

        t.Run(TimeSpan.FromSeconds(5.05));

        Assert.Single(t._config.Calibrations);
        Assert.Empty(t._beeper.Beeps);
    }

    [Fact]
    public void AutoMode_ShowsTheMaskOnlyWhileInPlayingPosture()
    {
        _loop.Tick();
        Receive(Enabled, true);
        Assert.True(Fingerboard.Visible);

        // 楽器を下ろした姿勢で自動モードにする。
        _lifecycle.Snapshot = Tracking.Make(Tracking.StandingHmd(), new Vec3(-0.25, -0.65, -0.1), null);
        Receive(Auto, true);
        Assert.Equal("AUTO ON", _status.CurrentText);
        Assert.False(Fingerboard.Visible);

        // 構える: 0.3 秒後に表示。
        _lifecycle.Snapshot = Tracking.Make(Tracking.StandingHmd(), LeftLocal, RightLocal);
        Run(TimeSpan.FromSeconds(0.2));
        Assert.False(Fingerboard.Visible);
        Run(TimeSpan.FromSeconds(0.3));
        Assert.True(Fingerboard.Visible);
        Assert.True(_state.InPosture);

        // 下ろす: 2 秒後に隠す。
        _lifecycle.Snapshot = Tracking.Make(Tracking.StandingHmd(), new Vec3(-0.25, -0.65, -0.1), null);
        Run(TimeSpan.FromSeconds(1.5));
        Assert.True(Fingerboard.Visible);
        Run(TimeSpan.FromSeconds(0.8));
        Assert.False(Fingerboard.Visible);
    }

    [Fact]
    public void AutoMode_DebugStillShowsTheMask()
    {
        _loop.Tick();
        _lifecycle.Snapshot = Tracking.Make(Tracking.StandingHmd(), new Vec3(-0.25, -0.65, -0.1), null);
        Receive(Enabled, true);
        Receive(Auto, true);
        Assert.False(Fingerboard.Visible);

        Receive(Debug, true);

        Assert.True(Fingerboard.Visible);
    }

    [Fact]
    public void AutoMode_IsSyncedToVrchat_AndSetFromConfigWhenItChanges()
    {
        _loop.Tick();
        Osc.TakeSent();

        _config.Publish(TestConfigs.Parse(TestConfigs.DefaultWith("\"enabled\": false,\n    \"useLeftHand\"", "\"enabled\": true,\n    \"useLeftHand\"")));
        _loop.Tick();

        Assert.True(_state.Auto);
        Assert.Contains($"{Auto} True", Osc.TakeSent());
    }

    [Fact]
    public void TrackingIsOnlyRequestedWhileAutoModeIsActive()
    {
        _loop.Tick();
        Receive(Auto, true);
        _loop.Tick();
        Assert.Equal(0, _lifecycle.SnapshotCount);

        Receive(Enabled, true);
        _loop.Tick();

        Assert.True(_lifecycle.SnapshotCount >= 2);
    }

    [Fact]
    public void Toasts_ForPresetAndEnabledInAutoMode()
    {
        _loop.Tick();

        Receive(Preset, 1);
        Assert.Equal("PRESET WHOLE", _status.CurrentText);

        Receive(Auto, true);
        Receive(Enabled, true);
        Assert.Equal("ON - RAISE TO SHOW", _status.CurrentText);
    }

    [Fact]
    public void Toasts_NotShownForEnabledWithoutAutoMode()
    {
        _loop.Tick();

        Receive(Enabled, true);

        Assert.Null(_status.CurrentText);
    }

    [Fact]
    public void ConfigError_IsShown()
    {
        _loop.Tick();

        _config.InvalidCount++;
        _loop.Tick();

        Assert.Equal("CONFIG ERROR - SEE LOG", _status.CurrentText);
    }

    [Fact]
    public void ConfigReload_IsShown_ButNotOverTheCalibrationResult()
    {
        _loop.Tick();
        _config.Publish(TestConfigs.Default);
        _loop.Tick();
        Assert.Equal("CONFIG RELOADED", _status.CurrentText);

        Receive(Calibrate, true);
        Run(TimeSpan.FromSeconds(5.05));
        _config.Publish(TestConfigs.Default);
        _loop.Tick();

        Assert.Equal("CALIBRATED", _status.CurrentText);
    }

    private IOscEndpoint CreateOsc(ViolinPassthrough.Config.OscSettings settings)
    {
        var endpoint = new FakeOscEndpoint(settings);
        _endpoints.Add(endpoint);
        return endpoint;
    }
}
