using System.Diagnostics;
using ViolinPassthrough.Config;
using ViolinPassthrough.Logging;
using ViolinPassthrough.Mask;
using ViolinPassthrough.State;

namespace ViolinPassthrough.App;

/// <summary>約 30Hz の更新ループ（イベント処理・状態反映）。</summary>
public sealed class MainLoop
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(1000.0 / 30.0);

    private readonly IAppLifecycle _lifecycle;
    private readonly IConfigSource _configSource;
    private readonly PassthroughState _state;
    private readonly MaskOverlaySet _overlays;
    private ResolvedConfig _config;
    private int _appliedStateVersion = -1;
    private bool _overlaysDirty = true;

    public MainLoop(IAppLifecycle lifecycle, IConfigSource configSource, PassthroughState state, MaskOverlaySet overlays)
    {
        _lifecycle = lifecycle;
        _configSource = configSource;
        _state = state;
        _overlays = overlays;
        _config = configSource.Current;
        Log.MinLevel = _config.LogLevel;
    }

    /// <summary>起動時の状態を設定から作る。</summary>
    public static PassthroughState CreateInitialState(ResolvedConfig config) =>
        new(config.StartEnabled, config.ActivePreset, config.DebugVisible);

    /// <summary>SteamVR から終了を求められるか、キャンセルされるまで回る。</summary>
    /// <returns>SteamVR から終了を求められたら true。</returns>
    public bool Run(CancellationToken cancellationToken)
    {
        var stopwatch = new Stopwatch();
        while (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Restart();

            if (Tick())
            {
                return true;
            }

            var remaining = TickInterval - stopwatch.Elapsed;
            if (remaining > TimeSpan.Zero && cancellationToken.WaitHandle.WaitOne(remaining))
            {
                break;
            }
        }

        Log.Info("Main loop cancelled.");
        return false;
    }

    /// <summary>1フレーム分の処理。</summary>
    /// <returns>SteamVR から終了を求められたら true。</returns>
    public bool Tick()
    {
        var events = _lifecycle.PollEvents();
        if (events.HasFlag(AppEvents.QuitRequested))
        {
            return true;
        }

        if (_configSource.TryReload(out var config) && config is not null)
        {
            ApplyConfig(config);
        }

        if (_overlaysDirty || _state.Version != _appliedStateVersion)
        {
            SyncOverlays();
        }

        if (_overlays.RequiresTracking)
        {
            _overlays.Tick(null);
        }

        return false;
    }

    private void ApplyConfig(ResolvedConfig next)
    {
        var previous = _config;
        _config = next;
        Log.MinLevel = next.LogLevel;

        // 状態に関わる項目は、ファイル上の値が変わったときだけ反映する（OSC での変更を上書きしないため）。
        if (next.DebugVisible != previous.DebugVisible && _state.SetDebug(next.DebugVisible))
        {
            Log.Info($"Debug display {(next.DebugVisible ? "on" : "off")} (config).");
        }

        if (next.ActivePreset != previous.ActivePreset && _state.SetActivePreset(next.ActivePreset))
        {
            Log.Info($"Preset changed to \"{next.ActivePreset}\" (config).");
        }

        _overlaysDirty = true;
    }

    private void SyncOverlays()
    {
        var preset = _config.FindPreset(_state.ActivePreset);
        if (preset is null)
        {
            Log.Warn($"Preset \"{_state.ActivePreset}\" no longer exists; switching to \"{_config.ActivePreset}\".");
            _state.SetActivePreset(_config.ActivePreset);
            preset = _config.FindPreset(_config.ActivePreset)!;
        }

        var visible = _state.IsVisible
            ? preset.RegionIds.ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        _overlays.Sync(_config.Regions, _config.Render with { Debug = _state.Debug }, visible);

        _appliedStateVersion = _state.Version;
        _overlaysDirty = false;
        Log.Debug($"Overlays synced ({_state}).");
    }
}
