using ViolinPassthrough.Anchor;
using ViolinPassthrough.Mask;

namespace ViolinPassthrough.Tests.Mask;

public class MaskOverlayMathTests
{
    [Fact]
    public void ToHmdMatrix_IsRowMajorWithTranslationInLastColumn()
    {
        var pose = new Pose3x4(
            1, 2, 3, 4,
            5, 6, 7, 8,
            9, 10, 11, 12);

        var m = MaskOverlay.ToHmdMatrix(pose);

        Assert.Equal(
            new float[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 },
            new[] { m.m0, m.m1, m.m2, m.m3, m.m4, m.m5, m.m6, m.m7, m.m8, m.m9, m.m10, m.m11 });
    }

    [Fact]
    public void ToHmdMatrix_PutsPositionIntoM3M7M11()
    {
        var pose = Pose3x4.FromPositionEuler(new Vec3(-0.15, -0.15, -0.40), new Vec3(15, 20, -15));

        var m = MaskOverlay.ToHmdMatrix(pose);

        Assert.Equal(-0.15f, m.m3);
        Assert.Equal(-0.15f, m.m7);
        Assert.Equal(-0.40f, m.m11);
    }

    [Fact]
    public void TexelAspect_RestoresExactQuadAspect()
    {
        var region = new MaskRegion("r", new HmdAnchorSpec(), new EllipseShape(0.225, 0.125), default, default, 0.45, 0.25, 0.03);
        var texture = MaskRasterizer.Rasterize(region, new RenderSettings(new Rgb(255, 0, 255), EdgeMode.Hard, 512, 100, false));

        var aspect = MaskOverlay.TexelAspect(region, texture);

        // OpenVR は 高さ = 幅 × (画素の高さ / 画素の幅) / テクセル比 として表示する。
        var displayedHeight = region.QuadWidthMeters * texture.Height / texture.Width / aspect;
        Assert.Equal(region.QuadHeightMeters, displayedHeight, 9);
        Assert.InRange(aspect, 0.99, 1.01);
    }
}
