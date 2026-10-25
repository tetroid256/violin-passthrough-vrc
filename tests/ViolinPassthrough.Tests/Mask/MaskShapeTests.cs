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
}
