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
        Assert.Equal((0, 0, 0, 0), tex.GetPixel(0, 0));
        Assert.Equal((0, 0, 0, 0), tex.GetPixel(tex.Width - 1, 0));
        Assert.Equal((0, 0, 0, 0), tex.GetPixel(0, tex.Height - 1));
        Assert.Equal((0, 0, 0, 0), tex.GetPixel(tex.Width - 1, tex.Height - 1));
    }

    [Fact]
    public void Hard_HasOnlyFullyOpaqueOrFullyTransparentPixels()
    {
        var tex = MaskRasterizer.Rasterize(Region(), Settings());

        for (var i = 0; i < tex.Rgba.Length; i += 4)
        {
            var a = tex.Rgba[i + 3];
            Assert.True(a is 0 or 255, $"Unexpected alpha {a} at pixel {i / 4}");
            if (a == 255)
            {
                Assert.Equal(Magenta, new Rgb(tex.Rgba[i], tex.Rgba[i + 1], tex.Rgba[i + 2]));
            }
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
    public void Aa_HasIntermediateAlphaOnlyNearTheEdge()
    {
        var tex = MaskRasterizer.Rasterize(Region(), Settings(EdgeMode.Aa));
        var partial = 0;
        for (var i = 0; i < tex.Rgba.Length; i += 4)
        {
            var a = tex.Rgba[i + 3];
            if (a is > 0 and < 255)
            {
                partial++;
                Assert.Equal(Magenta, new Rgb(tex.Rgba[i], tex.Rgba[i + 1], tex.Rgba[i + 2]));
            }
        }

        Assert.True(partial > 0);
        // 縁は周長 × 約 1.5px 程度。全体の数 % に収まる。
        Assert.True(partial < tex.Width * tex.Height * 0.03, $"Too many partial pixels: {partial}");
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
        Assert.Equal((0, 0, 0, 0), tex.GetPixel(0, 0));
        // キー色の画素は1つもない（抜けない）。
        for (var i = 0; i < tex.Rgba.Length; i += 4)
        {
            Assert.False(tex.Rgba[i] == 255 && tex.Rgba[i + 1] == 0 && tex.Rgba[i + 2] == 255 && tex.Rgba[i + 3] == 255);
        }
    }
}
