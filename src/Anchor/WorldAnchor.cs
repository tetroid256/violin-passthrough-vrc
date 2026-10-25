namespace ViolinPassthrough.Anchor;

/// <summary>
/// ルーム空間に固定する。位置と回転はトラッキング空間（standing / seated）の座標。毎フレームの更新は不要。
/// </summary>
public sealed class WorldAnchor : IAnchor
{
    public WorldAnchor(TrackingUniverse universe)
    {
        Universe = universe;
    }

    public TrackingUniverse Universe { get; }

    public bool RequiresTracking => false;

    public AnchorPlacement Resolve(Pose3x4 localPose, TrackingSnapshot? tracking) =>
        new AnchorPlacement.Absolute(Universe, localPose);
}
