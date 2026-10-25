using ViolinPassthrough.Mask;

namespace ViolinPassthrough.Tests.Mask;

public class MaskShapeTests
{
    [Fact]
    public void Ellipse_SignIsNegativeInsideAndPositiveOutside()
    {
        var e = new EllipseShape(0.2, 0.1);

        Assert.True(e.SignedDistance(0, 0) < 0);
        Assert.True(e.SignedDistance(0.19, 0) < 0);
        Assert.True(e.SignedDistance(0, 0.09) < 0);
        Assert.True(e.SignedDistance(0.21, 0) > 0);
        Assert.True(e.SignedDistance(0, 0.11) > 0);
        // 箱の角は楕円の外。
        Assert.True(e.SignedDistance(0.19, 0.09) > 0);
    }

    [Theory]
    [InlineData(0.2, 0.0)]
    [InlineData(0.0, 0.1)]
    [InlineData(-0.2, 0.0)]
    [InlineData(0.0, -0.1)]
    public void Ellipse_DistanceIsZeroOnBoundary(double x, double y)
    {
        var e = new EllipseShape(0.2, 0.1);

        Assert.Equal(0.0, e.SignedDistance(x, y), 12);
    }

    [Fact]
    public void Ellipse_DistanceIsAccurateNearBoundary()
    {
        var e = new EllipseShape(0.2, 0.1);

        // 軸上では真の距離が分かる。縁から 1mm。
        Assert.Equal(0.001, e.SignedDistance(0.201, 0), 4);
        Assert.Equal(-0.001, e.SignedDistance(0.199, 0), 4);
        Assert.Equal(0.001, e.SignedDistance(0, 0.101), 4);
    }

    [Fact]
    public void Ellipse_Inflate_AddsMarginToBothAxes()
    {
        var e = new EllipseShape(0.2, 0.1).Inflate(0.03);

        Assert.Equal(new EllipseShape(0.23, 0.13), e);
    }

    [Fact]
    public void RoundedRect_DistanceOnEdgesAndCorners()
    {
        var r = new RoundedRectShape(0.15, 0.10, 0.05);

        Assert.Equal(-0.10, r.SignedDistance(0, 0), 12);
        Assert.Equal(0.0, r.SignedDistance(0.15, 0), 12);
        Assert.Equal(0.0, r.SignedDistance(0, -0.10), 12);
        Assert.Equal(0.01, r.SignedDistance(0.16, 0), 12);
        // 角: 円弧の中心 (0.10, 0.05) から半径 0.05。
        var d = 0.05 / Math.Sqrt(2);
        Assert.Equal(0.0, r.SignedDistance(0.10 + d, 0.05 + d), 12);
        // 箱の角は丸めた分だけ外。
        Assert.Equal(0.05 * Math.Sqrt(2) - 0.05, r.SignedDistance(0.15, 0.10), 12);
    }

    [Fact]
    public void RoundedRect_ClampsRadiusToHalfOfShortSide()
    {
        var r = new RoundedRectShape(0.15, 0.10, 1.0);

        Assert.Equal(0.10, r.CornerRadius);
    }

    [Fact]
    public void RoundedRect_InflateMatchesOffsetDistance()
    {
        var r = new RoundedRectShape(0.15, 0.10, 0.05);
        var inflated = r.Inflate(0.03);

        foreach (var (x, y) in new[] { (0.2, 0.0), (0.0, 0.14), (0.17, 0.12), (0.3, 0.3), (0.05, 0.02) })
        {
            Assert.Equal(r.SignedDistance(x, y) - 0.03, inflated.SignedDistance(x, y), 12);
        }
    }
}
