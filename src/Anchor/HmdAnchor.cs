namespace ViolinPassthrough.Anchor;

/// <summary>
/// HMD 基準。HMD に対する相対姿勢で固定するので、毎フレームの更新は不要。
/// </summary>
public sealed class HmdAnchor : IAnchor
{
    /// <summary>OpenVR の k_unTrackedDeviceIndex_Hmd。</summary>
    public const uint HmdDeviceIndex = 0;

    public bool RequiresTracking => false;

    public AnchorPlacement Resolve(Pose3x4 localPose, TrackingSnapshot? tracking) =>
        new AnchorPlacement.DeviceRelative(HmdDeviceIndex, localPose);
}
