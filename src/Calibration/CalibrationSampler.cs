using ViolinPassthrough.Anchor;

namespace ViolinPassthrough.Calibration;

/// <summary>
/// カウントダウンの最後の <see cref="Window"/> の間、手の位置を HMD 座標で集める（DESIGN.md §16.3）。
/// 最後の瞬間だけを使うと、その瞬間にハンドトラッキングが外れていたら失敗し、手の震えもそのまま入るため。
/// 平均すれば、窓の間に一度でも見えていた手は使え、細かい揺れも均される。
/// </summary>
public sealed class CalibrationSampler
{
    /// <summary>集める時間（カウントダウンの最後）。</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    private Vec3 _leftSum;
    private int _leftCount;
    private Vec3 _rightSum;
    private int _rightCount;
    private Vec3 _postureSum;
    private int _postureCount;
    private Vec3 _upSum;
    private int _hmdCount;
    private Pose3x4? _anchor;

    /// <summary>集めたもの（HMD が見えていたフレームの数）。</summary>
    public int HmdSamples => _hmdCount;

    public int LeftSamples => _leftCount;

    public int RightSamples => _rightCount;

    public void Clear()
    {
        _leftSum = _rightSum = _postureSum = _upSum = default;
        _leftCount = _rightCount = _postureCount = _hmdCount = 0;
        _anchor = null;
    }

    /// <summary>1フレーム分を加える。</summary>
    /// <param name="anchor">デバイス基準の領域をキャリブレーションするとき、そのアンカー。</param>
    /// <param name="postureSerial">自動モードで左手の代わりに使うデバイス（<c>auto.deviceSerial</c>）。</param>
    public void Add(TrackingSnapshot tracking, HandDevices hands, DeviceAnchorSpec? anchor, string? postureSerial = null)
    {
        if (ValidPose(tracking, HmdAnchor.HmdDeviceIndex) is not { } hmd)
        {
            // HMD の姿勢が無いと HMD 座標にできないので、このフレームは使わない。
            return;
        }

        var toHmd = hmd.Inverse();
        _hmdCount++;
        _upSum += toHmd.TransformDirection(new Vec3(0, 1, 0));

        if (ValidPose(tracking, hands.LeftIndex(tracking)) is { } left)
        {
            _leftSum += toHmd.TransformPoint(left.Translation);
            _leftCount++;
        }

        if (ValidPose(tracking, hands.RightIndex(tracking)) is { } right)
        {
            _rightSum += toHmd.TransformPoint(right.Translation);
            _rightCount++;
        }

        if (postureSerial is not null
            && tracking.SerialToIndex.TryGetValue(postureSerial, out var postureIndex)
            && ValidPose(tracking, postureIndex) is { } posture)
        {
            _postureSum += toHmd.TransformPoint(posture.Translation);
            _postureCount++;
        }

        // アンカーの向きは平均できないので、最後に見えていたときのものを使う。
        if (anchor is not null && ValidPose(tracking, HandDevices.AnchorIndex(tracking, anchor)) is { } device)
        {
            _anchor = toHmd * device;
        }
    }

    /// <summary>集めたものから入力を作る。足りなければ null と理由（通知表示に出す短い英大文字）。</summary>
    /// <param name="hands">失敗の理由を、手かトラッカーかで分けるため。</param>
    /// <param name="needsAnchor">デバイス基準の領域か。</param>
    /// <param name="postureSerial"><see cref="Add"/> に渡したもの。</param>
    public CalibrationInput? TryGetInput(HandDevices hands, bool needsAnchor, out string error, string? postureSerial = null)
    {
        if (_hmdCount == 0)
        {
            error = "NO HMD";
            return null;
        }

        if (_leftCount == 0)
        {
            error = hands.LeftHandSerial is null ? "NO LEFT HAND" : "NO LEFT TRACKER";
            return null;
        }

        if (_rightCount == 0)
        {
            error = hands.RightHandSerial is null ? "NO RIGHT HAND" : "NO RIGHT TRACKER";
            return null;
        }

        if (needsAnchor && _anchor is null)
        {
            error = "NO ANCHOR DEVICE";
            return null;
        }

        if (postureSerial is not null && _postureCount == 0)
        {
            error = "NO AUTO TRACKER";
            return null;
        }

        error = string.Empty;
        return new CalibrationInput(
            _leftSum * (1.0 / _leftCount),
            _rightSum * (1.0 / _rightCount),
            (_upSum * (1.0 / _hmdCount)).Normalized(),
            needsAnchor ? _anchor : null,
            postureSerial is not null ? _postureSum * (1.0 / _postureCount) : null);
    }

    private static Pose3x4? ValidPose(TrackingSnapshot tracking, uint index) =>
        index != TrackingSnapshot.InvalidDeviceIndex
        && tracking.TryGetPose(index) is { IsConnected: true, IsPoseValid: true } pose
            ? pose.DeviceToAbsolute
            : null;
}
