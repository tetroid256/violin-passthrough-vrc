namespace ViolinPassthrough.Anchor;

/// <summary>
/// マスク領域の基準座標系。「どのデバイス基準で、どの相対姿勢に置くか」を返す。
/// OpenVR を直接呼ばない（必要なトラッキング情報は <see cref="TrackingSnapshot"/> で受け取る）。
/// </summary>
public interface IAnchor
{
    /// <summary>true なら毎フレーム <see cref="TrackingSnapshot"/> を渡して <see cref="Resolve"/> を呼ぶ必要がある。</summary>
    bool RequiresTracking { get; }

    /// <summary>
    /// 領域の姿勢（アンカー座標系での位置・回転）から、OpenVR への配置方法を決める。
    /// </summary>
    AnchorPlacement Resolve(Pose3x4 localPose, TrackingSnapshot? tracking);
}

/// <summary>OpenVR のトラッキング空間。</summary>
public enum TrackingUniverse
{
    Standing,
    Seated,
}

/// <summary>オーバーレイをどう置くか。</summary>
public abstract record AnchorPlacement
{
    private AnchorPlacement()
    {
    }

    /// <summary>トラッキングデバイス基準の相対姿勢（SetOverlayTransformTrackedDeviceRelative）。</summary>
    public sealed record DeviceRelative(uint DeviceIndex, Pose3x4 Pose) : AnchorPlacement;

    /// <summary>ルーム空間の絶対姿勢（SetOverlayTransformAbsolute）。</summary>
    public sealed record Absolute(TrackingUniverse Universe, Pose3x4 Pose) : AnchorPlacement;

    /// <summary>置き場所が決まらない（デバイスをロストした等）。非表示にする。</summary>
    public sealed record Hidden : AnchorPlacement;
}

/// <summary>1台のトラッキングデバイスの姿勢。</summary>
public readonly record struct DevicePose(bool IsConnected, bool IsPoseValid, Pose3x4 DeviceToAbsolute);

/// <summary>
/// 1フレーム分のトラッキング情報。OpenVR から取得したものを AppLifecycle が詰めて渡す。
/// </summary>
public sealed class TrackingSnapshot
{
    public const uint InvalidDeviceIndex = uint.MaxValue;

    public TrackingSnapshot(
        TrackingUniverse universe,
        IReadOnlyList<DevicePose> poses,
        uint leftHandIndex,
        uint rightHandIndex,
        IReadOnlyDictionary<string, uint> serialToIndex)
    {
        Universe = universe;
        Poses = poses;
        LeftHandIndex = leftHandIndex;
        RightHandIndex = rightHandIndex;
        SerialToIndex = serialToIndex;
    }

    /// <summary><see cref="Poses"/> の座標系。</summary>
    public TrackingUniverse Universe { get; }

    /// <summary>デバイスインデックス順の姿勢。</summary>
    public IReadOnlyList<DevicePose> Poses { get; }

    public uint LeftHandIndex { get; }

    public uint RightHandIndex { get; }

    /// <summary>シリアル番号 → デバイスインデックス。</summary>
    public IReadOnlyDictionary<string, uint> SerialToIndex { get; }

    public DevicePose? TryGetPose(uint index) =>
        index < Poses.Count ? Poses[(int)index] : null;
}
