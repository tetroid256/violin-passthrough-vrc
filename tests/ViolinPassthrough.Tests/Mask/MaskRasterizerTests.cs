using ViolinPassthrough.Anchor;
using ViolinPassthrough.Mask;

namespace ViolinPassthrough.Tests.Mask;

public class MaskRasterizerTests
{
    private static readonly Rgb Magenta = new(255, 0, 255);

    private static RenderSettings Settings(EdgeMode mode = EdgeMode.Hard, bool debug = false, int longSide = 512) =>
        new(Magenta, mode, longSide, 100, debug);

    private static MaskRegion Region(double width = 0.45, double height = 0.25, double margin = 0.03) =>
        new("test", new HmdAnchorSpec(), new EllipseShape(width / 2, height / 2), default, default, width, height, margin);

    [Theory]
    [InlineData(0.51, 0.31, 512, 512, 311)]
    [InlineData(0.31, 0.51, 512, 311, 512)]
    [InlineData(1.0, 1.0, 256, 256, 256)]
    [InlineData(1.0, 0.0001, 512, 512, 1)]
    public void ComputeTextureSize_KeepsAspectWithLongSide(double w, double h, int longSide, int expectedW, int expectedH)
    {
        var (tw, th) = MaskRasterizer.ComputeTextureSize(w, h, longSide);

        Assert.Equal(expectedW, tw);
        Assert.Equal(expectedH, th);
    }

    [Fact]
    public void ComputeTextureSize_ClampsLongSide()
    {
        Assert.Equal(RenderSettings.MaxTextureLongSide, MaskRasterizer.ComputeTextureSize(1, 1, 100_000).Width);
        Assert.Equal(RenderSettings.MinTextureLongSide, MaskRasterizer.ComputeTextureSize(1, 1, 1).Width);
    }

    [Fact]
    public void Hard_CenterIsOpaqueKeyColorAndCornersAreTransparent()
    {
        var tex = MaskRasterizer.Rasterize(Region(), Settings());

        Assert.Equal(512, tex.Width);
        Assert.Equal((255, 0, 255, 255), tex.GetPixel(tex.Width / 2, tex.Height / 2));
        Assert.Equal((255, 0, 255, 0), tex.GetPixel(0, 0));
        Assert.Equal((255, 0, 255, 0), tex.GetPixel(tex.Width - 1, 0));
        Assert.Equal((255, 0, 255, 0), tex.GetPixel(0, tex.Height - 1));
        Assert.Equal((255, 0, 255, 0), tex.GetPixel(tex.Width - 1, tex.Height - 1));
    }

    [Fact]
    public void Hard_HasOnlyFullyOpaqueOrFullyTransparentPixels_AllInKeyColor()
    {
        var tex = MaskRasterizer.Rasterize(Region(), Settings());

        for (var i = 0; i < tex.Rgba.Length; i += 4)
        {
            var a = tex.Rgba[i + 3];
            Assert.True(a is 0 or 255, $"Unexpected alpha {a} at pixel {i / 4}");
            // 透明な画素もキー色にする（テクスチャの補間で縁に別の色が混ざらないように）。
            Assert.Equal(Magenta, new Rgb(tex.Rgba[i], tex.Rgba[i + 1], tex.Rgba[i + 2]));
        }
    }

    [Fact]
    public void Hard_MarginExtendsShapeToQuadEdges()
    {
        // 余白込みの楕円は板の全面に内接する: 中央の行の左右端まで抜ける。
        var tex = MaskRasterizer.Rasterize(Region(), Settings());
        var midRow = tex.Height / 2;

        Assert.Equal(255, tex.GetPixel(1, midRow).A);
        Assert.Equal(255, tex.GetPixel(tex.Width - 2, midRow).A);
    }

    [Fact]
    public void Hard_FilledAreaMatchesEllipseArea()
    {
        var region = Region();
        var tex = MaskRasterizer.Rasterize(region, Settings());
        var filled = 0;
        for (var i = 3; i < tex.Rgba.Length; i += 4)
        {
            if (tex.Rgba[i] == 255)
            {
                filled++;
            }
        }

        var expected = Math.PI / 4 * tex.Width * tex.Height;
        Assert.InRange(filled, expected * 0.99, expected * 1.01);
    }

    [Fact]
    public void Aa_HasIntermediateAlphaOnlyWithinTheEdgeBand()
    {
        var region = Region();
        var tex = MaskRasterizer.Rasterize(region, Settings(EdgeMode.Aa));
        var shape = region.EffectiveShape;
        var texel = region.QuadWidthMeters / tex.Width;
        var partial = 0;

        for (var py = 0; py < tex.Height; py++)
        {
            for (var px = 0; px < tex.Width; px++)
            {
                var (r, g, b, a) = tex.GetPixel(px, py);
                Assert.Equal(Magenta, new Rgb(r, g, b));
                if (a is 0 or 255)
                {
                    continue;
                }

                partial++;
                var x = (px + 0.5) * texel - region.QuadWidthMeters / 2;
                var y = region.QuadHeightMeters / 2 - (py + 0.5) * region.QuadHeightMeters / tex.Height;
                var distancePixels = shape.SignedDistance(x, y) / texel;
                Assert.True(
                    Math.Abs(distancePixels) <= MaskRasterizer.AaWidthPixels / 2 + 0.01,
                    $"Partial alpha {a} at {distancePixels:0.00}px from the edge");
            }
        }

        Assert.True(partial > 0);
        Assert.Equal(255, tex.GetPixel(tex.Width / 2, tex.Height / 2).A);
        Assert.Equal(0, tex.GetPixel(0, 0).A);
    }

    [Fact]
    public void Debug_UsesTranslucentRedFillAndWhiteBorder()
    {
        var tex = MaskRasterizer.Rasterize(Region(), Settings(debug: true));
        var midRow = tex.Height / 2;

        Assert.Equal(MaskRasterizer.DebugFill, tex.GetPixel(tex.Width / 2, midRow));
        Assert.Equal(MaskRasterizer.DebugBorder, tex.GetPixel(1, midRow));
        Assert.Equal((255, 255, 255, 0), tex.GetPixel(0, 0));
        // キー色の画素は1つもない（抜けない）。
        for (var i = 0; i < tex.Rgba.Length; i += 4)
        {
            Assert.False(tex.Rgba[i] == 255 && tex.Rgba[i + 1] == 0 && tex.Rgba[i + 2] == 255 && tex.Rgba[i + 3] == 255);
        }
    }
}
