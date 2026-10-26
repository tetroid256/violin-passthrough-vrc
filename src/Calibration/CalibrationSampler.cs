using ViolinPassthrough.Anchor;

namespace ViolinPassthrough.Calibration;

/// <summary>キャリブレーションで姿勢を集めるデバイス（設定から決まる）。</summary>
/// <param name="Hands">左右の手として使うデバイス。</param>
/// <param name="Anchor">デバイス基準の領域をキャリブレーションするとき、そのアンカー。</param>
/// <param name="PostureSerial">自動モードで左手の代わりに使うデバイス（<c>auto.deviceSerial</c>）。</param>
public sealed record CalibrationDevices(HandDevices Hands, DeviceAnchorSpec? Anchor, string? PostureSerial);

/// <summary>
/// カウントダウンの最後の <see cref="Window"/> の間、手の位置を HMD 座標で集める（DESIGN.md §16.3）。
/// 最後の瞬間だけを使うと、その瞬間にハンドトラッキングが外れていたら失敗し、手の震えもそのまま入るため。
/// 平均すれば、窓の間に一度でも見えていた手は使え、細かい揺れも均される。
/// </summary>
public sealed class CalibrationSampler
{
    /// <summary>集める時間（カウントダウンの最後）。</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    private CalibrationDevices? _devices;
    private Vec3 _leftSum;
    private Vec3 _rightSum;
    private Vec3 _postureSum;
    private Vec3 _upSum;
    private Pose3x4? _anchor;

    /// <summary>
    /// 集める時間。カウントダウンが短いときは、その半分にする
    /// （メニューを操作した直後の、構える前の位置を混ぜないため）。
    /// </summary>
    public static TimeSpan WindowFor(int countdownSeconds) =>
        TimeSpan.FromSeconds(Math.Min(Window.TotalSeconds, Math.Max(1, countdownSeconds) / 2.0));

    /// <summary>集めたもの（それぞれのデバイスが見えていたフレームの数）。</summary>
    public int HmdSamples { get; private set; }

    public int LeftSamples { get; private set; }

    public int RightSamples { get; private set; }

    public int PostureSamples { get; private set; }

    public int AnchorSamples { get; private set; }

    public void Clear()
    {
        _devices = null;
        _leftSum = _rightSum = _postureSum = _upSum = default;
        HmdSamples = LeftSamples = RightSamples = PostureSamples = AnchorSamples = 0;
        _anchor = null;
    }

    /// <summary>1フレーム分を加える。</summary>
    /// <param name="devices">集めるデバイス。途中で変わったら（設定の再読み込み）、それまでの分は捨てる（違うデバイスの位置を混ぜないため）。</param>
    public void Add(TrackingSnapshot tracking, CalibrationDevices devices)
    {
        if (_devices != devices)
        {
            Clear();
            _devices = devices;
        }

        if (ValidPose(tracking, HmdAnchor.HmdDeviceIndex) is not { } hmd)
        {
            // HMD の姿勢が無いと HMD 座標にできないので、このフレームは使わない。
            return;
        }

        var toHmd = hmd.Inverse();
        HmdSamples++;
        _upSum += toHmd.TransformDirection(new Vec3(0, 1, 0));

        if (ValidPose(tracking, devices.Hands.LeftIndex(tracking)) is { } left)
        {
            _leftSum += toHmd.TransformPoint(left.Translation);
            LeftSamples++;
        }

        if (ValidPose(tracking, devices.Hands.RightIndex(tracking)) is { } right)
        {
            _rightSum += toHmd.TransformPoint(right.Translation);
            RightSamples++;
        }

        if (devices.PostureSerial is { } serial
            && tracking.SerialToIndex.TryGetValue(serial, out var postureIndex)
            && ValidPose(tracking, postureIndex) is { } posture)
        {
            _postureSum += toHmd.TransformPoint(posture.Translation);
            PostureSamples++;
        }

        // アンカーの向きは平均できないので、最後に見えていたときのものを使う。
        if (devices.Anchor is { } anchor && ValidPose(tracking, HandDevices.AnchorIndex(tracking, anchor)) is { } device)
        {
            _anchor = toHmd * device;
            AnchorSamples++;
        }
    }

    /// <summary>集めたものから入力を作る。足りなければ null と理由（通知表示に出す短い英大文字）。</summary>
    /// <param name="requirePosture">
    /// <c>auto.deviceSerial</c> のデバイスが必要か（自動モードが位置で判定するとき）。必要でなく見えていなければ、
    /// 自動モードの基準は記録しない（左手の位置を記録すると、そのデバイスの基準として使われてしまうため）。
    /// </param>
    public CalibrationInput? TryGetInput(bool requirePosture, out string error)
    {
        var devices = _devices;
        if (devices is null || HmdSamples == 0)
        {
            error = "NO HMD";
            return null;
        }

        if (LeftSamples == 0)
        {
            error = devices.Hands.LeftHandSerial is null ? "NO LEFT HAND" : "NO LEFT TRACKER";
            return null;
        }

        if (RightSamples == 0)
        {
            error = devices.Hands.RightHandSerial is null ? "NO RIGHT HAND" : "NO RIGHT TRACKER";
            return null;
        }

        if (devices.Anchor is not null && _anchor is null)
        {
            error = "NO ANCHOR DEVICE";
            return null;
        }

        if (requirePosture && devices.PostureSerial is not null && PostureSamples == 0)
        {
            error = "NO AUTO TRACKER";
            return null;
        }

        var leftHand = _leftSum * (1.0 / LeftSamples);
        Vec3? autoReference = devices.PostureSerial is null
            ? leftHand
            : PostureSamples > 0 ? _postureSum * (1.0 / PostureSamples) : null;

        error = string.Empty;
        return new CalibrationInput(
            leftHand,
            _rightSum * (1.0 / RightSamples),
            (_upSum * (1.0 / HmdSamples)).Normalized(),
            _anchor,
            autoReference);
    }

    private static Pose3x4? ValidPose(TrackingSnapshot tracking, uint index) =>
        index != TrackingSnapshot.InvalidDeviceIndex
        && tracking.TryGetPose(index) is { IsConnected: true, IsPoseValid: true } pose
            ? pose.DeviceToAbsolute
            : null;
}
