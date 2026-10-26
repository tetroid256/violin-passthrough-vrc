using ViolinPassthrough.App;
using ViolinPassthrough.Config;
using ViolinPassthrough.Mask;
using ViolinPassthrough.Osc;
using ViolinPassthrough.State;

namespace ViolinPassthrough.Tests.App;

public class MainLoopTests
{
    private sealed class Harness
    {
        public Harness(string? json = null)
        {
            Config = new FakeConfigSource(json is null ? TestConfigs.Default : TestConfigs.Parse(json));
            State = MainLoop.CreateInitialState(Config.Current);
            Overlays = new MaskOverlaySet(Factory.Create);
            Loop = new MainLoop(Lifecycle, Config, State, Overlays, CreateOsc, Time);
        }

        public ManualTimeProvider Time { get; } = new();

        public List<FakeOscEndpoint> OscEndpoints { get; } = new();

        public FakeOscEndpoint Osc => OscEndpoints.Last();

        private IOscEndpoint CreateOsc(OscSettings settings)
        {
            var endpoint = new FakeOscEndpoint(settings);
            OscEndpoints.Add(endpoint);
            return endpoint;
        }

        public FakeLifecycle Lifecycle { get; } = new();

        public FakeConfigSource Config { get; }

        public FakeOverlayFactory Factory { get; } = new();

        public PassthroughState State { get; }

        public MaskOverlaySet Overlays { get; }

        public MainLoop Loop { get; }

        public FakeOverlay this[string id] => Factory[id];
    }

    [Fact]
    public void FirstTick_CreatesHiddenOverlaysWhenDisabled()
    {
        var h = new Harness();

        Assert.False(h.Loop.Tick());

        Assert.Equal(2, h.Factory.Created.Count);
        Assert.All(h.Factory.Created, o => Assert.False(o.Visible));
    }

    [Fact]
    public void StartEnabled_ShowsActivePresetRegions()
    {
        var h = new Harness(TestConfigs.DefaultWith("\"startEnabled\": false", "\"startEnabled\": true"));

        h.Loop.Tick();

        Assert.True(h["fingerboard"].Visible);
        Assert.False(h["body"].Visible);
        Assert.False(h["fingerboard"].Settings!.Debug);
    }

    [Fact]
    public void StateChanges_AreReflectedOnNextTick()
    {
        var h = new Harness();
        h.Loop.Tick();

        h.State.SetEnabled(true);
        h.State.SetActivePreset("whole");
        h.Loop.Tick();

        Assert.True(h["fingerboard"].Visible);
        Assert.True(h["body"].Visible);

        h.State.SetEnabled(false);
        h.Loop.Tick();

        Assert.False(h["fingerboard"].Visible);
        Assert.False(h["body"].Visible);
    }

    [Fact]
    public void Debug_ShowsMaskEvenWhenDisabled_AndUsesDebugRendering()
    {
        var h = new Harness();
        h.Loop.Tick();

        h.State.SetDebug(true);
        h.Loop.Tick();

        Assert.True(h["fingerboard"].Visible);
        Assert.True(h["fingerboard"].Settings!.Debug);
        Assert.False(h["body"].Visible);
    }

    [Fact]
    public void ConfigReload_AppliesNewGeometry()
    {
        var h = new Harness();
        h.Loop.Tick();

        h.Config.Publish(TestConfigs.Parse(TestConfigs.DefaultWith("\"widthMeters\": 0.45", "\"widthMeters\": 0.5")));
        h.Loop.Tick();

        Assert.Equal(0.5, h["fingerboard"].Region!.WidthMeters);
    }

