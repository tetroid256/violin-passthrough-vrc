using ViolinPassthrough.Anchor;
using ViolinPassthrough.Mask;

namespace ViolinPassthrough.Calibration;

/// <summary>キャリブレーションの結果（HMD 座標）。設定ファイルに書き込む値。</summary>
/// <param name="RegionId">置き直した領域。</param>
/// <param name="Position">板の中心。</param>
/// <param name="RotationEulerDeg">板の回転（pitch, yaw, roll）。</param>
/// <param name="WidthMeters">板の幅（余白を足す前）。</param>
/// <param name="LeftHandPosition">HMD から見た左手の位置（自動モードの基準）。</param>
/// <param name="HeadUp">HMD から見た「上」の向き（自動モードの基準）。</param>
public sealed record CalibrationResult(
    string RegionId,
    Vec3 Position,
    Vec3 RotationEulerDeg,
    double WidthMeters,
    Vec3 LeftHandPosition,
    Vec3 HeadUp);

/// <summary>
/// 両手の位置から、領域の板を置き直す（DESIGN.md §15.2）。
/// 板の中心は両手の中点、表は視点を向き、横方向は左手 → 右手。幅は両手の距離 + 両端の余裕。
/// </summary>
public static class Calibrator
{
    /// <summary>両手がこれより近いと、向きが決まらないので失敗にする。</summary>
    public const double MinHandDistanceMeters = 0.08;

    /// <summary>板の中心が視点にこれより近いと、表の向きが決まらないので失敗にする。</summary>
    public const double MinDistanceFromEyeMeters = 0.1;

    /// <summary>
    /// 計算する。姿勢はすべて同じトラッキング空間（standing など）のもの。
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
        var left = toHmd.TransformPoint(leftHand.Translation);
        var right = toHmd.TransformPoint(rightHand.Translation);
        var center = (left + right) * 0.5;

        if (center.Length < MinDistanceFromEyeMeters)
        {
            error = "HANDS TOO CLOSE TO HEAD";
            return null;
        }

        // 表（+Z）は視点（原点）を向く。
        var normal = (-center).Normalized();

        // 横方向（+X）は左手 → 右手を板の面に投影したもの。
        var across = right - left;
        var inPlane = across - normal * Vec3.Dot(across, normal);
        var length = inPlane.Length;
        if (length < MinHandDistanceMeters)
        {
            error = "HANDS TOO CLOSE";
            return null;
        }

        var x = inPlane.Normalized();
        var y = Vec3.Cross(normal, x);
        var pose = Pose3x4.FromBasis(center, x, y, normal);

        var width = Math.Min(length + 2 * padMeters, Config.ConfigValidator.MaxSizeMeters);

        // HMD から見た「上」= 世界の上（+Y）を HMD 座標に直したもの。
        var headUp = toHmd.TransformDirection(new Vec3(0, 1, 0));

        error = string.Empty;
        return new CalibrationResult(region.Id, center, pose.ToEulerDeg(), width, left, headUp);
    }
}
