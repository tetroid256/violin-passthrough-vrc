using ViolinPassthrough.Anchor;

namespace ViolinPassthrough.Auto;

/// <summary>自動モード（構えたときだけ表示）の設定（検証済み）。</summary>
/// <param name="Enabled">起動時に自動モードにするか（設定の値。変わったときに状態へ反映する）。</param>
/// <param name="UseLeftHand">左手の位置で判定する。</param>
/// <param name="UseHeadTilt">頭の傾きで判定する。</param>
/// <param name="LeftHandPosition">HMD から見た、構えたときの左手の位置。</param>
/// <param name="HeadUp">HMD から見た、構えたときの「上」の向き。<see cref="UseHeadTilt"/> のときは必須。</param>
/// <param name="RadiusMeters">左手がこの距離以内なら構えている。</param>
/// <param name="HeadTiltToleranceDeg">「上」の向きがこの角度以内なら構えている。</param>
/// <param name="EnterDelaySeconds">構えた状態がこの時間続いたら表示する。</param>
/// <param name="ExitDelaySeconds">外れた状態がこの時間続いたら隠す。</param>
public sealed record AutoSettings(
    bool Enabled,
    bool UseLeftHand,
    bool UseHeadTilt,
    Vec3 LeftHandPosition,
    Vec3? HeadUp,
    double RadiusMeters,
    double HeadTiltToleranceDeg,
    double EnterDelaySeconds,
    double ExitDelaySeconds);

/// <summary>
/// 楽器を構えているかどうかを判定する（DESIGN.md §15.3）。
/// 入るときと出るときで閾値と待ち時間を変えて、判定がばたつかないようにする。
/// </summary>
public sealed class PostureDetector
{
    /// <summary>外れたと判定する距離・角度は、入るときのこの倍。</summary>
    public const double ExitHysteresis = 1.3;

    private readonly TimeProvider _time;
    private long? _changingSince;

    public PostureDetector(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
    }

    public bool InPosture { get; private set; }

    /// <summary>1フレーム分の判定。</summary>
    /// <returns>構えているか（待ち時間を考慮した結果）。</returns>
    public bool Update(AutoSettings settings, TrackingSnapshot tracking)
    {
        var raw = Evaluate(settings, tracking, InPosture);
        if (raw is null || raw == InPosture)
        {
            // 姿勢が取れない間は直前の判定を保つ。待ち時間も数え直す。
            _changingSince = null;
            return InPosture;
        }

        var now = _time.GetTimestamp();
        _changingSince ??= now;
        var delay = raw.Value ? settings.EnterDelaySeconds : settings.ExitDelaySeconds;
        if (_time.GetElapsedTime(_changingSince.Value, now).TotalSeconds >= delay)
        {
            InPosture = raw.Value;
            _changingSince = null;
        }

        return InPosture;
    }

    public void Reset()
    {
        InPosture = false;
        _changingSince = null;
    }

    /// <summary>
    /// 待ち時間を考えない判定。必要な姿勢が取れなければ null。
    /// </summary>
    internal static bool? Evaluate(AutoSettings settings, TrackingSnapshot tracking, bool currentlyInPosture)
    {
        if (tracking.TryGetPose(HmdAnchor.HmdDeviceIndex) is not { IsConnected: true, IsPoseValid: true } hmd)
        {
            return null;
        }

        var toHmd = hmd.DeviceToAbsolute.Inverse();
        var factor = currentlyInPosture ? ExitHysteresis : 1.0;
        var result = true;

        if (settings.UseLeftHand)
        {
            var index = tracking.LeftHandIndex;
            if (index == TrackingSnapshot.InvalidDeviceIndex
                || tracking.TryGetPose(index) is not { IsConnected: true, IsPoseValid: true } hand)
            {
                return null;
            }

            var local = toHmd.TransformPoint(hand.DeviceToAbsolute.Translation);
            result &= (local - settings.LeftHandPosition).Length <= settings.RadiusMeters * factor;
        }

        if (settings.UseHeadTilt && settings.HeadUp is { } reference)
        {
            var up = toHmd.TransformDirection(new Vec3(0, 1, 0));
            result &= Vec3.AngleDeg(up, reference) <= settings.HeadTiltToleranceDeg * factor;
        }

        return result;
    }
}
