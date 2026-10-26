using ViolinPassthrough.Anchor;
using ViolinPassthrough.Mask;

namespace ViolinPassthrough.Calibration;

/// <summary>キャリブレーションの結果。設定ファイルに書き込む値。</summary>
/// <param name="RegionId">置き直した領域。</param>
/// <param name="Position">板の中心（領域のアンカーの座標）。</param>
/// <param name="RotationEulerDeg">板の回転（pitch, yaw, roll。領域のアンカーの座標）。</param>
/// <param name="WidthMeters">板の幅（余白を足す前）。</param>
/// <param name="LeftHandPosition">HMD から見た、自動モードで判定に使うデバイス（既定は左手）の位置（自動モードの基準）。</param>
/// <param name="HeadUp">HMD から見た「上」の向き（自動モードの基準）。</param>
public sealed record CalibrationResult(
    string RegionId,
    Vec3 Position,
    Vec3 RotationEulerDeg,
    double WidthMeters,
    Vec3 LeftHandPosition,
    Vec3 HeadUp);

/// <summary>キャリブレーションの入力（すべて HMD 座標）。</summary>
/// <param name="LeftHand">左手（または左手の代わりのデバイス）の位置。</param>
/// <param name="RightHand">右手（または右手の代わりのデバイス）の位置。</param>
/// <param name="HeadUp">世界の「上」の向き。</param>
/// <param name="AnchorInHmd">デバイス基準の領域のとき、そのデバイスの姿勢。</param>
/// <param name="PostureDevice">自動モードで左手の代わりに使うデバイスの位置。null なら左手の位置を記録する。</param>
public sealed record CalibrationInput(Vec3 LeftHand, Vec3 RightHand, Vec3 HeadUp, Pose3x4? AnchorInHmd, Vec3? PostureDevice = null);

/// <summary>
/// 両手の位置から、領域の板を置き直す（DESIGN.md §15.2）。
/// 目から見た両手の方向の真ん中に、両手までの距離の中間で板を置く。板は視線（両手の方向の二等分線）に垂直で、
/// 両手はそれぞれの視線に沿って板に投影する（目から見て、両手がちょうど板の左右の端の内側に来る）。
/// 弓の手は近く、左手（スクロール側）は遠いのが普通なので、真正面に投影すると近い方の手が板からはみ出すため。
/// </summary>
public static class Calibrator
{
    /// <summary>目から見た両手の方向がこれより近いと、向きが決まらないので失敗にする（度）。</summary>
    public const double MinHandAngleDeg = 4.0;

    /// <summary>目から見た両手の方向がこれより離れていると、板が決まらないので失敗にする（度）。</summary>
    public const double MaxHandAngleDeg = 150.0;

    /// <summary>手が目にこれより近いと、向きが決まらないので失敗にする。</summary>
    public const double MinDistanceFromEyeMeters = 0.1;

    /// <summary>
    /// 計算する。姿勢はすべて同じトラッキング空間（standing など）のもの。HMD 基準の領域だけに使える。
    /// </summary>
    /// <param name="error">失敗したときの理由（通知表示に出す短い英大文字）。</param>
    public static CalibrationResult? Compute(
        Pose3x4 hmd,
        Pose3x4 leftHand,
        Pose3x4 rightHand,
        MaskRegion region,
        double padMeters,
        out string error)
    {
        var toHmd = hmd.Inverse();
        var input = new CalibrationInput(
            toHmd.TransformPoint(leftHand.Translation),
            toHmd.TransformPoint(rightHand.Translation),
            toHmd.TransformDirection(new Vec3(0, 1, 0)),
            AnchorInHmd: null);
        return Compute(input, region, padMeters, out error);
    }

    /// <summary>
    /// HMD 座標の入力から計算する。デバイス基準の領域では、板の姿勢をそのデバイスの座標に直して返す。
    /// </summary>
    /// <param name="error">失敗したときの理由（通知表示に出す短い英大文字）。</param>
    public static CalibrationResult? Compute(CalibrationInput input, MaskRegion region, double padMeters, out string error)
    {
        var left = input.LeftHand;
        var right = input.RightHand;

        if (region.Anchor is WorldAnchorSpec)
        {
            error = "WORLD REGION";
            return null;
        }

        if (region.Anchor is DeviceAnchorSpec && input.AnchorInHmd is null)
        {
            error = "NO ANCHOR DEVICE";
            return null;
        }

        if (left.Length < MinDistanceFromEyeMeters || right.Length < MinDistanceFromEyeMeters)
        {
            error = "TOO CLOSE TO HEAD";
            return null;
        }

        var leftDir = left.Normalized();
        var rightDir = right.Normalized();
        var angle = Vec3.AngleDeg(leftDir, rightDir);
        if (angle < MinHandAngleDeg)
        {
            error = "HANDS TOO CLOSE";
            return null;
        }

        if (angle > MaxHandAngleDeg)
        {
            error = "HANDS TOO FAR APART";
            return null;
        }

        // 視線 = 両手の方向の二等分線。板はこれに垂直で、両手までの距離の中間に置く。
        var view = (leftDir + rightDir).Normalized();
        var distance = ((left + right) * 0.5).Length;

        // 両手をそれぞれの視線に沿って板の面（p・view = distance）に投影する。
        var leftOnPlate = left * (distance / Vec3.Dot(left, view));
        var rightOnPlate = right * (distance / Vec3.Dot(right, view));
        var center = (leftOnPlate + rightOnPlate) * 0.5;
        var across = rightOnPlate - leftOnPlate;

        // 表（+Z）は視点を向き、横方向（+X）は左手 → 右手。
        var normal = -view;
        var x = across.Normalized();
        var y = Vec3.Cross(normal, x);
        var pose = Pose3x4.FromBasis(center, x, y, normal);

        // デバイス基準の領域は、キャリブレーションした瞬間のデバイスから見た姿勢にする
        // （その後はデバイスに追従するので、楽器に付けたトラッカーなら楽器と一緒に動く）。
        if (input.AnchorInHmd is { } anchor && region.Anchor is DeviceAnchorSpec)
        {
            pose = anchor.Inverse() * pose;
        }

        var width = Math.Min(across.Length + 2 * padMeters, Config.ConfigValidator.MaxSizeMeters);

        error = string.Empty;
        return new CalibrationResult(region.Id, pose.Translation, pose.ToEulerDeg(), width, input.PostureDevice ?? left, input.HeadUp.Normalized());
    }
}
