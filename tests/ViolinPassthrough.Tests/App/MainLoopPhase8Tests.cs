using ViolinPassthrough.Anchor;
using ViolinPassthrough.App;
using ViolinPassthrough.Feedback;
using ViolinPassthrough.Mask;
using ViolinPassthrough.Osc;
using ViolinPassthrough.State;

namespace ViolinPassthrough.Tests.App;

/// <summary>判定方法の切り替えとバイオリンのモデルの状態（DESIGN.md §17）。</summary>
public class MainLoopPhase8Tests
{
    private const string Enabled = "/avatar/parameters/HandPassthrough";
    private const string Auto = "/avatar/parameters/HandPassthroughAuto";
    private const string Calibrate = "/avatar/parameters/HandPassthroughCalibrate";
    private const string Method = "/avatar/parameters/HandPassthroughAutoMethod";
    private const string Violin = "/avatar/parameters/HandPassthroughViolin";
    private const string ViolinStateAddress = "/avatar/parameters/HandPassthroughViolinState";

    private static readonly Pose3x4 Hmd = Tracking.StandingHmd(yawDeg: 40);
    private static readonly Vec3 LeftLocal = new(-0.35, -0.10, -0.35);
    private static readonly Vec3 RightLocal = new(0.0, -0.25, -0.30);
    private static readonly Vec3 Lowered = new(-0.25, -0.65, -0.1);

    // 顎当てに顎を乗せて頭を左に 20° 傾けた姿勢。
    private static readonly Pose3x4 TiltedHmd = Tracking.StandingHmd(yawDeg: 40, rollDeg: 20);

    private readonly FakeLifecycle _lifecycle = new();
    private readonly FakeConfigSource _config;
    private readonly FakeOverlayFactory _factory = new();
    private readonly ManualTimeProvider _time = new();
    private readonly FakeStatusOverlay _statusOverlay = new();
    private readonly List<FakeOscEndpoint> _endpoints = new();
    private readonly PassthroughState _state;
    private readonly StatusDisplay _status;
    private readonly MainLoop _loop;

    public MainLoopPhase8Tests()
        : this(null)
    {
    }

    private MainLoopPhase8Tests(string? json)
    {
        _config = new FakeConfigSource(json is null ? TestConfigs.Default : TestConfigs.Parse(json));
        _state = MainLoop.CreateInitialState(_config.Current);
        _status = new StatusDisplay(_statusOverlay, _time);
        _loop = new MainLoop(_lifecycle, _config, _state, new MaskOverlaySet(_factory.Create), CreateOsc, _time, _status, new FakeBeeper());
        _lifecycle.Snapshot = Tracking.Make(Hmd, Lowered, RightLocal);
        _loop.Tick();
        Osc.TakeSent();
    }

    private FakeOscEndpoint Osc => _endpoints.Last();

    private FakeOverlay Fingerboard => _factory["fingerboard"];

    /// <summary>構えたときの頭の傾き（HMD から見た「上」）を記録済みの設定。</summary>
    private static MainLoopPhase8Tests WithHeadReference()
    {
        var up = TiltedHmd.Inverse().TransformDirection(new Vec3(0, 1, 0));
        return new MainLoopPhase8Tests(TestConfigs.DefaultWith(
            "\"useHeadTilt\": false,",
            FormattableString.Invariant($"\"useHeadTilt\": false,\n    \"headUp\": [{up.X}, {up.Y}, {up.Z}],")));
    }

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
    public void InitialState_ComesFromTheConfig()
    {
        var t = new MainLoopPhase8Tests(TestConfigs.DefaultWith("\"useHeadTilt\": false,", "\"useHeadTilt\": true,"));
        Assert.Equal(AutoMethod.LeftHandAndHead, t._state.AutoMethod);

        var fresh = new MainLoopPhase8Tests();
        Assert.Equal(AutoMethod.LeftHand, fresh._state.AutoMethod);
        Assert.Equal(ViolinState.Away, fresh._state.ViolinState);
    }

