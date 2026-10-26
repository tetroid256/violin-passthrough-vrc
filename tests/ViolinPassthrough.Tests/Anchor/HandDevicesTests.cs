using ViolinPassthrough.Anchor;

namespace ViolinPassthrough.Tests.Anchor;

public class HandDevicesTests
{
    private static readonly Pose3x4 Hmd = Tracking.StandingHmd();

    [Fact]
    public void WithoutSerials_UsesTheHandRoles()
    {
        var tracking = Tracking.Make(Hmd, new Vec3(-0.3, 0, -0.3), new Vec3(0.1, 0, -0.3));

        Assert.Equal(Tracking.Left, HandDevices.Default.LeftIndex(tracking));
        Assert.Equal(Tracking.Right, HandDevices.Default.RightIndex(tracking));
    }

    [Fact]
    public void Serial_SelectsThatDevice()
    {
        var tracking = Tracking.WithTracker(Tracking.Make(Hmd, null, null), "LHR-1", Hmd);

        var hands = new HandDevices("LHR-1", null);

        Assert.Equal(3u, hands.LeftIndex(tracking));
        Assert.Equal(Tracking.Right, hands.RightIndex(tracking));
    }

    [Fact]
    public void MissingSerial_DoesNotFallBackToTheHand()
    {
        // 手首のトラッカーと手では位置がずれるので、混ぜない。
        var tracking = Tracking.Make(Hmd, new Vec3(-0.3, 0, -0.3), null);

        Assert.Equal(TrackingSnapshot.InvalidDeviceIndex, new HandDevices("LHR-1", null).LeftIndex(tracking));
    }

    [Fact]
    public void AnchorIndex_FollowsTheAnchorDefinition()
    {
        var tracking = Tracking.WithTracker(Tracking.Make(Hmd, null, null), "LHR-1", Hmd);

        Assert.Equal(3u, HandDevices.AnchorIndex(tracking, new DeviceAnchorSpec(null, "LHR-1", LostBehavior.Hold)));
        Assert.Equal(Tracking.Left, HandDevices.AnchorIndex(tracking, new DeviceAnchorSpec(DeviceRole.LeftHand, null, LostBehavior.Hold)));
        Assert.Equal(TrackingSnapshot.InvalidDeviceIndex, HandDevices.AnchorIndex(tracking, new DeviceAnchorSpec(null, "nope", LostBehavior.Hold)));
    }
}
