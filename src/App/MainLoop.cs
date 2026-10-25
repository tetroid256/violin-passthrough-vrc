using System.Diagnostics;
using ViolinPassthrough.Config;
using ViolinPassthrough.Logging;
using ViolinPassthrough.Mask;
using ViolinPassthrough.Osc;
using ViolinPassthrough.State;

namespace ViolinPassthrough.App;

/// <summary>約 30Hz の更新ループ（イベント処理・状態反映）。</summary>
public sealed class MainLoop : IDisposable
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(1000.0 / 30.0);

    /// <summary>
    /// アバター変更の直後は、VRChat が新しいアバターの既定値を送ってくるので受け取らない。
    /// この時間が過ぎたら、現在の状態をもう一度送る。
    /// </summary>
    public static readonly TimeSpan AvatarChangeGrace = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan ErrorLogInterval = TimeSpan.FromSeconds(5);

    private readonly IAppLifecycle _lifecycle;
    private readonly IConfigSource _configSource;
    private readonly PassthroughState _state;
    private readonly MaskOverlaySet _overlays;
    private readonly Func<OscSettings, IOscEndpoint?> _oscFactory;
    private readonly TimeProvider _time;
    private ResolvedConfig _config;
    private int _appliedStateVersion = -1;
    private bool _overlaysDirty = true;

    private IOscEndpoint? _osc;
    private OscSettings? _oscSettings;
    private OscBindings? _bindings;
    private AvatarParameterValues _vrchat = AvatarParameterValues.Unknown;
    private long? _avatarChangedAt;

    public MainLoop(
        IAppLifecycle lifecycle,
        IConfigSource configSource,
        PassthroughState state,
        MaskOverlaySet overlays,
        Func<OscSettings, IOscEndpoint?> oscFactory,
        TimeProvider? timeProvider = null)
    {
        _lifecycle = lifecycle;
        _configSource = configSource;
        _state = state;
        _overlays = overlays;
        _oscFactory = oscFactory;
        _time = timeProvider ?? TimeProvider.System;
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
        long? lastErrorLoggedAt = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Restart();

            try
            {
                if (Tick())
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                // 常駐アプリなので、想定外の例外でも落とさずに次のフレームへ進む。同じ失敗が続いてもログは数秒に1回。
                var now = _time.GetTimestamp();
                if (lastErrorLoggedAt is not { } last || _time.GetElapsedTime(last, now) >= ErrorLogInterval)
                {
                    Log.Error("Unexpected error in the main loop", ex);
                    lastErrorLoggedAt = now;
                }
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

        EnsureOsc();
        ReceiveOsc();

        if (_overlaysDirty || _state.Version != _appliedStateVersion)
        {
            SyncOverlays();
        }

        SendStateToVrchat();

        if (_overlays.RequiresTracking)
        {
            _overlays.Tick(null);
        }

        return false;
    }

    public void Dispose()
    {
        _osc?.Dispose();
        _osc = null;
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

    private void EnsureOsc()
    {
        if (_oscSettings is not null && _oscSettings == _config.Osc)
        {
            return;
        }

        if (_osc is not null)
        {
            Log.Info("OSC settings changed; restarting OSC.");
            _osc.Dispose();
        }

        _oscSettings = _config.Osc;
        _bindings = new OscBindings(_oscSettings);
        _osc = _oscFactory(_oscSettings);
        // 起動時（と設定変更時）は、VRChat に現在の状態を送ってメニューの表示を合わせる。
        _vrchat = AvatarParameterValues.Unknown;
        _avatarChangedAt = null;
    }

    private void ReceiveOsc()
    {
        if (_osc is null || _bindings is null)
        {
            return;
        }

        var now = _time.GetTimestamp();
        if (_avatarChangedAt is { } changedAt && _time.GetElapsedTime(changedAt, now) >= AvatarChangeGrace)
        {
            // 新しいアバターの初期化が終わったころなので、もう一度送る。
            _avatarChangedAt = null;
            _vrchat = AvatarParameterValues.Unknown;
        }

        while (_osc.TryReceive(out var message))
        {
            if (message.Address == OscBindings.AvatarChangeAddress)
            {
                _bindings.Apply(message, _state, _config, ref _vrchat);
                _vrchat = AvatarParameterValues.Unknown;
                _avatarChangedAt = now;
                continue;
            }

            if (_avatarChangedAt is not null)
            {
                Log.Debug($"Ignored during avatar change: {message}");
                continue;
            }

            _bindings.Apply(message, _state, _config, ref _vrchat);
        }
    }

    private void SendStateToVrchat()
    {
        if (_osc is null || _bindings is null)
        {
            return;
        }

        var messages = _bindings.BuildSyncMessages(_state, _config, _vrchat);
        if (messages.Count == 0)
        {
            return;
        }

        foreach (var message in messages)
        {
            _osc.Send(message);
        }

        _vrchat = OscBindings.ValuesOf(_state, _config);
        Log.Debug($"Sent state to VRChat ({_state}).");
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
