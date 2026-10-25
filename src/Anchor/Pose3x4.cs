namespace ViolinPassthrough.Anchor;

public readonly record struct Vec3(double X, double Y, double Z)
{
    public double Length => Math.Sqrt(Dot(this, this));

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);

    public static Vec3 operator *(Vec3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);

    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3 Cross(Vec3 a, Vec3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    /// <summary>長さ 1 にする。長さがほぼ 0 なら 0 ベクトルのまま。</summary>
    public Vec3 Normalized()
    {
        var length = Length;
        return length < 1e-12 ? default : this * (1.0 / length);
    }

    /// <summary>2 つの向きのなす角（度）。</summary>
    public static double AngleDeg(Vec3 a, Vec3 b)
    {
        var cos = Dot(a.Normalized(), b.Normalized());
        return Math.Acos(Math.Clamp(cos, -1.0, 1.0)) * 180.0 / Math.PI;
    }
}

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
    /// 軸（列）と位置から姿勢を作る。<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/> は正規直交で右手系であること。
    /// </summary>
    public static Pose3x4 FromBasis(Vec3 position, Vec3 x, Vec3 y, Vec3 z) => new(
        x.X, y.X, z.X, position.X,
        x.Y, y.Y, z.Y, position.Y,
        x.Z, y.Z, z.Z, position.Z);

    /// <summary>逆変換（回転は転置、平行移動は -Rᵀt）。</summary>
    public Pose3x4 Inverse()
    {
        var t = Translation;
        return new Pose3x4(
            M00, M10, M20, -(M00 * t.X + M10 * t.Y + M20 * t.Z),
            M01, M11, M21, -(M01 * t.X + M11 * t.Y + M21 * t.Z),
            M02, M12, M22, -(M02 * t.X + M12 * t.Y + M22 * t.Z));
    }

    /// <summary>
    /// 回転を <see cref="FromPositionEuler"/> と同じ規約のオイラー角（pitch, yaw, roll。度）に分解する。
    /// R = Ry(yaw) * Rx(-pitch) * Rz(roll) なので、M12 = sin(pitch)、M02 / M22 から yaw、M10 / M11 から roll。
    /// </summary>
    public Vec3 ToEulerDeg()
    {
        const double toDeg = 180.0 / Math.PI;
        var pitch = Math.Asin(Math.Clamp(M12, -1.0, 1.0));
        var cosPitch = Math.Cos(pitch);
        double yaw;
        double roll;
        if (Math.Abs(cosPitch) > 1e-6)
        {
            yaw = Math.Atan2(M02, M22);
            roll = Math.Atan2(M10, M11);
        }
        else
        {
            // 真上か真下を向いている（ジンバルロック）。roll を 0 として yaw に寄せる。
            yaw = Math.Atan2(-M20, M00);
            roll = 0;
        }

        return new Vec3(pitch * toDeg, yaw * toDeg, roll * toDeg);
    }

    /// <summary>
    /// 位置とオイラー角（度）から板の姿勢を作る。板は XY 平面にあり、表（見える面）は +Z を向く。
    /// <list type="bullet">
    /// <item>pitch（X 軸）: 正で板が上を向く（上端が奥へ倒れる）。右手系の X 軸まわりとは符号が逆。</item>
    /// <item>yaw（Y 軸）: 正で板が右を向く（右手系どおり）。</item>
    /// <item>roll（Z 軸）: 正で板の右端が上がる（右手系どおり）。</item>
    /// </list>
    /// R = Ry(yaw) * Rx(-pitch) * Rz(roll)（板をまず roll、次に pitch、最後に yaw で回す）。
    /// 視点より下にある板を視点に向けるとき、pitch が正になるようにしている（設計書の初期値の意図）。
    /// </summary>
    public static Pose3x4 FromPositionEuler(Vec3 position, Vec3 eulerDeg)
    {
        var rx = RotationX(-eulerDeg.X);
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