    [Fact]
    public void HeadOnly_DetectsThePosture_WithoutTheHands()
    {
        var t = WithHeadReference();
        t.Receive(Enabled, true);
        t.Receive(Auto, true);
        t.Receive(Method, 2);
        Assert.Equal("AUTO: HEAD", t._status.CurrentText);

        // 両手とも見えないが、頭を傾けている。
        t._lifecycle.Snapshot = Tracking.Make(TiltedHmd, null, null);
        t.Run(TimeSpan.FromSeconds(0.5));
        Assert.True(t.Fingerboard.Visible);

        // 頭を戻すと隠れる（手は関係ない）。
        t._lifecycle.Snapshot = Tracking.Make(Hmd, LeftLocal, RightLocal);
        t.Run(TimeSpan.FromSeconds(2.5));
        Assert.False(t.Fingerboard.Visible);
    }

    [Fact]
    public void HandAndHead_UsesTheHeadWhileTheHandIsLost()
    {
        var t = WithHeadReference();
        t.Receive(Enabled, true);
        t.Receive(Auto, true);

        // 左手のみ（既定）: 構えたときに左手が見えていないと出ない。
        t._lifecycle.Snapshot = Tracking.Make(TiltedHmd, null, null);
        t.Run(TimeSpan.FromSeconds(0.5));
        Assert.False(t.Fingerboard.Visible);

        t.Receive(Method, 1);
        Assert.Equal("AUTO: HAND+HEAD", t._status.CurrentText);
        t.Run(TimeSpan.FromSeconds(0.5));
        Assert.True(t.Fingerboard.Visible);
    }

    [Fact]
    public void HeadMethod_WithoutCalibration_SaysSo_AndUsesTheLeftHand()
    {
        Receive(Enabled, true);
        Receive(Auto, true);

        Receive(Method, 2);

        Assert.Equal("CALIBRATE FOR HEAD", _status.CurrentText);
        Assert.Equal(AutoMethod.Head, _state.AutoMethod);
        _lifecycle.Snapshot = Tracking.Make(Hmd, LeftLocal, null);
        Run(TimeSpan.FromSeconds(0.5));
        Assert.True(Fingerboard.Visible);
    }

    [Fact]
    public void Method_IsEchoedToVrchat_AndConfigChangesApply()
    {
        Receive(Method, 1);
        // メニューで選んだ値は VRChat が持っているので送り返さない。
        Assert.DoesNotContain(Osc.TakeSent(), m => m.StartsWith(Method, StringComparison.Ordinal));

        _config.Publish(TestConfigs.Parse(TestConfigs.DefaultWith(
            "\"useLeftHand\": true,\n    \"useHeadTilt\": false,",
            "\"useLeftHand\": false,\n    \"useHeadTilt\": true,")));
        _loop.Tick();

        Assert.Equal(AutoMethod.Head, _state.AutoMethod);
        Assert.Contains($"{Method} 2", Osc.TakeSent());
    }

    [Fact]
    public void Violin_HoldThenRaiseThenLower_SendsTheStates()
    {
        Receive(Violin, 1);
        Assert.Equal("VIOLIN: HOLD", _status.CurrentText);
        Assert.Contains($"{ViolinStateAddress} 1", Osc.TakeSent());

        // 構える: 自動モードでなくても、持っている間は判定する。
        _lifecycle.Snapshot = Tracking.Make(Hmd, LeftLocal, RightLocal);
        Run(TimeSpan.FromSeconds(0.5));
        Assert.Equal(ViolinState.Playing, _state.ViolinState);
        Assert.Contains($"{ViolinStateAddress} 2", Osc.TakeSent());

        // 下ろす: 2 秒後に「持つ」に戻る。
        _lifecycle.Snapshot = Tracking.Make(Hmd, Lowered, RightLocal);
        Run(TimeSpan.FromSeconds(1.5));
        Assert.Equal(ViolinState.Playing, _state.ViolinState);
        Run(TimeSpan.FromSeconds(0.8));
        Assert.Equal(ViolinState.Held, _state.ViolinState);
        Assert.Contains($"{ViolinStateAddress} 1", Osc.TakeSent());

        // しまう。
        Receive(Violin, 0);
        Assert.Equal(ViolinState.Away, _state.ViolinState);
        Assert.Contains($"{ViolinStateAddress} 0", Osc.TakeSent());
    }

