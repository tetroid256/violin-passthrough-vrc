using ViolinPassthrough.Anchor;

namespace ViolinPassthrough.Tests.Anchor;

public class PoseMathTests
{
    [Fact]
    public void Inverse_UndoesThePose()
    {
        var pose = Pose3x4.FromPositionEuler(new Vec3(0.3, 1.5, -0.2), new Vec3(25, -40, 10));

        var identity = pose * pose.Inverse();
        var p = new Vec3(0.1, -0.2, 0.7);

        AssertVec(p, identity.TransformPoint(p));
        AssertVec(p, pose.Inverse().TransformPoint(pose.TransformPoint(p)));
    }

    [Theory]
    [InlineData(15, 20, -15)]
    [InlineData(-30, 170, 45)]
    [InlineData(0, -90, 0)]
    [InlineData(89, 10, 20)]
    [InlineData(-60, -135, -170)]
    public void ToEulerDeg_RoundTrips(double pitch, double yaw, double roll)
    {
        var pose = Pose3x4.FromPositionEuler(default, new Vec3(pitch, yaw, roll));

        var euler = pose.ToEulerDeg();
        var back = Pose3x4.FromPositionEuler(default, euler);

        Assert.Equal(pitch, euler.X, 6);
        Assert.Equal(yaw, euler.Y, 6);
        Assert.Equal(roll, euler.Z, 6);
        AssertRotation(pose, back);
    }

    [Theory]
    [InlineData(90, 30, 0)]
    [InlineData(-90, -45, 0)]
    public void ToEulerDeg_HandlesGimbalLock(double pitch, double yaw, double roll)
    {
        var pose = Pose3x4.FromPositionEuler(default, new Vec3(pitch, yaw, roll));

        var back = Pose3x4.FromPositionEuler(default, pose.ToEulerDeg());

        AssertRotation(pose, back);
    }

    [Fact]
    public void FromBasis_PutsAxesInColumns()
    {
        var pose = Pose3x4.FromBasis(new Vec3(1, 2, 3), new Vec3(0, 1, 0), new Vec3(-1, 0, 0), new Vec3(0, 0, 1));

        AssertVec(new Vec3(0, 1, 0), pose.TransformDirection(new Vec3(1, 0, 0)));
        AssertVec(new Vec3(-1, 0, 0), pose.TransformDirection(new Vec3(0, 1, 0)));
        AssertVec(new Vec3(1, 2, 3), pose.Translation);
    }

    [Fact]
    public void VectorHelpers()
    {
        Assert.Equal(5, new Vec3(3, 4, 0).Length, 12);
        AssertVec(new Vec3(0, 0, 1), Vec3.Cross(new Vec3(1, 0, 0), new Vec3(0, 1, 0)));
        Assert.Equal(90, Vec3.AngleDeg(new Vec3(1, 0, 0), new Vec3(0, 2, 0)), 9);
        Assert.Equal(default, new Vec3(0, 0, 0).Normalized());
    }

    private static void AssertVec(Vec3 expected, Vec3 actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
        Assert.Equal(expected.Z, actual.Z, 9);
    }

    private static void AssertRotation(Pose3x4 expected, Pose3x4 actual)
    {
        foreach (var axis in new[] { new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1) })
        {
            AssertVec(expected.TransformDirection(axis), actual.TransformDirection(axis));
        }
    }
}
