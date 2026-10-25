using ViolinPassthrough.Logging;

namespace ViolinPassthrough.Anchor;

/// <summary>手のロール。</summary>
public enum DeviceRole
{
    LeftHand,
    RightHand,
}

/// <summary>デバイスを見失ったときの扱い。</summary>
public enum LostBehavior
{
    /// <summary>最後に見えていた場所に置いたままにする。</summary>
    Hold,

    /// <summary>隠す。</summary>
    Hide,
}

/// <summary>
/// 手（コントローラー）やトラッカーに追従する。ロール（左手/右手）かシリアル番号でデバイスを選ぶ。
/// ハンドトラッキングでは、VD がエミュレートするコントローラーの姿勢になる。
/// 見えている間はデバイス基準の相対姿勢で置き（SteamVR が追従させる）、見失ったら <see cref="LostBehavior"/> に従う。
/// </summary>
public sealed class DeviceAnchor : IAnchor
{
    private Pose3x4? _lastDevicePose;
    private TrackingUniverse _lastUniverse;
    private bool _serialMissingLogged;

    public DeviceAnchor(DeviceRole? role, string? serial, LostBehavior onLost)
    {
        if (role is null == serial is null)
        {
            throw new ArgumentException("Specify exactly one of role or serial.");
        }

        Role = role;
        Serial = serial;
        OnLost = onLost;
    }

    public DeviceRole? Role { get; }

    public string? Serial { get; }

    public LostBehavior OnLost { get; }

    public bool RequiresTracking => true;

    public AnchorPlacement Resolve(Pose3x4 localPose, TrackingSnapshot? tracking)
    {
        if (tracking is not null)
        {
            var index = FindDevice(tracking);
            if (index != TrackingSnapshot.InvalidDeviceIndex
                && tracking.TryGetPose(index) is { IsConnected: true, IsPoseValid: true } pose)
            {
                _lastDevicePose = pose.DeviceToAbsolute;
                _lastUniverse = tracking.Universe;
                return new AnchorPlacement.DeviceRelative(index, localPose);
            }
        }

        // 見失った（または、まだ一度も見えていない）。
        if (OnLost == LostBehavior.Hold && _lastDevicePose is { } last)
        {
            return new AnchorPlacement.Absolute(_lastUniverse, last * localPose);
        }

        return new AnchorPlacement.Hidden();
    }

    private uint FindDevice(TrackingSnapshot tracking)
    {
        if (Serial is not null)
        {
            if (tracking.SerialToIndex.TryGetValue(Serial, out var index))
            {
                if (_serialMissingLogged)
                {
                    Log.Info($"Device with serial \"{Serial}\" found.");
                    _serialMissingLogged = false;
                }

                return index;
            }

            if (!_serialMissingLogged)
            {
                // 打ち間違いに気づけるように、一度だけ知らせる。接続中のデバイスの一覧は起動時のログにある。
                Log.Warn($"No connected device has serial \"{Serial}\" (yet). Connected: {string.Join(", ", tracking.SerialToIndex.Keys)}.");
                _serialMissingLogged = true;
            }

            return TrackingSnapshot.InvalidDeviceIndex;
        }

        return Role == DeviceRole.LeftHand ? tracking.LeftHandIndex : tracking.RightHandIndex;
    }
}