    [Fact]
    public void Violin_AwayWhilePlaying_GoesStraightAway()
    {
        _lifecycle.Snapshot = Tracking.Make(Hmd, LeftLocal, RightLocal);
        Receive(Violin, 1);
        Run(TimeSpan.FromSeconds(0.5));
        Assert.Equal(ViolinState.Playing, _state.ViolinState);

        Receive(Violin, 0);

        Assert.Equal(ViolinState.Away, _state.ViolinState);
        Assert.False(_state.InPosture);
    }

    [Fact]
    public void Violin_ManualPlay_IgnoresTheDetection_AndShowsTheAutoWindow()
    {
        Receive(Enabled, true);
        Receive(Auto, true);
        Assert.False(Fingerboard.Visible);

        // 判定がうまくいかないとき: 手動で「構える」にすると、楽器を下ろした姿勢でも構えたままにする。
        Receive(Violin, 2);
        Run(TimeSpan.FromSeconds(3));

        Assert.Equal(ViolinState.Playing, _state.ViolinState);
        Assert.True(Fingerboard.Visible);
    }

    [Fact]
    public void Violin_Away_DoesNotRequestTracking()
    {
        var before = _lifecycle.SnapshotCount;

        Run(TimeSpan.FromSeconds(1));

        Assert.Equal(before, _lifecycle.SnapshotCount);
    }

    [Fact]
    public void AvatarChange_SendsTheViolinStateNow_AndAgainAfterTheGrace()
    {
        Receive(Violin, 2);
        Osc.TakeSent();

        Receive("/avatar/change", "avtr_other");
        Assert.Contains($"{ViolinStateAddress} 2", Osc.TakeSent());

        Receive(Violin, 0); // 新しいアバターの既定値は無視する。
        Run(TimeSpan.FromSeconds(2.1));

        Assert.Equal(ViolinState.Playing, _state.ViolinState);
        var sent = Osc.TakeSent();
        Assert.Contains($"{ViolinStateAddress} 2", sent);
        Assert.Contains($"{Violin} 2", sent);
    }

    [Fact]
    public void Calibration_WithTheHeadMethod_DoesNotNeedTheAutoDevice()
    {
        // 頭の傾きだけで判定するなら、auto.deviceSerial のトラッカーが無くても窓は合わせられる。
        var t = new MainLoopPhase8Tests(TestConfigs.DefaultWith("\"deviceSerial\": null", "\"deviceSerial\": \"LHR-VIOLIN\""));
        t._lifecycle.Snapshot = Tracking.Make(Hmd, LeftLocal, RightLocal);
        t.Receive(Method, 2);

        t.Receive(Calibrate, true);
        t.Run(TimeSpan.FromSeconds(5.05));

        var result = Assert.Single(t._config.Calibrations);
        Assert.Null(result.LeftHandPosition);
    }

    [Fact]
    public void PlayThenHold_WhileStillRaised_StaysPlaying()
    {
        // 手動の「構える」の間も判定しているので、「持つ」に戻しても構えたままなら「持つ」を挟まない（モデルが行き来しない）。
        _lifecycle.Snapshot = Tracking.Make(Hmd, LeftLocal, RightLocal);
        Receive(Violin, 2);
        Run(TimeSpan.FromSeconds(0.5));
        Osc.TakeSent();

        Receive(Violin, 1);

        Assert.Equal(ViolinState.Playing, _state.ViolinState);
        Assert.DoesNotContain(Osc.TakeSent(), m => m.StartsWith(ViolinStateAddress, StringComparison.Ordinal));
    }

    [Fact]
    public void HoldThenAway_WithAutoOn_KeepsDetectingForTheWindow()
    {
        _lifecycle.Snapshot = Tracking.Make(Hmd, LeftLocal, RightLocal);
        Receive(Enabled, true);
        Receive(Auto, true);
        Receive(Violin, 1);
        Run(TimeSpan.FromSeconds(0.5));
        Assert.True(Fingerboard.Visible);

        Receive(Violin, 0);

        Assert.Equal(ViolinState.Away, _state.ViolinState);
        Assert.True(_state.InPosture);
        Assert.True(Fingerboard.Visible);
    }

    [Fact]
    public void Exit_PutsTheViolinAway_AndTurnsCalibrateOff()
    {
        Receive(Violin, 2);
        Receive(Calibrate, true);
        Osc.TakeSent();

        _loop.Dispose();

        var sent = Osc.TakeSent();
        Assert.Contains($"{ViolinStateAddress} 0", sent);
        Assert.Contains($"{Violin} 0", sent);
        Assert.Contains($"{Calibrate} False", sent);
    }

