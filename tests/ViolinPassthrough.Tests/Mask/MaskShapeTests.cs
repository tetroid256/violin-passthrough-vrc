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

    [Theory]
    [InlineData(0.002)]
    [InlineData(-0.002)]
    [InlineData(0.0005)]
    public void Ellipse_DistanceIsAccurateOffAxis(double offset)
    {
        var e = new EllipseShape(0.255, 0.155);

        for (var deg = 5; deg < 360; deg += 10)
        {
            // 縁上の点から法線方向に offset だけずらした点。真の距離は offset。
            var t = deg * Math.PI / 180;
            var (bx, by) = (e.HalfWidth * Math.Cos(t), e.HalfHeight * Math.Sin(t));
            var (nx, ny) = (bx / (e.HalfWidth * e.HalfWidth), by / (e.HalfHeight * e.HalfHeight));
            var len = Math.Sqrt(nx * nx + ny * ny);
            var (x, y) = (bx + offset * nx / len, by + offset * ny / len);

            Assert.Equal(offset, e.SignedDistance(x, y), 4);
            Assert.InRange(e.SignedDistance(x, y) / offset, 0.97, 1.03);
        }
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
    public void RoundedRect_InvalidSize_DoesNotThrow()
    {
        Assert.Equal(0, new RoundedRectShape(-0.15, 0.10, 0.05).CornerRadius);
        Assert.Equal(0, new RoundedRectShape(0.15, 0.10, -1).CornerRadius);
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
