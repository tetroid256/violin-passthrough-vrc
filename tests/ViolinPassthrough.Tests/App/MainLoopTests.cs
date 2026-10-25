using ViolinPassthrough.App;
using ViolinPassthrough.Mask;
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
            Loop = new MainLoop(Lifecycle, Config, State, Overlays);
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
    public void ConfigReload_AppliesDebugAndPresetOnlyWhenTheyChangeInTheFile()
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

        // ファイル上の値が変わったら反映する。
        var json = TestConfigs.DefaultWith("\"debugVisible\": false", "\"debugVisible\": true")
            .Replace("\"activePreset\": \"fingerboard\"", "\"activePreset\": \"whole\"");
        h.Config.Publish(TestConfigs.Parse(json));
        h.Loop.Tick();
        h.State.SetDebug(false);
        h.State.SetActivePreset("fingerboard");
        h.Config.Publish(TestConfigs.Parse(json.Replace("\"debugVisible\": true", "\"debugVisible\": false")));
        h.Loop.Tick();

        Assert.False(h.State.Debug);
        Assert.Equal("fingerboard", h.State.ActivePreset);
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
    public void QuitEvent_StopsTheLoop()
    {
        var h = new Harness();
        h.Lifecycle.NextEvents = AppEvents.QuitRequested;

        Assert.True(h.Loop.Tick());
    }
}
