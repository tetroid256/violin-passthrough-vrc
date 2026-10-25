using ViolinPassthrough.Anchor;

namespace ViolinPassthrough.Mask;

/// <summary>
/// 1つのマスク領域。形状・アンカー・姿勢・サイズを持つ（値オブジェクト）。
/// </summary>
/// <param name="Id">領域 ID。オーバーレイのキーにも使う。</param>
/// <param name="Anchor">基準座標系。</param>
/// <param name="Shape">形状（余白を足す前、<see cref="WidthMeters"/> × <see cref="HeightMeters"/> の箱に収まる）。</param>
/// <param name="Position">アンカー座標系での板の中心位置（メートル）。</param>
/// <param name="RotationEulerDeg">アンカー座標系での板の回転（pitch, yaw, roll。度）。</param>
/// <param name="WidthMeters">形状の幅。</param>
/// <param name="HeightMeters">形状の高さ。</param>
/// <param name="MarginMeters">縁に足す余白。板の実寸は幅・高さに余白を両側分足したものになる。</param>
public sealed record MaskRegion(
    string Id,
    AnchorSpec Anchor,
    MaskShape Shape,
    Vec3 Position,
    Vec3 RotationEulerDeg,
    double WidthMeters,
    double HeightMeters,
    double MarginMeters)
{
    public const string OverlayKeyPrefix = "violinpassthrough.region.";

    public string OverlayKey => OverlayKeyPrefix + Id;

    /// <summary>オーバーレイ（板）の実際の幅。</summary>
    public double QuadWidthMeters => WidthMeters + 2 * MarginMeters;

    /// <summary>オーバーレイ（板）の実際の高さ。</summary>
    public double QuadHeightMeters => HeightMeters + 2 * MarginMeters;

    /// <summary>余白を足した形状。ラスタライズにはこちらを使う。</summary>
    public MaskShape EffectiveShape => Shape.Inflate(MarginMeters);

    public Pose3x4 LocalPose => Pose3x4.FromPositionEuler(Position, RotationEulerDeg);
}