    [Fact]
    public void ConfigReload_AppliesDebugAndPresetWhenTheyChangeInTheFile()
    {
        var h = new Harness();
        h.Loop.Tick();
        Assert.False(h.State.Debug);
        Assert.Equal("fingerboard", h.State.ActivePreset);

        var json = TestConfigs.DefaultWith("\"debugVisible\": false", "\"debugVisible\": true")
            .Replace("\"activePreset\": \"fingerboard\"", "\"activePreset\": \"whole\"");
        h.Config.Publish(TestConfigs.Parse(json));
        h.Loop.Tick();

        Assert.True(h.State.Debug);
        Assert.Equal("whole", h.State.ActivePreset);
        Assert.True(h["body"].Visible);
        Assert.True(h["body"].Settings!.Debug);
    }

    [Fact]
    public void ConfigReload_DoesNotOverrideStateWhenThoseKeysAreUnchanged()
    {
        var h = new Harness();
        h.Loop.Tick();

        // OSC などで状態を変えた後、関係ない項目だけ変わった設定が来ても状態は上書きしない。
        h.State.SetDebug(true);
        h.State.SetActivePreset("whole");
        h.Config.Publish(TestConfigs.Parse(TestConfigs.DefaultWith("\"g\": 0", "\"g\": 1")));
        h.Loop.Tick();

        Assert.True(h.State.Debug);
        Assert.Equal("whole", h.State.ActivePreset);
    }

    [Fact]
    public void ConfigReload_AppliesOnlyTheKeyThatChanged()
    {
        var h = new Harness();
        h.Loop.Tick();
        h.State.SetActivePreset("whole");

        // debugVisible だけが変わった。activePreset（fingerboard のまま）は状態に反映しない。
        h.Config.Publish(TestConfigs.Parse(TestConfigs.DefaultWith("\"debugVisible\": false", "\"debugVisible\": true")));
        h.Loop.Tick();

        Assert.True(h.State.Debug);
        Assert.Equal("whole", h.State.ActivePreset);
    }

    [Fact]
    public void ConfigReload_StartEnabledIsIgnoredAfterStartup()
    {
        var h = new Harness();
        h.Loop.Tick();

        h.Config.Publish(TestConfigs.Parse(TestConfigs.DefaultWith("\"startEnabled\": false", "\"startEnabled\": true")));
        h.Loop.Tick();

        Assert.False(h.State.Enabled);
    }

    [Fact]
    public void ConfigReload_FallsBackWhenActivePresetDisappears()
    {
        var h = new Harness();
        h.State.SetEnabled(true);
        h.State.SetActivePreset("whole");
        h.Loop.Tick();
        Assert.True(h["body"].Visible);

        var json = TestConfigs.DefaultWith(",\n    \"whole\": [\"fingerboard\", \"body\"]", string.Empty);
        h.Config.Publish(TestConfigs.Parse(json));
        h.Loop.Tick();

        Assert.Equal("fingerboard", h.State.ActivePreset);
        Assert.True(h["fingerboard"].Visible);
        Assert.False(h["body"].Visible);
    }

    [Fact]
    public void TrackingSnapshot_IsOnlyRequestedForDeviceAnchors()
    {
        var h = new Harness();
        h.Loop.Tick();
        h.Loop.Tick();
        Assert.Equal(0, h.Lifecycle.SnapshotCount);

        h.Config.Publish(TestConfigs.Parse(TestConfigs.DefaultWith(
            "\"anchor\": { \"type\": \"hmd\" },\n      \"shape\": { \"type\": \"ellipse\" }",
            "\"anchor\": { \"type\": \"device\", \"role\": \"leftHand\" },\n      \"shape\": { \"type\": \"ellipse\" }")));
        h.Loop.Tick();
        h["fingerboard"].RequiresTracking = true;
        h.Loop.Tick();

        Assert.Equal(1, h.Lifecycle.SnapshotCount);
        Assert.Same(h.Lifecycle.Snapshot, h["fingerboard"].LastTracking);
        Assert.IsType<ViolinPassthrough.Anchor.DeviceAnchorSpec>(h["fingerboard"].Region!.Anchor);
    }

    [Fact]
    public void QuitEvent_StopsTheLoop()
    {
        var h = new Harness();
        h.Lifecycle.NextEvents = AppEvents.QuitRequested;

        Assert.True(h.Loop.Tick());
    }
}

