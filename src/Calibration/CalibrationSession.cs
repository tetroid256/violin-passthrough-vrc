namespace ViolinPassthrough.Calibration;

/// <summary><see cref="CalibrationSession.Update"/> の結果。</summary>
public readonly record struct CalibrationStep(CalibrationStepKind Kind, int SecondsRemaining = 0);

public enum CalibrationStepKind
{
    /// <summary>何もしない。</summary>
    None,

    /// <summary>残り秒数が変わった（通知とビープ音を出す）。</summary>
    Tick,

    /// <summary>カウントダウンが終わった。今の姿勢を取る。</summary>
    Capture,
}

/// <summary>
/// キャリブレーションのカウントダウン（DESIGN.md §15.2）。時間で進むだけで、姿勢の取得や計算はしない。
/// </summary>
public sealed class CalibrationSession
{
    private readonly TimeProvider _time;
    private long? _startedAt;
    private int _seconds;
    private int? _announced;

    public CalibrationSession(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>カウントダウン中か。</summary>
    public bool Active => _startedAt is not null;

    /// <summary>撮るまでの残り時間（カウントダウン中でなければ 0）。</summary>
    public TimeSpan Remaining =>
        _startedAt is { } startedAt
            ? TimeSpan.FromSeconds(Math.Max(0, _seconds - _time.GetElapsedTime(startedAt).TotalSeconds))
            : TimeSpan.Zero;

    public void Start(int seconds)
    {
        _startedAt = _time.GetTimestamp();
        _seconds = Math.Max(1, seconds);
        _announced = null;
    }

    public void Cancel()
    {
        _startedAt = null;
        _announced = null;
    }

    public CalibrationStep Update()
    {
        if (_startedAt is not { } startedAt)
        {
            return new CalibrationStep(CalibrationStepKind.None);
        }

        var elapsed = _time.GetElapsedTime(startedAt).TotalSeconds;
        if (elapsed >= _seconds)
        {
            Cancel();
            return new CalibrationStep(CalibrationStepKind.Capture);
        }

        var remaining = _seconds - (int)Math.Floor(elapsed);
        if (remaining == _announced)
        {
            return new CalibrationStep(CalibrationStepKind.None);
        }

        _announced = remaining;
        return new CalibrationStep(CalibrationStepKind.Tick, remaining);
    }
}
