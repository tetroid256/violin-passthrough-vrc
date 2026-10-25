using ViolinPassthrough.Anchor;
using ViolinPassthrough.Mask;

namespace ViolinPassthrough.Tests.Mask;

public class PolygonShapeTests
{
    private static readonly PolygonShape Square = new(new[] { (-0.1, -0.1), (0.1, -0.1), (0.1, 0.1), (-0.1, 0.1) });

    [Theory]
    [InlineData(0, 0, -0.1)]
    [InlineData(0.05, 0, -0.05)]
    [InlineData(0.1, 0, 0)]
    [InlineData(0.15, 0, 0.05)]
    [InlineData(0, -0.13, 0.03)]
    public void Square_DistanceMatchesRectangle(double x, double y, double expected)
    {
        Assert.Equal(expected, Square.SignedDistance(x, y), 12);
    }

    [Fact]
    public void Square_CornerDistanceIsEuclidean()
    {
        Assert.Equal(Math.Sqrt(2) * 0.03, Square.SignedDistance(0.13, 0.13), 12);
    }

    [Fact]
    public void Orientation_DoesNotMatter()
    {
        var clockwise = new PolygonShape(Square.Vertices.Reverse().ToArray());

        foreach (var (x, y) in new[] { (0.0, 0.0), (0.2, 0.05), (-0.05, 0.09) })
        {
            Assert.Equal(Square.SignedDistance(x, y), clockwise.SignedDistance(x, y), 12);
        }
    }

    [Fact]
    public void ConcaveShape_NotchIsOutside()
    {
        // L 字: 右上の四角が欠けている。
        var l = new PolygonShape(new[] { (-0.2, -0.2), (0.2, -0.2), (0.2, 0.0), (0.0, 0.0), (0.0, 0.2), (-0.2, 0.2) });

        Assert.True(l.SignedDistance(-0.1, 0.1) < 0);
        Assert.True(l.SignedDistance(0.1, -0.1) < 0);
        Assert.Equal(0.05, l.SignedDistance(0.1, 0.05), 12);
    }

    [Fact]
    public void Inflate_OffsetsDistance()
    {
        var inflated = Square.Inflate(0.03);

        Assert.Equal(Square.SignedDistance(0.2, 0.05) - 0.03, inflated.SignedDistance(0.2, 0.05), 12);
        Assert.Equal(Square.SignedDistance(0.2, 0.05) - 0.05, inflated.Inflate(0.02).SignedDistance(0.2, 0.05), 12);
        Assert.Same(Square, Square.Inflate(0));
    }

    [Fact]
    public void Equality_IsByVertices()
    {
        var copy = new PolygonShape(Square.Vertices.ToList());

        Assert.Equal(Square, copy);
        Assert.Equal(Square.GetHashCode(), copy.GetHashCode());
        Assert.NotEqual(Square, new PolygonShape(new[] { (-0.1, -0.1), (0.1, -0.1), (0.0, 0.1) }));
    }

    [Fact]
    public void TooFewVertices_Throws()
    {
        Assert.Throws<ArgumentException>(() => new PolygonShape(new[] { (0.0, 0.0), (1.0, 1.0) }));
    }

    [Fact]
    public void Rasterize_TopRowIsTheTopOfThePlate()
    {
        // 上向きの三角形: 上の行ほど塗られた画素が少ない。
        var triangle = new PolygonShape(new[] { (0.0, 0.1), (-0.1, -0.1), (0.1, -0.1) });
        var region = new MaskRegion("t", new HmdAnchorSpec(), triangle, default, default, 0.2, 0.2, 0);
        var tex = MaskRasterizer.Rasterize(region, new RenderSettings(new Rgb(255, 0, 255), EdgeMode.Hard, 64, 100, false));

        int Filled(int row) => Enumerable.Range(0, tex.Width).Count(x => tex.GetPixel(x, row).A == 255);

        Assert.True(Filled(4) < Filled(32));
        Assert.True(Filled(32) < Filled(60));
    }
}
