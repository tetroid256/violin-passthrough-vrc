namespace ViolinPassthrough.Mask;

/// <summary>
/// マスクの形状。板の平面上の 2D 形状で、単位はメートル。
/// 座標は板の中心が原点、+X が右、+Y が上。
/// </summary>
public abstract record MaskShape
{
    /// <summary>
    /// 符号付き距離（メートル）。内側が負、外側が正。
    /// 縁のアンチエイリアスとデバッグ枠に使うので、縁の近くで正確であればよい。
    /// </summary>
    public abstract double SignedDistance(double x, double y);

    /// <summary>外側に <paramref name="margin"/> メートル広げた形状。</summary>
    public abstract MaskShape Inflate(double margin);
}

/// <summary>楕円。半径は幅・高さの半分。</summary>
public sealed record EllipseShape(double HalfWidth, double HalfHeight) : MaskShape
{
    public override double SignedDistance(double x, double y)
    {
        // 楕円の距離関数の近似（縁の近くで正確）。
        // k0 = |p / r|、k1 = |p / r^2|、d ≈ k0 (k0 - 1) / k1
        var k0 = Math.Sqrt(Square(x / HalfWidth) + Square(y / HalfHeight));
        var k1 = Math.Sqrt(Square(x / (HalfWidth * HalfWidth)) + Square(y / (HalfHeight * HalfHeight)));
        if (k1 < 1e-12)
        {
            return -Math.Min(HalfWidth, HalfHeight);
        }

        return k0 * (k0 - 1.0) / k1;
    }

    public override MaskShape Inflate(double margin) =>
        new EllipseShape(HalfWidth + margin, HalfHeight + margin);

    private static double Square(double v) => v * v;
}

/// <summary>角丸の長方形。角の半径は短辺の半分までに切り詰める。</summary>
public sealed record RoundedRectShape : MaskShape
{
    public RoundedRectShape(double halfWidth, double halfHeight, double cornerRadius)
    {
        HalfWidth = halfWidth;
        HalfHeight = halfHeight;
        // 不正な大きさでも例外を出さないように、上限が負にならないようにしてから切り詰める。
        CornerRadius = Math.Min(Math.Max(cornerRadius, 0), Math.Max(0, Math.Min(halfWidth, halfHeight)));
    }

    public double HalfWidth { get; }

    public double HalfHeight { get; }

    public double CornerRadius { get; }

    public override double SignedDistance(double x, double y)
    {
        var qx = Math.Abs(x) - (HalfWidth - CornerRadius);
        var qy = Math.Abs(y) - (HalfHeight - CornerRadius);
        var outside = Math.Sqrt(Square(Math.Max(qx, 0)) + Square(Math.Max(qy, 0)));
        var inside = Math.Min(Math.Max(qx, qy), 0);
        return outside + inside - CornerRadius;
    }

    // 距離関数をそのまま余白分ずらすのと同じ（角の半径も余白分大きくなる）。
    public override MaskShape Inflate(double margin) =>
        new RoundedRectShape(HalfWidth + margin, HalfHeight + margin, CornerRadius + margin);

    private static double Square(double v) => v * v;
}
