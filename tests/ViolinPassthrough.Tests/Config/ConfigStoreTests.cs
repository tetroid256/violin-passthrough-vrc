using ViolinPassthrough.Config;
using ViolinPassthrough.Mask;

namespace ViolinPassthrough.Tests.Config;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vp-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new();

    private string ConfigPath => Path.Combine(_dir, "sub", "config.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Initialize_CopiesDefaultConfigWhenMissing()
    {
        using var store = new ConfigStore(ConfigPath, _time);

        store.Initialize();

        Assert.True(File.Exists(ConfigPath));
        Assert.Equal(TestConfigs.DefaultJson, File.ReadAllText(ConfigPath));
        Assert.Equal(TestConfigs.Default.Render, store.Current.Render);
        Assert.Equal(TestConfigs.Default.Regions, store.Current.Regions);
    }

    [Fact]
    public void Initialize_WithInvalidFile_FallsBackToDefault()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, "{ broken");
        using var store = new ConfigStore(ConfigPath, _time);

        store.Initialize();

        Assert.Equal("{ broken", File.ReadAllText(ConfigPath));
        Assert.Equal(new Rgb(255, 0, 255), store.Current.Render.KeyColor);
    }

    [Fact]
    public void TryReload_PicksUpChangesAfterDebounce()
    {
        using var store = new ConfigStore(ConfigPath, _time);
        store.Initialize();
        Assert.False(store.TryReload(out _));

        WriteConfig(TestConfigs.DefaultWith("\"g\": 0", "\"g\": 128"));

        // 更新日時のポーリングで検出し、デバウンス後に読み込む。
        _time.Advance(ConfigStore.PollInterval);
        Assert.False(store.TryReload(out _));
        _time.Advance(ConfigStore.Debounce);
        Assert.True(store.TryReload(out var config));

        Assert.Equal(new Rgb(255, 128, 255), config!.Render.KeyColor);
        Assert.Equal(config, store.Current);

        // 同じ内容は二度読まない。
        _time.Advance(ConfigStore.PollInterval + ConfigStore.Debounce);
        Assert.False(store.TryReload(out _));
        _time.Advance(ConfigStore.PollInterval + ConfigStore.Debounce);
        Assert.False(store.TryReload(out _));
    }

    [Fact]
    public void WallClockStepBackwards_DoesNotStopPolling()
    {
        using var store = new ConfigStore(ConfigPath, _time);
        store.Initialize();
        store.TryReload(out _);

        _time.StepWallClock(TimeSpan.FromMinutes(-10));
        WriteConfig(TestConfigs.DefaultWith("\"g\": 0", "\"g\": 99"));

        Assert.True(ReloadNow(store));
        Assert.Equal(99, store.Current.Render.KeyColor.G);
    }

    [Fact]
    public void TryReload_RoundedRectWithNegativeSize_DoesNotThrow()
    {
        using var store = new ConfigStore(ConfigPath, _time);
        store.Initialize();
        var good = store.Current;

        WriteConfig(TestConfigs.DefaultWith("\"widthMeters\": 0.30", "\"widthMeters\": -0.30"));

        Assert.False(ReloadNow(store));
        Assert.Same(good, store.Current);
    }

    [Fact]
    public void TryReload_KeepsPreviousConfigWhenFileBecomesInvalid()
    {
        using var store = new ConfigStore(ConfigPath, _time);
        store.Initialize();
        WriteConfig(TestConfigs.DefaultWith("\"g\": 0", "\"g\": 10"));
        ReloadNow(store);
        var good = store.Current;

        WriteConfig("{ \"keyColor\": ");
        Assert.False(ReloadNow(store));
        Assert.Same(good, store.Current);

        WriteConfig(TestConfigs.DefaultWith("\"widthMeters\": 0.45", "\"widthMeters\": -3"));
        Assert.False(ReloadNow(store));
        Assert.Same(good, store.Current);

        WriteConfig(TestConfigs.DefaultWith("\"g\": 0", "\"g\": 20"));
        Assert.True(ReloadNow(store));
        Assert.Equal(20, store.Current.Render.KeyColor.G);
    }

    [Fact]
    public void TryReload_KeepsCurrentConfigWhenFileIsDeleted()
    {
        using var store = new ConfigStore(ConfigPath, _time);
        store.Initialize();
        var current = store.Current;

        File.Delete(ConfigPath);

        Assert.False(ReloadNow(store));
        Assert.Same(current, store.Current);

        WriteConfig(TestConfigs.DefaultWith("\"g\": 0", "\"g\": 30"));
        Assert.True(ReloadNow(store));
        Assert.Equal(30, store.Current.Render.KeyColor.G);
    }

    [Fact]
    public void RealClock_ReflectsSavedFileWithinOneSecond()
    {
        using var store = new ConfigStore(ConfigPath, TimeProvider.System);
        store.Initialize();
        store.StartWatching();
        Assert.False(store.TryReload(out _));

        WriteConfig(TestConfigs.DefaultWith("\"g\": 0", "\"g\": 64"));
        var saved = DateTime.UtcNow;

        ResolvedConfig? config = null;
        while (DateTime.UtcNow - saved < TimeSpan.FromSeconds(5) && !store.TryReload(out config))
        {
            Thread.Sleep(20);
        }

        Assert.NotNull(config);
        Assert.Equal(64, config!.Render.KeyColor.G);
        Assert.True(DateTime.UtcNow - saved < TimeSpan.FromSeconds(1), $"Took {DateTime.UtcNow - saved}");
    }

    private bool ReloadNow(ConfigStore store)
    {
        _time.Advance(ConfigStore.PollInterval);
        store.TryReload(out _);
        _time.Advance(ConfigStore.Debounce);
        return store.TryReload(out _);
    }

    private void WriteConfig(string text)
    {
        File.WriteAllText(ConfigPath, text);
        // 更新日時の分解能が粗いファイルシステムでも変化が分かるように、日時を進めておく。
        File.SetLastWriteTimeUtc(ConfigPath, DateTime.UtcNow.AddSeconds(Interlocked.Increment(ref _writeCounter)));
    }

    private int _writeCounter;
}