    [Fact]
    public void Exit_WithTheViolinAway_SendsNothing()
    {
        _loop.Dispose();

        Assert.Empty(Osc.TakeSent());
    }

    [Fact]
    public void HeadMethodFromTheConfig_WithoutCalibration_IsReportedOnce()
    {
        var t = new MainLoopPhase8Tests(TestConfigs.DefaultWith(
            "\"useLeftHand\": true,\n    \"useHeadTilt\": false,",
            "\"useLeftHand\": false,\n    \"useHeadTilt\": true,"));
        // 判定が要らない間は知らせない。
        Assert.Null(t._status.CurrentText);

        t.Receive(Enabled, true);
        t.Receive(Auto, true);
        Assert.Equal("CALIBRATE FOR HEAD", t._status.CurrentText);

        t.Run(TimeSpan.FromSeconds(3.5));
        Assert.Null(t._status.CurrentText);
    }

    [Fact]
    public void OscMethod_SurvivesAnUnrelatedConfigReload()
    {
        Receive(Method, 2);

        _config.Publish(TestConfigs.Parse(TestConfigs.DefaultWith("\"radiusMeters\": 0.15", "\"radiusMeters\": 0.2")));
        _loop.Tick();

        Assert.Equal(AutoMethod.Head, _state.AutoMethod);
    }

    [Fact]
    public void MethodSwitch_WhileRaised_KeepsTheWindow_ThenUsesTheNewMethod()
    {
        var t = WithHeadReference();
        t._lifecycle.Snapshot = Tracking.Make(Hmd, LeftLocal, RightLocal);
        t.Receive(Enabled, true);
        t.Receive(Auto, true);
        t.Run(TimeSpan.FromSeconds(0.5));
        Assert.True(t.Fingerboard.Visible);

        // 頭を傾けていないので、頭の傾きだけの方法では外れている。すぐには消えず、待ち時間の後で消える。
        t.Receive(Method, 2);
        Assert.True(t.Fingerboard.Visible);
        t.Run(TimeSpan.FromSeconds(2.5));
        Assert.False(t.Fingerboard.Visible);
    }

    [Fact]
    public void MethodSwitch_BeforeRaising_RestartsTheEnterDelay()
    {
        var t = WithHeadReference();
        t._lifecycle.Snapshot = Tracking.Make(TiltedHmd, LeftLocal, RightLocal);
        t.Receive(Enabled, true);
        t.Receive(Auto, true);
        t.Run(TimeSpan.FromSeconds(0.2));
        Assert.False(t.Fingerboard.Visible);

        // 前の方法で数えた 0.2 秒は使わない。
        t.Receive(Method, 1);
        t.Run(TimeSpan.FromSeconds(0.2));
        Assert.False(t.Fingerboard.Visible);
        t.Run(TimeSpan.FromSeconds(0.2));
        Assert.True(t.Fingerboard.Visible);
    }

    [Fact]
    public void Calibration_OscLeftHandMethod_RequiresTheAutoDevice_EvenIfTheConfigUsesTheHead()
    {
        var t = new MainLoopPhase8Tests(TestConfigs.DefaultWith(
            "\"useLeftHand\": true,\n    \"useHeadTilt\": false,\n    \"deviceSerial\": null,",
            "\"useLeftHand\": false,\n    \"useHeadTilt\": true,\n    \"deviceSerial\": \"LHR-VIOLIN\","));
        t._lifecycle.Snapshot = Tracking.Make(Hmd, LeftLocal, RightLocal);
        t.Receive(Method, 0);

        t.Receive(Calibrate, true);
        t.Run(TimeSpan.FromSeconds(5.05));

        Assert.Empty(t._config.Calibrations);
        Assert.Equal("CAL FAIL: NO AUTO TRACKER", t._status.CurrentText);
    }

    private IOscEndpoint CreateOsc(ViolinPassthrough.Config.OscSettings settings)
    {
        var endpoint = new FakeOscEndpoint(settings);
        _endpoints.Add(endpoint);
        return endpoint;
    }
}
