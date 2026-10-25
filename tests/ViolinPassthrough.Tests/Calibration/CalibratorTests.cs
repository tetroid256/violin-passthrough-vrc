using ViolinPassthrough.Anchor;
using ViolinPassthrough.Calibration;

namespace ViolinPassthrough.Tests.Calibration;

public class CalibratorTests
{
    private static readonly Vec3 LeftLocal = new(-0.35, -0.10, -0.35);
    private static readonly Vec3 RightLocal = new(0.0, -0.25, -0.30);

    private static ViolinPassthrough.Mask.MaskRegion Region => TestConfigs.Default.Regions[0];

    private static CalibrationResult Compute(Pose3x4 hmd, Vec3 left, Vec3 right, double pad = 0.05)
    {
        var tracking = Tracking.Make(hmd, left, right);
        var result = Calibrator.Compute(
            hmd,
            tracking.Poses[1].DeviceToAbsolute,
            tracking.Poses[2].DeviceToAbsolute,
            Region,
            pad,
            out var error);
        Assert.True(result is not null, error);
        return result!;
    }

    [Fact]
    public void PlateIsCenteredBetweenTheHandsAndFacesTheEye()
    {
        var result = Compute(Tracking.StandingHmd(), LeftLocal, RightLocal);
        var pose = Pose3x4.FromPositionEuler(result.Position, result.RotationEulerDeg);

        AssertVec((LeftLocal + RightLocal) * 0.5, result.Position, 9);
        var normal = pose.TransformDirection(new Vec3(0, 0, 1));
        AssertVec((-result.Position).Normalized(), normal, 9);
        Assert.Equal("fingerboard", result.RegionId);
    }

    [Fact]
    public void HandsLieOnThePlatesHorizontalAxis_AndWidthAddsPadding()
    {
        var result = Compute(Tracking.StandingHmd(), LeftLocal, RightLocal, pad: 0.05);
        var toPlate = Pose3x4.FromPositionEuler(result.Position, result.RotationEulerDeg).Inverse();

        var left = toPlate.TransformPoint(LeftLocal);
        var right = toPlate.TransformPoint(RightLocal);

        // 左手は板の左（-X）、右手は右（+X）。板の面の上で縦方向（Y）のずれはない。
        Assert.True(left.X < 0 && right.X > 0);
        Assert.Equal(0, left.Y, 9);
        Assert.Equal(0, right.Y, 9);
        Assert.Equal(right.X - left.X + 0.10, result.WidthMeters, 9);
        Assert.Equal(-left.X, right.X, 9);
    }

    [Fact]
    public void ResultIsInHmdSpace_IndependentOfWhereThePlayerStands()
    {
        var a = Compute(Tracking.StandingHmd(), LeftLocal, RightLocal);
        var b = Compute(Pose3x4.FromPositionEuler(new Vec3(-2, 1.4, 3), new Vec3(10, 135, -20)), LeftLocal, RightLocal);

        AssertVec(a.Position, b.Position, 9);
        AssertVec(a.RotationEulerDeg, b.RotationEulerDeg, 6);
        Assert.Equal(a.WidthMeters, b.WidthMeters, 9);
        AssertVec(LeftLocal, b.LeftHandPosition, 9);
    }

    [Fact]
    public void HeadUp_IsWorldUpSeenFromTheHmd()
    {
        // 頭を左に 20° 傾けると、HMD から見た「上」は右に傾く。
        var result = Compute(Tracking.StandingHmd(rollDeg: 20), LeftLocal, RightLocal);

        Assert.Equal(1, result.HeadUp.Length, 9);
        Assert.Equal(20, Vec3.AngleDeg(result.HeadUp, new Vec3(0, 1, 0)), 6);
        Assert.True(result.HeadUp.X > 0);
    }

    [Fact]
    public void DesignLikeLayout_GivesSimilarAnglesToTheDesignDefaults()
    {
        // 左手が左下奥、右手が右下手前にあるとき、板は左下から視点を向き、右端が下がる（roll が負）。
        var result = Compute(Tracking.StandingHmd(), LeftLocal, RightLocal);

        Assert.True(result.RotationEulerDeg.X > 0, $"pitch {result.RotationEulerDeg.X}");
        Assert.True(result.RotationEulerDeg.Y > 0, $"yaw {result.RotationEulerDeg.Y}");
        Assert.True(result.RotationEulerDeg.Z < 0, $"roll {result.RotationEulerDeg.Z}");
    }

    [Fact]
    public void HandsTooClose_Fails()
    {
        var hmd = Tracking.StandingHmd();
        var tracking = Tracking.Make(hmd, LeftLocal, LeftLocal + new Vec3(0.02, 0, 0));

        var result = Calibrator.Compute(hmd, tracking.Poses[1].DeviceToAbsolute, tracking.Poses[2].DeviceToAbsolute, Region, 0.05, out var error);

        Assert.Null(result);
        Assert.Equal("HANDS TOO CLOSE", error);
    }

    [Fact]
    public void HandsAlongTheLineOfSight_Fail()
    {
        // 両手が視線の方向に並んでいると、板の面に投影した距離が足りない。
        var hmd = Tracking.StandingHmd();
        var tracking = Tracking.Make(hmd, new Vec3(0, 0, -0.3), new Vec3(0, 0, -0.6));

        Assert.Null(Calibrator.Compute(hmd, tracking.Poses[1].DeviceToAbsolute, tracking.Poses[2].DeviceToAbsolute, Region, 0.05, out var error));
        Assert.Equal("HANDS TOO CLOSE", error);
    }

    [Fact]
    public void HandsAtTheHead_Fail()
    {
        var hmd = Tracking.StandingHmd();
        var tracking = Tracking.Make(hmd, new Vec3(-0.04, 0, 0), new Vec3(0.04, 0, 0));

        Assert.Null(Calibrator.Compute(hmd, tracking.Poses[1].DeviceToAbsolute, tracking.Poses[2].DeviceToAbsolute, Region, 0.05, out var error));
        Assert.Equal("HANDS TOO CLOSE TO HEAD", error);
    }

    private static void AssertVec(Vec3 expected, Vec3 actual, int precision)
    {
        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
        Assert.Equal(expected.Z, actual.Z, precision);
    }
}
