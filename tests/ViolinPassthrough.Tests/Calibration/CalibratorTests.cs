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
    public void PlateIsPerpendicularToTheViewBisector_AtTheHandsMeanDistance()
    {
        var result = Compute(Tracking.StandingHmd(), LeftLocal, RightLocal);
        var pose = Pose3x4.FromPositionEuler(result.Position, result.RotationEulerDeg);

        var view = (LeftLocal.Normalized() + RightLocal.Normalized()).Normalized();
        var normal = pose.TransformDirection(new Vec3(0, 0, 1));
        AssertVec(-view, normal, 9);
        Assert.Equal(((LeftLocal + RightLocal) * 0.5).Length, Vec3.Dot(result.Position, view), 9);
        Assert.Equal("fingerboard", result.RegionId);
    }

    [Fact]
    public void FromTheEye_HandsAppearOnTheHorizontalAxis_PaddingInsideTheEdges()
    {
        var result = Compute(Tracking.StandingHmd(), LeftLocal, RightLocal, pad: 0.05);

        var left = SeenOnPlate(result, new Vec3(0, 0, 0), LeftLocal);
        var right = SeenOnPlate(result, new Vec3(0, 0, 0), RightLocal);

        Assert.Equal(0, left.Y, 9);
        Assert.Equal(0, right.Y, 9);
        Assert.Equal(-(result.WidthMeters / 2 - 0.05), left.X, 9);
        Assert.Equal(result.WidthMeters / 2 - 0.05, right.X, 9);
    }

    [Theory]
    [InlineData(-0.032)]
    [InlineData(0.032)]
    public void NearBowHandAndFarLeftHand_BothFitTheWindowFromEachEye(double eyeX)
    {
        // 弓の手（駒）は近く、左手（スクロール側）は遠い。どちらの目から見ても両手が窓（余白込み）に入ること。
        var left = new Vec3(-0.35, -0.12, -0.35);
        var right = new Vec3(0.02, -0.18, -0.15);
        var result = Compute(Tracking.StandingHmd(), left, right, pad: 0.05);
        var region = Region with { WidthMeters = result.WidthMeters };
        var halfW = region.QuadWidthMeters / 2;
        var halfH = region.QuadHeightMeters / 2;

        foreach (var hand in new[] { left, right })
        {
            var p = SeenOnPlate(result, new Vec3(eyeX, 0, 0), hand);
            Assert.InRange(p.X, -halfW, halfW);
            Assert.InRange(p.Y, -halfH, halfH);
        }
    }

    [Fact]
    public void LookingAtTheLeftHand_StillCalibrates()
    {
        // 左手を見ながら構えると、目から見た両手の方向は近い（約 10°）が、失敗にしない。
        var left = new Vec3(-0.05, -0.08, -0.55);
        var right = new Vec3(0.0, -0.10, -0.30);

        var result = Compute(Tracking.StandingHmd(), left, right);

        Assert.InRange(Vec3.AngleDeg(left, right), 5, 15);
        Assert.True(result.WidthMeters > 0.1);
    }

    /// <summary>目（HMD 座標の点）から手を見たとき、視線が板と交わる点（板の座標）。</summary>
    private static Vec3 SeenOnPlate(CalibrationResult result, Vec3 eye, Vec3 hand)
    {
        var pose = Pose3x4.FromPositionEuler(result.Position, result.RotationEulerDeg);
        var normal = pose.TransformDirection(new Vec3(0, 0, 1));
        var dir = (hand - eye).Normalized();
        var t = Vec3.Dot(result.Position - eye, normal) / Vec3.Dot(dir, normal);
        return pose.Inverse().TransformPoint(eye + dir * t);
    }

    [Fact]
    public void ResultIsInHmdSpace_IndependentOfWhereThePlayerStands()
    {
        var a = Compute(Tracking.StandingHmd(), LeftLocal, RightLocal);
        var b = Compute(Pose3x4.FromPositionEuler(new Vec3(-2, 1.4, 3), new Vec3(10, 135, -20)), LeftLocal, RightLocal);

        AssertVec(a.Position, b.Position, 9);
        AssertVec(a.RotationEulerDeg, b.RotationEulerDeg, 6);
        Assert.Equal(a.WidthMeters, b.WidthMeters, 9);
        AssertVec(LeftLocal, b.LeftHandPosition!.Value, 9);
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
    public void HandsInOppositeDirections_Fail()
    {
        var hmd = Tracking.StandingHmd();
        var tracking = Tracking.Make(hmd, new Vec3(-0.5, 0, 0.01), new Vec3(0.5, 0, 0.01));

        Assert.Null(Calibrator.Compute(hmd, tracking.Poses[1].DeviceToAbsolute, tracking.Poses[2].DeviceToAbsolute, Region, 0.05, out var error));
        Assert.Equal("HANDS TOO FAR APART", error);
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
        Assert.Equal("TOO CLOSE TO HEAD", error);
    }

    [Fact]
    public void DeviceRegion_IsExpressedInTheDeviceFrame_AndLandsAtTheSamePlace()
    {
        // 楽器に付けたトラッカー（傾いている）に追従する領域。キャリブレーションした瞬間は、HMD 基準と同じ場所に来ること。
        var hmdResult = Compute(Tracking.StandingHmd(), LeftLocal, RightLocal);
        var anchorInHmd = Pose3x4.FromPositionEuler(new Vec3(-0.2, -0.15, -0.3), new Vec3(30, -40, 70));
        var region = Region with { Anchor = new DeviceAnchorSpec(null, "VIOLIN", LostBehavior.Hold) };
        var input = new CalibrationInput(LeftLocal, RightLocal, new Vec3(0, 1, 0), anchorInHmd, LeftLocal);

        var result = Calibrator.Compute(input, region, 0.05, out var error);

        Assert.True(result is not null, error);
        var placed = anchorInHmd * Pose3x4.FromPositionEuler(result!.Position, result.RotationEulerDeg);
        var expected = Pose3x4.FromPositionEuler(hmdResult.Position, hmdResult.RotationEulerDeg);
        AssertVec(expected.Translation, placed.Translation, 9);
        AssertVec(expected.TransformDirection(new Vec3(1, 0, 0)), placed.TransformDirection(new Vec3(1, 0, 0)), 6);
        AssertVec(expected.TransformDirection(new Vec3(0, 0, 1)), placed.TransformDirection(new Vec3(0, 0, 1)), 6);
        Assert.Equal(hmdResult.WidthMeters, result.WidthMeters, 9);
        // 自動モードの基準は、領域のアンカーに関係なく HMD から見た位置。
        AssertVec(LeftLocal, result.LeftHandPosition!.Value, 9);
    }

    [Fact]
    public void DeviceRegion_WithoutTheDevicePose_Fails()
    {
        var region = Region with { Anchor = new DeviceAnchorSpec(DeviceRole.LeftHand, null, LostBehavior.Hold) };

        Assert.Null(Calibrator.Compute(new CalibrationInput(LeftLocal, RightLocal, new Vec3(0, 1, 0), null, LeftLocal), region, 0.05, out var error));
        Assert.Equal("NO ANCHOR DEVICE", error);
    }

    [Fact]
    public void WorldRegion_Fails()
    {
        var region = Region with { Anchor = new WorldAnchorSpec(TrackingUniverse.Standing) };

        Assert.Null(Calibrator.Compute(new CalibrationInput(LeftLocal, RightLocal, new Vec3(0, 1, 0), Pose3x4.Identity, LeftLocal), region, 0.05, out var error));
        Assert.Equal("WORLD REGION", error);
    }

    [Fact]
    public void HmdRegion_IgnoresAnAnchorPose()
    {
        var hmdResult = Compute(Tracking.StandingHmd(), LeftLocal, RightLocal);
        var input = new CalibrationInput(LeftLocal, RightLocal, new Vec3(0, 1, 0), Pose3x4.FromPositionEuler(new Vec3(1, 2, 3), new Vec3(10, 20, 30)), LeftLocal);

        var result = Calibrator.Compute(input, Region, 0.05, out _)!;

        AssertVec(hmdResult.Position, result.Position, 9);
    }

    private static void AssertVec(Vec3 expected, Vec3 actual, int precision)
    {
        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
        Assert.Equal(expected.Z, actual.Z, precision);
    }
}
