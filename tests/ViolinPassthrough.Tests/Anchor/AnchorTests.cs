using ViolinPassthrough.Anchor;

namespace ViolinPassthrough.Tests.Anchor;

public class AnchorTests
{
    private static readonly Pose3x4 Local = Pose3x4.FromPositionEuler(new Vec3(0, 0.05, -0.1), new Vec3(10, 0, 0));
    private static readonly Pose3x4 HandPose = Pose3x4.FromPositionEuler(new Vec3(0.3, 1.2, -0.4), new Vec3(0, 45, 0));

    private static TrackingSnapshot Snapshot(
        bool leftValid = true,
        uint left = 3,
        uint right = 4,
        bool connected = true,
        Dictionary<string, uint>? serials = null,
        TrackingUniverse universe = TrackingUniverse.Standing)
    {
        var poses = Enumerable.Repeat(new DevicePose(false, false, Pose3x4.Identity), 8).ToArray();
        poses[3] = new DevicePose(connected, leftValid, HandPose);
        poses[4] = new DevicePose(true, true, Pose3x4.Identity);
        poses[5] = new DevicePose(true, true, HandPose);
        return new TrackingSnapshot(universe, poses, left, right, serials ?? new Dictionary<string, uint>());
    }

    [Fact]
    public void Hmd_IsRelativeToDeviceZero()
    {
        var anchor = new HmdAnchorSpec().CreateAnchor();

        Assert.False(anchor.RequiresTracking);
        Assert.Equal(new AnchorPlacement.DeviceRelative(0, Local), anchor.Resolve(Local, null));
    }

    [Theory]
    [InlineData(TrackingUniverse.Standing)]
    [InlineData(TrackingUniverse.Seated)]
    public void World_IsAbsoluteInTheChosenUniverse(TrackingUniverse universe)
    {
        var anchor = new WorldAnchorSpec(universe).CreateAnchor();

        Assert.False(anchor.RequiresTracking);
        Assert.Equal(new AnchorPlacement.Absolute(universe, Local), anchor.Resolve(Local, null));
    }

    [Fact]
    public void Device_FollowsTheHandWhileTracked()
    {
        var anchor = new DeviceAnchor(DeviceRole.LeftHand, null, LostBehavior.Hold);

        Assert.True(anchor.RequiresTracking);
        Assert.Equal(new AnchorPlacement.DeviceRelative(3, Local), anchor.Resolve(Local, Snapshot()));
    }

    [Fact]
    public void Device_UsesRightHandIndex()
    {
        var anchor = new DeviceAnchor(DeviceRole.RightHand, null, LostBehavior.Hold);

        Assert.Equal(new AnchorPlacement.DeviceRelative(4, Local), anchor.Resolve(Local, Snapshot()));
    }

    [Fact]
    public void Device_FollowsRoleReassignment()
    {
        var anchor = new DeviceAnchor(DeviceRole.LeftHand, null, LostBehavior.Hold);
        anchor.Resolve(Local, Snapshot());

        Assert.Equal(new AnchorPlacement.DeviceRelative(5, Local), anchor.Resolve(Local, Snapshot(left: 5)));
    }

    [Fact]
    public void Device_BySerial()
    {
        var anchor = new DeviceAnchor(null, "LHR-1234", LostBehavior.Hide);
        var serials = new Dictionary<string, uint> { ["LHR-1234"] = 5 };

        Assert.Equal(new AnchorPlacement.DeviceRelative(5, Local), anchor.Resolve(Local, Snapshot(serials: serials)));
        Assert.IsType<AnchorPlacement.Hidden>(anchor.Resolve(Local, Snapshot()));
    }

    [Fact]
    public void Device_Hold_KeepsLastWorldPoseWhenLost()
    {
        var anchor = new DeviceAnchor(DeviceRole.LeftHand, null, LostBehavior.Hold);
        anchor.Resolve(Local, Snapshot());

        var lost = anchor.Resolve(Local, Snapshot(leftValid: false));

        Assert.Equal(new AnchorPlacement.Absolute(TrackingUniverse.Standing, HandPose * Local), lost);
        // 見失っている間は同じ置き場所のまま（毎フレーム OpenVR を呼ばない）。
        Assert.Equal(lost, anchor.Resolve(Local, Snapshot(left: TrackingSnapshot.InvalidDeviceIndex)));
        Assert.Equal(lost, anchor.Resolve(Local, Snapshot(connected: false)));
        // 見つかったら追従に戻る。
        Assert.Equal(new AnchorPlacement.DeviceRelative(3, Local), anchor.Resolve(Local, Snapshot()));
    }

    [Fact]
    public void Device_Hide_HidesWhenLost()
    {
        var anchor = new DeviceAnchor(DeviceRole.LeftHand, null, LostBehavior.Hide);
        anchor.Resolve(Local, Snapshot());

        Assert.IsType<AnchorPlacement.Hidden>(anchor.Resolve(Local, Snapshot(leftValid: false)));
    }

    [Fact]
    public void Device_NeverTracked_IsHiddenEvenWithHold()
    {
        var anchor = new DeviceAnchor(DeviceRole.LeftHand, null, LostBehavior.Hold);

        Assert.IsType<AnchorPlacement.Hidden>(anchor.Resolve(Local, null));
        Assert.IsType<AnchorPlacement.Hidden>(anchor.Resolve(Local, Snapshot(leftValid: false)));
    }

    [Fact]
    public void Device_RequiresExactlyOneSelector()
    {
        Assert.Throws<ArgumentException>(() => new DeviceAnchor(null, null, LostBehavior.Hold));
        Assert.Throws<ArgumentException>(() => new DeviceAnchor(DeviceRole.LeftHand, "x", LostBehavior.Hold));
    }

    [Fact]
    public void OnlyDeviceAnchors_RequireTracking()
    {
        // MaskOverlay.RequiresTracking は、設定から作ったアンカーのこの値をそのまま返す。
        Assert.False(new HmdAnchorSpec().CreateAnchor().RequiresTracking);
        Assert.False(new WorldAnchorSpec(TrackingUniverse.Standing).CreateAnchor().RequiresTracking);
        Assert.True(new DeviceAnchorSpec(DeviceRole.LeftHand, null, LostBehavior.Hold).CreateAnchor().RequiresTracking);
        Assert.True(new DeviceAnchorSpec(null, "x", LostBehavior.Hide).CreateAnchor().RequiresTracking);
    }

    [Fact]
    public void HmdMatrix_RoundTrips()
    {
        var pose = Pose3x4.FromPositionEuler(new Vec3(0.1, -0.2, 0.3), new Vec3(10, 20, 30));

        var back = ViolinPassthrough.App.AppLifecycle.FromHmdMatrix(ViolinPassthrough.Mask.MaskOverlay.ToHmdMatrix(pose));

        Assert.Equal(pose.M03, back.M03, 6);
        Assert.Equal(pose.M12, back.M12, 6);
        Assert.Equal(pose.M21, back.M21, 6);
        Assert.Equal(pose.M23, back.M23, 6);
    }
}
