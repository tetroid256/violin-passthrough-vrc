using System.Diagnostics;
using ViolinPassthrough.Anchor;
using ViolinPassthrough.Auto;
using ViolinPassthrough.Calibration;
using ViolinPassthrough.Config;
using ViolinPassthrough.Feedback;
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
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan ResultDuration = TimeSpan.FromSeconds(3);

    private readonly IAppLifecycle _lifecycle;
    private readonly IConfigSource _configSource;
    private readonly PassthroughState _state;
    private readonly MaskOverlaySet _overlays;
    private readonly Func<OscSettings, IOscEndpoint?> _oscFactory;
    private readonly TimeProvider _time;
    private readonly StatusDisplay _status;
    private readonly IBeeper? _beeper;
    private readonly PostureDetector _posture;
    private readonly CalibrationSession _calibration;
    private ResolvedConfig _config;
    private int _appliedStateVersion = -1;
    private bool _overlaysDirty = true;
    private int _seenInvalidCount;

    private IOscEndpoint? _osc;
    private OscSettings? _oscSettings;
    private OscBindings? _bindings;
    private AvatarParameterValues _vrchat = AvatarParameterValues.Unknown;
    private long? _avatarChangedAt;

    // キャリブレーションの結果をデバッグ表示で見せている間（その領域と、終わる時刻）。
    private string? _previewRegionId;
    private long? _previewUntil;

    // キャリブレーションの書き込みによる再読み込みでは「CONFIG RELOADED」を出さない。
    private long? _suppressReloadToastUntil;

    // 1フレームの中で、トラッキング情報は必要になったときに1回だけ取る。
    private TrackingSnapshot? _frameTracking;

    public MainLoop(
        IAppLifecycle lifecycle,
        IConfigSource configSource,
        PassthroughState state,
        MaskOverlaySet overlays,
        Func<OscSettings, IOscEndpoint?> oscFactory,
        TimeProvider? timeProvider = null,
        StatusDisplay? status = null,
        IBeeper? beeper = null)
    {
        _lifecycle = lifecycle;
        _configSource = configSource;
        _state = state;
        _overlays = overlays;
        _oscFactory = oscFactory;
        _time = timeProvider ?? TimeProvider.System;
        _status = status ?? new StatusDisplay(null, _time);
        _beeper = beeper;
        _posture = new PostureDetector(_time);
        _calibration = new CalibrationSession(_time);
        _config = configSource.Current;
        // 起動時から設定が壊れていた（既定の設定で動いている）ときも、最初のフレームで知らせる。
        _seenInvalidCount = 0;
        Log.MinLevel = _config.LogLevel;
        _status.Configure(_config.Feedback, _config.Render.SortOrder, _config.Render.KeyColor);
    }

    /// <summary>キャリブレーションのカウントダウン中か。</summary>
    public bool Calibrating => _calibration.Active;

    /// <summary>キャリブレーションの結果を見せている間か。</summary>
    public bool Previewing => _previewUntil is not null;

    /// <summary>起動時の状態を設定から作る。</summary>
    public static PassthroughState CreateInitialState(ResolvedConfig config) =>
        new(config.StartEnabled, config.ActivePreset, config.DebugVisible, config.Auto.Enabled);

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
        _frameTracking = null;
        var events = _lifecycle.PollEvents();
        if (events.HasFlag(AppEvents.QuitRequested))
        {
            return true;
        }

        if (_configSource.TryReload(out var config) && config is not null)
        {
            ApplyConfig(config);
        }

        if (_configSource.InvalidCount != _seenInvalidCount)
        {
            _seenInvalidCount = _configSource.InvalidCount;
            _status.Show("CONFIG ERROR", StatusKind.Error, ResultDuration);
        }

        EnsureOsc();
        ReceiveOsc();
        UpdateCalibration();
        UpdatePosture();
        UpdatePreview();

        if (_overlaysDirty || _state.Version != _appliedStateVersion)
        {
            SyncOverlays();
        }

        SendStateToVrchat();

        if (_overlays.RequiresTracking)
        {
            _overlays.Tick(Tracking);
        }

        _status.Update();
        return false;
    }

    public void Dispose()
    {
        _osc?.Dispose();
        _osc = null;
        _status.Dispose();
    }

    private TrackingSnapshot Tracking => _frameTracking ??= _lifecycle.GetTrackingSnapshot();

    private void ApplyConfig(ResolvedConfig next)
    {
        var previous = _config;
        _config = next;
        Log.MinLevel = next.LogLevel;
        _status.Configure(next.Feedback, next.Render.SortOrder, next.Render.KeyColor);

        // 状態に関わる項目は、ファイル上の値が変わったときだけ反映する（OSC での変更を上書きしないため）。
        if (next.DebugVisible != previous.DebugVisible && _state.SetDebug(next.DebugVisible))
        {
            Log.Info($"Debug display {(next.DebugVisible ? "on" : "off")} (config).");
        }

        if (next.ActivePreset != previous.ActivePreset && _state.SetActivePreset(next.ActivePreset))
        {
            Log.Info($"Preset changed to \"{next.ActivePreset}\" (config).");
        }

        if (next.Auto.Enabled != previous.Auto.Enabled && _state.SetAuto(next.Auto.Enabled))
        {
            Log.Info($"Auto mode {(next.Auto.Enabled ? "on" : "off")} (config).");
        }

        // キャリブレーションの結果を書き込んだときは、結果の通知を上書きしない。
        if (_suppressReloadToastUntil is { } until && _time.GetTimestamp() < until)
        {
            _suppressReloadToastUntil = null;
        }
        else
        {
            _status.Show("CONFIG RELOADED", StatusKind.Success, ToastDuration);
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

            var before = (_state.Enabled, _state.Auto, _state.ActivePreset);
            switch (_bindings.Apply(message, _state, _config, ref _vrchat))
            {
                case OscApplyResult.CalibrationRequested:
                    StartCalibration();
                    break;
                case OscApplyResult.CalibrationCancelled:
                    CancelCalibration();
                    break;
                case OscApplyResult.StateChanged:
                    ShowStateToast(before);
                    break;
            }
        }
    }

    /// <summary>OSC で変わった状態を通知表示で知らせる（窓の見た目で分からないものだけ）。</summary>
    private void ShowStateToast((bool Enabled, bool Auto, string Preset) before)
    {
        if (_state.Auto != before.Auto)
        {
            _status.Show(_state.Auto ? "AUTO ON" : "AUTO OFF", StatusKind.Info, ToastDuration);
        }
        else if (_state.ActivePreset != before.Preset)
        {
            // フォントに無い文字（日本語など）を含む名前は、番号で出す。
            var name = BitmapFont.IsSupported(_state.ActivePreset)
                ? _state.ActivePreset.ToUpperInvariant()
                : _config.IndexOfPreset(_state.ActivePreset).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _status.Show("PRESET " + name, StatusKind.Info, ToastDuration);
        }
        else if (_state.Enabled != before.Enabled && _state.Auto)
        {
            // 自動モードでは、構えるまで窓が出ないので知らせる。
            _status.Show(_state.Enabled ? "ON - RAISE TO SHOW" : "OFF", StatusKind.Info, ToastDuration);
        }
    }

    private void StartCalibration()
    {
        if (_calibration.Active)
        {
            return;
        }

        Log.Info($"Calibration started ({_config.Calibration.CountdownSeconds} s countdown).");
        _calibration.Start(_config.Calibration.CountdownSeconds);
    }

    private void CancelCalibration()
    {
        if (!_calibration.Active)
        {
            return;
        }

        _calibration.Cancel();
        Log.Info("Calibration cancelled.");
        _status.Show("CAL CANCELLED", StatusKind.Error, ResultDuration, isToast: false);
        Beep(BeepKind.Error);
    }

    private void UpdateCalibration()
    {
        var step = _calibration.Update();
        switch (step.Kind)
        {
            case CalibrationStepKind.Tick:
                _status.Show($"CALIBRATE {step.SecondsRemaining}", StatusKind.Info, TimeSpan.FromSeconds(1.5), isToast: false);
                Beep(BeepKind.Tick);
                break;

            case CalibrationStepKind.Capture:
                Capture();
                break;
        }
    }

    private void Capture()
    {
        var settings = _config.Calibration;
        var region = _config.Regions.FirstOrDefault(r => r.Id == settings.RegionId);
        if (region is null)
        {
            FailCalibration("REGION NOT FOUND", $"region \"{settings.RegionId}\" is not defined");
            return;
        }

        if (region.Anchor is not HmdAnchorSpec)
        {
            FailCalibration("NOT HMD REGION", $"region \"{settings.RegionId}\" is not hmd-anchored");
            return;
        }

        var tracking = Tracking;
        if (ValidPose(tracking, HmdAnchor.HmdDeviceIndex) is not { } hmd)
        {
            FailCalibration("NO HMD", "the HMD pose is not valid");
            return;
        }

        if (ValidPose(tracking, tracking.LeftHandIndex) is not { } left)
        {
            FailCalibration("NO LEFT HAND", "the left hand is not tracked");
            return;
        }

        if (ValidPose(tracking, tracking.RightHandIndex) is not { } right)
        {
            FailCalibration("NO RIGHT HAND", "the right hand is not tracked");
            return;
        }

        var result = Calibrator.Compute(hmd, left, right, region, settings.PadMeters, out var error);
        if (result is null)
        {
            FailCalibration(error, error.ToLowerInvariant());
            return;
        }

        if (!_configSource.TryApplyCalibration(result, out error))
        {
            FailCalibration(error, error.ToLowerInvariant());
            return;
        }

        Log.Info($"Calibrated \"{result.RegionId}\": position {Format(result.Position)}, rotation {Format(result.RotationEulerDeg)}, width {result.WidthMeters:0.000} m.");
        _status.Show("CALIBRATED", StatusKind.Success, ResultDuration, isToast: false);
        _suppressReloadToastUntil = _time.GetTimestamp() + (long)(ResultDuration.TotalSeconds * _time.TimestampFrequency);
        Beep(BeepKind.Success);

        // 結果をデバッグ表示で見せる（無効の状態でも見せる）。
        if (settings.PreviewSeconds > 0)
        {
            _previewRegionId = result.RegionId;
            _previewUntil = _time.GetTimestamp() + (long)(settings.PreviewSeconds * _time.TimestampFrequency);
            _overlaysDirty = true;
        }
    }

    private void FailCalibration(string message, string detail)
    {
        Log.Warn($"Calibration failed: {detail}.");
        _status.Show("CAL FAIL: " + message, StatusKind.Error, ResultDuration, isToast: false);
        Beep(BeepKind.Error);
    }

    private void UpdatePosture()
    {
        // 自動モードで有効なときだけ判定する（毎フレームのトラッキング情報の取得を減らすため）。
        if (!_state.Auto || !_state.Enabled)
        {
            _posture.Reset();
            _state.SetInPosture(false);
            return;
        }

        var inPosture = _posture.Update(_config.Auto, Tracking);
        if (_state.SetInPosture(inPosture))
        {
            Log.Info(inPosture ? "Playing posture detected." : "Playing posture ended.");
        }
    }

    private void UpdatePreview()
    {
        if (_previewUntil is { } until && _time.GetTimestamp() >= until)
        {
            _previewUntil = null;
            _previewRegionId = null;
            _overlaysDirty = true;
        }
    }

    private void SendStateToVrchat()
    {
        if (_osc is null || _bindings is null)
        {
            return;
        }

        var calibrating = _calibration.Active;
        var messages = _bindings.BuildSyncMessages(_state, _config, _vrchat, calibrating);
        if (messages.Count == 0)
        {
            return;
        }

        foreach (var message in messages)
        {
            _osc.Send(message);
        }

        _vrchat = OscBindings.ValuesOf(_state, _config, calibrating);
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
        var debug = _state.Debug;
        if (_previewRegionId is not null)
        {
            // キャリブレーションした領域は、プリセットに入っていなくても見せる。
            visible.Add(_previewRegionId);
            debug = true;
        }

        _overlays.Sync(_config.Regions, _config.Render with { Debug = debug }, visible);

        _appliedStateVersion = _state.Version;
        _overlaysDirty = false;
        Log.Debug($"Overlays synced ({_state}, preview={_previewRegionId ?? "none"}).");
    }

    private void Beep(BeepKind kind)
    {
        if (_config.Calibration.Beep)
        {
            _beeper?.Beep(kind);
        }
    }

    private static Pose3x4? ValidPose(TrackingSnapshot tracking, uint index) =>
        index != TrackingSnapshot.InvalidDeviceIndex
        && tracking.TryGetPose(index) is { IsConnected: true, IsPoseValid: true } pose
            ? pose.DeviceToAbsolute
            : null;

    private static string Format(Vec3 v) =>
        FormattableString.Invariant($"[{v.X:0.000}, {v.Y:0.000}, {v.Z:0.000}]");
}
