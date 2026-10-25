namespace ViolinPassthrough.Anchor;

public readonly record struct Vec3(double X, double Y, double Z);

/// <summary>
/// 3x4 の剛体変換（回転＋平行移動）。行優先で、OpenVR の HmdMatrix34_t と同じ並び。
/// 列ベクトルに左から掛ける（p' = R p + t）。
/// </summary>
public readonly record struct Pose3x4(
    double M00, double M01, double M02, double M03,
    double M10, double M11, double M12, double M13,
    double M20, double M21, double M22, double M23)
{
    public static Pose3x4 Identity { get; } = new(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0);

    public Vec3 Translation => new(M03, M13, M23);

    /// <summary>
    /// 位置とオイラー角（度）から姿勢を作る。
    /// 回転は右手系で、pitch = X 軸、yaw = Y 軸、roll = Z 軸まわり。
    /// R = Ry(yaw) * Rx(pitch) * Rz(roll)（板をまず roll、次に pitch、最後に yaw で回す）。
    /// </summary>
    public static Pose3x4 FromPositionEuler(Vec3 position, Vec3 eulerDeg)
    {
        var rx = RotationX(eulerDeg.X);
        var ry = RotationY(eulerDeg.Y);
        var rz = RotationZ(eulerDeg.Z);
        var r = ry * rx * rz;
        return r with { M03 = position.X, M13 = position.Y, M23 = position.Z };
    }

    public static Pose3x4 RotationX(double degrees)
    {
        var (s, c) = Math.SinCos(degrees * Math.PI / 180.0);
        return new(
            1, 0, 0, 0,
            0, c, -s, 0,
            0, s, c, 0);
    }

    public static Pose3x4 RotationY(double degrees)
    {
        var (s, c) = Math.SinCos(degrees * Math.PI / 180.0);
        return new(
            c, 0, s, 0,
            0, 1, 0, 0,
            -s, 0, c, 0);
    }

    public static Pose3x4 RotationZ(double degrees)
    {
        var (s, c) = Math.SinCos(degrees * Math.PI / 180.0);
        return new(
            c, -s, 0, 0,
            s, c, 0, 0,
            0, 0, 1, 0);
    }

    /// <summary>a * b。b を適用してから a を適用する変換。</summary>
    public static Pose3x4 operator *(Pose3x4 a, Pose3x4 b) => new(
        a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20,
        a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21,
        a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22,
        a.M00 * b.M03 + a.M01 * b.M13 + a.M02 * b.M23 + a.M03,
        a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20,
        a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21,
        a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22,
        a.M10 * b.M03 + a.M11 * b.M13 + a.M12 * b.M23 + a.M13,
        a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20,
        a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21,
        a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22,
        a.M20 * b.M03 + a.M21 * b.M13 + a.M22 * b.M23 + a.M23);

    public Vec3 TransformPoint(Vec3 p) => new(
        M00 * p.X + M01 * p.Y + M02 * p.Z + M03,
        M10 * p.X + M11 * p.Y + M12 * p.Z + M13,
        M20 * p.X + M21 * p.Y + M22 * p.Z + M23);

    public Vec3 TransformDirection(Vec3 d) => new(
        M00 * d.X + M01 * d.Y + M02 * d.Z,
        M10 * d.X + M11 * d.Y + M12 * d.Z,
        M20 * d.X + M21 * d.Y + M22 * d.Z);
}