public class MainLoopOscTests
{
    private const string Enabled = "/avatar/parameters/HandPassthrough";
    private const string Preset = "/avatar/parameters/HandPassthroughPreset";
    private const string Debug = "/avatar/parameters/HandPassthroughDebug";
    private const string Auto = "/avatar/parameters/HandPassthroughAuto";
    private const string Calibrate = "/avatar/parameters/HandPassthroughCalibrate";

    private const string AutoMethod = "/avatar/parameters/HandPassthroughAutoMethod";
    private const string Violin = "/avatar/parameters/HandPassthroughViolin";
    private const string ViolinState = "/avatar/parameters/HandPassthroughViolinState";

    private static readonly string[] AllOff =
    {
        $"{Enabled} False", $"{Preset} 0", $"{Debug} False", $"{Auto} False", $"{Calibrate} False",
        $"{AutoMethod} 0", $"{Violin} 0", $"{ViolinState} 0",
    };

    private static readonly string[] EnabledOnly =
    {
        $"{Enabled} True", $"{Preset} 0", $"{Debug} False", $"{Auto} False", $"{Calibrate} False",
        $"{AutoMethod} 0", $"{Violin} 0", $"{ViolinState} 0",
    };

    private readonly FakeLifecycle _lifecycle = new();
    private readonly FakeConfigSource _config = new(TestConfigs.Default);
    private readonly FakeOverlayFactory _factory = new();
    private readonly ManualTimeProvider _time = new();
    private readonly List<FakeOscEndpoint> _endpoints = new();
    private readonly PassthroughState _state;
    private readonly MainLoop _loop;

    public MainLoopOscTests()
    {
        _state = MainLoop.CreateInitialState(_config.Current);
        _loop = new MainLoop(_lifecycle, _config, _state, new MaskOverlaySet(_factory.Create), CreateOsc, _time);
    }

    private FakeOscEndpoint Osc => _endpoints.Last();

    [Fact]
    public void Startup_SendsCurrentStateToVrchat()
    {
        _loop.Tick();

        Assert.Equal(AllOff, Osc.TakeSent());

        _loop.Tick();
        Assert.Empty(Osc.TakeSent());
    }

    [Fact]
    public void Toggle_ShowsAndHidesMaskWithoutEcho()
    {
        _loop.Tick();
        Osc.TakeSent();

        Osc.Incoming.Enqueue(new OscMessage(Enabled, true));
        _loop.Tick();

        Assert.True(_factory["fingerboard"].Visible);
        Assert.Empty(Osc.TakeSent());

        Osc.Incoming.Enqueue(new OscMessage(Enabled, false));
        _loop.Tick();

        Assert.False(_factory["fingerboard"].Visible);
        Assert.Empty(Osc.TakeSent());
    }

    [Fact]
    public void PresetAndDebug_FromOsc()
    {
        _loop.Tick();
        Osc.Incoming.Enqueue(new OscMessage(Enabled, true));
        Osc.Incoming.Enqueue(new OscMessage(Preset, 1));
        Osc.Incoming.Enqueue(new OscMessage(Debug, true));
        _loop.Tick();

        Assert.True(_factory["fingerboard"].Visible);
        Assert.True(_factory["body"].Visible);
        Assert.True(_factory["body"].Settings!.Debug);
    }

    [Fact]
    public void OutOfRangePreset_IsCorrectedInVrchat()
    {
        _loop.Tick();
        Osc.TakeSent();

        Osc.Incoming.Enqueue(new OscMessage(Preset, 7));
        _loop.Tick();

        Assert.Equal(new[] { $"{Preset} 0" }, Osc.TakeSent());
    }

