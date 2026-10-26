using ViolinPassthrough.Anchor;

namespace ViolinPassthrough.Auto;

/// <summary>自動モード（構えたときだけ表示）の設定（検証済み）。</summary>
/// <param name="Enabled">起動時に自動モードにするか（設定の値。変わったときに状態へ反映する）。</param>
/// <param name="UseLeftHand">左手（または <see cref="DeviceSerial"/> のデバイス）の位置で判定する。</param>
/// <param name="UseHeadTilt">頭の傾きで判定する。</param>
/// <param name="LeftHandPosition">HMD から見た、構えたときの左手（または <see cref="DeviceSerial"/> のデバイス）の位置。</param>
/// <param name="HeadUp">HMD から見た、構えたときの「上」の向き。<see cref="UseHeadTilt"/> のときは必須。</param>
/// <param name="RadiusMeters">左手（または <see cref="DeviceSerial"/> のデバイス）がこの距離以内なら構えている。</param>
/// <param name="HeadTiltToleranceDeg">「上」の向きがこの角度以内なら構えている。</param>
/// <param name="EnterDelaySeconds">構えた状態がこの時間続いたら表示する。</param>
/// <param name="ExitDelaySeconds">外れた状態がこの時間続いたら隠す。</param>
/// <param name="DeviceSerial">左手の代わりに位置で判定するデバイス（楽器に付けたトラッカーなど）。null なら左手。</param>
public sealed record AutoSettings(
    bool Enabled,
    bool UseLeftHand,
    bool UseHeadTilt,
    Vec3 LeftHandPosition,
    Vec3? HeadUp,
    double RadiusMeters,
    double HeadTiltToleranceDeg,
    double EnterDelaySeconds,
    double ExitDelaySeconds,
    string? DeviceSerial = null);

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
    /// <param name="hands">左手として使うデバイス。null なら左手のロールのデバイス。</param>
    public bool Update(AutoSettings settings, TrackingSnapshot tracking, HandDevices? hands = null)
    {
        var raw = Evaluate(settings, tracking, InPosture, hands);
        if (raw is null)
        {
            // 姿勢が取れない間は直前の判定を保つ。ただし、外れ始めていたら（楽器を下ろした直後に手が
            // カメラの外に出た場合など）その待ち時間は数え続け、時間が来たら隠す。
            if (InPosture && _changingSince is { } since
                && _time.GetElapsedTime(since).TotalSeconds >= settings.ExitDelaySeconds)
            {
                InPosture = false;
                _changingSince = null;
            }
            else if (!InPosture)
            {
                _changingSince = null;
            }

            return InPosture;
        }

        if (raw == InPosture)
        {
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

    /// <summary>位置で判定するデバイス。見つからなければ <see cref="TrackingSnapshot.InvalidDeviceIndex"/>。</summary>
    public static uint PostureDeviceIndex(AutoSettings settings, TrackingSnapshot tracking, HandDevices? hands = null) =>
        settings.DeviceSerial is { } serial
            ? tracking.SerialToIndex.TryGetValue(serial, out var index) ? index : TrackingSnapshot.InvalidDeviceIndex
            : (hands ?? HandDevices.Default).LeftIndex(tracking);

    public void Reset()
    {
        InPosture = false;
        _changingSince = null;
    }

    /// <summary>
    /// 待ち時間を考えない判定。必要な姿勢が取れなければ null。
    /// 左手を見失っていても、頭の傾きの条件を使っていれば頭の傾きだけで決める。
    /// </summary>
    internal static bool? Evaluate(AutoSettings settings, TrackingSnapshot tracking, bool currentlyInPosture, HandDevices? hands = null)
    {
        if (tracking.TryGetPose(HmdAnchor.HmdDeviceIndex) is not { IsConnected: true, IsPoseValid: true } hmd)
        {
            return null;
        }

        var toHmd = hmd.DeviceToAbsolute.Inverse();
        var factor = currentlyInPosture ? ExitHysteresis : 1.0;
        var result = true;

        var useHeadTilt = settings.UseHeadTilt && settings.HeadUp is not null;
        if (settings.UseLeftHand)
        {
            var index = PostureDeviceIndex(settings, tracking, hands);
            if (index != TrackingSnapshot.InvalidDeviceIndex
                && tracking.TryGetPose(index) is { IsConnected: true, IsPoseValid: true } hand)
            {
                var local = toHmd.TransformPoint(hand.DeviceToAbsolute.Translation);
                result &= (local - settings.LeftHandPosition).Length <= settings.RadiusMeters * factor;
            }
            else if (!useHeadTilt)
            {
                return null;
            }
        }

        if (useHeadTilt && settings.HeadUp is { } reference)
        {
            var up = toHmd.TransformDirection(new Vec3(0, 1, 0));
            result &= Vec3.AngleDeg(up, reference) <= settings.HeadTiltToleranceDeg * factor;
        }

        return result;
    }
}
