using ViolinPassthrough.Anchor;

namespace ViolinPassthrough.Tests.Anchor;

public class Pose3x4Tests
{
    private const int Precision = 9;

    [Fact]
    public void ZeroEuler_IsPureTranslation()
    {
        var pose = Pose3x4.FromPositionEuler(new Vec3(1, 2, 3), new Vec3(0, 0, 0));

        Assert.Equal(Pose3x4.Identity with { M03 = 1, M13 = 2, M23 = 3 }, pose);
    }

    [Fact]
    public void PositiveYaw_TurnsPlaneNormalToTheRight()
    {
        // 板の法線（+Z、見る人の方）は yaw 正で右（+X）に向く。
        var pose = Pose3x4.FromPositionEuler(default, new Vec3(0, 90, 0));
        var normal = pose.TransformDirection(new Vec3(0, 0, 1));

        AssertVec(new Vec3(1, 0, 0), normal);
    }

    [Fact]
    public void PositivePitch_TurnsPlaneToFaceUp()
    {
        // pitch 正で板の表（+Z）は上を向き、上端（+Y）は奥（-Z）へ倒れる。
        var pose = Pose3x4.FromPositionEuler(default, new Vec3(90, 0, 0));

        AssertVec(new Vec3(0, 1, 0), pose.TransformDirection(new Vec3(0, 0, 1)));
        AssertVec(new Vec3(0, 0, -1), pose.TransformDirection(new Vec3(0, 1, 0)));
    }

    [Theory]
    [InlineData(-0.15, -0.15, -0.40, 15, 20, -15)]
    [InlineData(-0.05, -0.22, -0.30, 30, 10, -15)]
    public void DesignDefaults_FaceTheViewer(double x, double y, double z, double pitch, double yaw, double roll)
    {
        // 設計書の初期値は、視点の左下にある板を視点に向ける角度になっている。
        var pose = Pose3x4.FromPositionEuler(new Vec3(x, y, z), new Vec3(pitch, yaw, roll));
        var normal = pose.TransformDirection(new Vec3(0, 0, 1));
        var toEye = new Vec3(-x, -y, -z);
        var cos = Dot(normal, toEye) / Math.Sqrt(Dot(toEye, toEye));

        Assert.True(cos > Math.Cos(15 * Math.PI / 180), $"angle = {Math.Acos(cos) * 180 / Math.PI:0.0} deg");
    }

    [Fact]
    public void PositiveRoll_RotatesRightEdgeUp()
    {
        var pose = Pose3x4.FromPositionEuler(default, new Vec3(0, 0, 90));
        var right = pose.TransformDirection(new Vec3(1, 0, 0));

        AssertVec(new Vec3(0, 1, 0), right);
    }

    [Fact]
    public void Roll_IsAppliedInThePlanesOwnFrame()
    {
        // roll は板自身の面内の回転なので、pitch / yaw の後でも板の法線は変えない。
        var withoutRoll = Pose3x4.FromPositionEuler(default, new Vec3(15, 20, 0));
        var withRoll = Pose3x4.FromPositionEuler(default, new Vec3(15, 20, -40));

        AssertVec(
            withoutRoll.TransformDirection(new Vec3(0, 0, 1)),
            withRoll.TransformDirection(new Vec3(0, 0, 1)));
    }

    [Fact]
    public void Yaw_IsAppliedLast_AroundTheViewersVerticalAxis()
    {
        // yaw は最後に HMD の Y 軸まわりで回すので、pitch で上を向いた法線の高さ（Y 成分）は yaw で変わらない。
        var a = Pose3x4.FromPositionEuler(default, new Vec3(30, 0, 10)).TransformDirection(new Vec3(0, 0, 1));
        var b = Pose3x4.FromPositionEuler(default, new Vec3(30, 70, 10)).TransformDirection(new Vec3(0, 0, 1));

        Assert.Equal(a.Y, b.Y, Precision);
    }

    [Fact]
    public void EulerOrder_IsYawPitchRoll()
    {
        var expected = Pose3x4.RotationY(20) * Pose3x4.RotationX(-15) * Pose3x4.RotationZ(-15);

        var pose = Pose3x4.FromPositionEuler(default, new Vec3(15, 20, -15));

        AssertPose(expected, pose);
    }

    [Fact]
    public void Multiply_AppliesRightOperandFirst()
    {
        var translate = Pose3x4.Identity with { M03 = 1 };
        var rotate = Pose3x4.RotationY(90);

        // 回してから動かす: 原点 → (1,0,0)
        var p1 = (translate * rotate).TransformPoint(new Vec3(0, 0, 0));
        // 動かしてから回す: (1,0,0) → Y 軸 90° で (0,0,-1)
        var p2 = (rotate * translate).TransformPoint(new Vec3(0, 0, 0));

        AssertVec(new Vec3(1, 0, 0), p1);
        AssertVec(new Vec3(0, 0, -1), p2);
    }

    [Fact]
    public void RotationPart_IsOrthonormal()
    {
        var pose = Pose3x4.FromPositionEuler(new Vec3(-0.15, -0.15, -0.4), new Vec3(15, 20, -15));
        var x = pose.TransformDirection(new Vec3(1, 0, 0));
        var y = pose.TransformDirection(new Vec3(0, 1, 0));
        var z = pose.TransformDirection(new Vec3(0, 0, 1));

        Assert.Equal(1.0, Dot(x, x), Precision);
        Assert.Equal(1.0, Dot(y, y), Precision);
        Assert.Equal(1.0, Dot(z, z), Precision);
        Assert.Equal(0.0, Dot(x, y), Precision);
        Assert.Equal(0.0, Dot(y, z), Precision);
        Assert.Equal(0.0, Dot(x, z), Precision);
    }

    private static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static void AssertVec(Vec3 expected, Vec3 actual)
    {
        Assert.Equal(expected.X, actual.X, Precision);
        Assert.Equal(expected.Y, actual.Y, Precision);
        Assert.Equal(expected.Z, actual.Z, Precision);
    }

    private static void AssertPose(Pose3x4 expected, Pose3x4 actual)
    {
        Assert.Equal(expected.M00, actual.M00, Precision);
        Assert.Equal(expected.M01, actual.M01, Precision);
        Assert.Equal(expected.M02, actual.M02, Precision);
        Assert.Equal(expected.M03, actual.M03, Precision);
        Assert.Equal(expected.M10, actual.M10, Precision);
        Assert.Equal(expected.M11, actual.M11, Precision);
        Assert.Equal(expected.M12, actual.M12, Precision);
        Assert.Equal(expected.M13, actual.M13, Precision);
        Assert.Equal(expected.M20, actual.M20, Precision);
        Assert.Equal(expected.M21, actual.M21, Precision);
        Assert.Equal(expected.M22, actual.M22, Precision);
        Assert.Equal(expected.M23, actual.M23, Precision);
    }
}