    [Fact]
    public void AvatarChange_KeepsStateIgnoresDefaultsAndResendsState()
    {
        _loop.Tick();
        Osc.Incoming.Enqueue(new OscMessage(Enabled, true));
        _loop.Tick();
        Osc.TakeSent();

        // アバター変更の直後に、新しいアバターの既定値（false）が届く。
        Osc.Incoming.Enqueue(new OscMessage("/avatar/change", "avtr_new"));
        Osc.Incoming.Enqueue(new OscMessage(Enabled, false));
        _loop.Tick();

        Assert.True(_state.Enabled);
        Assert.True(_factory["fingerboard"].Visible);
        Assert.Equal(EnabledOnly, Osc.TakeSent());

        _time.Advance(MainLoop.AvatarChangeGrace / 2);
        Osc.Incoming.Enqueue(new OscMessage(Enabled, false));
        _loop.Tick();
        Assert.True(_state.Enabled);
        Assert.Empty(Osc.TakeSent());

        // 猶予が過ぎたら、もう一度送る。その後のトグルは普通に効く。
        _time.Advance(MainLoop.AvatarChangeGrace);
        _loop.Tick();
        Assert.Equal(EnabledOnly, Osc.TakeSent());

        Osc.Incoming.Enqueue(new OscMessage(Enabled, false));
        _loop.Tick();
        Assert.False(_state.Enabled);
        Assert.Empty(Osc.TakeSent());
    }

    [Fact]
    public void ConfigDrivenStateChange_IsSentToVrchat()
    {
        _loop.Tick();
        Osc.TakeSent();

        _config.Publish(TestConfigs.Parse(TestConfigs.DefaultWith("\"debugVisible\": false", "\"debugVisible\": true")));
        _loop.Tick();

        Assert.Equal(new[] { $"{Debug} True" }, Osc.TakeSent());
    }

    [Fact]
    public void ReorderedPresets_SendNewIndex()
    {
        _loop.Tick();
        Osc.TakeSent();

        var json = TestConfigs.DefaultWith(
            "\"fingerboard\": [\"fingerboard\"],\n    \"whole\": [\"fingerboard\", \"body\"]",
            "\"whole\": [\"fingerboard\", \"body\"],\n    \"fingerboard\": [\"fingerboard\"]");
        _config.Publish(TestConfigs.Parse(json));
        _loop.Tick();

        Assert.Equal(new[] { $"{Preset} 1" }, Osc.TakeSent());
    }

    [Fact]
    public void OscSettingsChange_RestartsEndpointAndResends()
    {
        _loop.Tick();
        var first = Osc;

        var json = TestConfigs.DefaultWith("\"enabled\": \"HandPassthrough\"", "\"enabled\": \"PT\"");
        _config.Publish(TestConfigs.Parse(json));
        _loop.Tick();

        Assert.True(first.Disposed);
        Assert.NotSame(first, Osc);
        Assert.Equal("PT", Osc.Settings.EnabledParameter);
        Assert.Contains("/avatar/parameters/PT False", Osc.TakeSent());

        Osc.Incoming.Enqueue(new OscMessage("/avatar/parameters/PT", true));
        _loop.Tick();
        Assert.True(_state.Enabled);
    }

    [Fact]
    public void UnrelatedConfigChange_KeepsEndpoint()
    {
        _loop.Tick();
        var first = Osc;

        _config.Publish(TestConfigs.Parse(TestConfigs.DefaultWith("\"g\": 0", "\"g\": 1")));
        _loop.Tick();

        Assert.Same(first, Osc);
        Assert.False(first.Disposed);
    }

    [Fact]
    public void Dispose_StopsOsc()
    {
        _loop.Tick();

        _loop.Dispose();

        Assert.True(Osc.Disposed);
    }

    [Fact]
    public void OscStartFailure_DoesNotBreakTheLoop()
    {
        var loop = new MainLoop(_lifecycle, _config, _state, new MaskOverlaySet(_factory.Create), _ => null, _time);

        Assert.False(loop.Tick());
        Assert.False(loop.Tick());
    }

    private IOscEndpoint CreateOsc(ViolinPassthrough.Config.OscSettings settings)
    {
        var endpoint = new FakeOscEndpoint(settings);
        _endpoints.Add(endpoint);
        return endpoint;
    }
}
