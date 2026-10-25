namespace ViolinPassthrough.Mask;

public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>縁の処理。</summary>
public enum EdgeMode
{
    /// <summary>アンチエイリアスなし。中間色ができないので、キーの抜けが安定する。</summary>
    Hard,

    /// <summary>縁に約 1.5px のアンチエイリアス。縁取りが残る可能性がある。</summary>
    Aa,
}

/// <summary>全領域に共通する描画設定。</summary>
public sealed record RenderSettings(
    Rgb KeyColor,
    EdgeMode EdgeMode,
    int TextureLongSide,
    uint SortOrder,
    bool Debug)
{
    public const int MinTextureLongSide = 16;
    public const int MaxTextureLongSide = 2048;
}

/// <summary>RGBA（1画素 4 バイト、行は上から下）のテクスチャ。</summary>
public sealed class MaskTexture
{
    public MaskTexture(int width, int height, byte[] rgba)
    {
        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException("Buffer size does not match the texture size.", nameof(rgba));
        }

        Width = width;
        Height = height;
        Rgba = rgba;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Rgba { get; }

    public (byte R, byte G, byte B, byte A) GetPixel(int x, int y)
    {
        var i = (y * Width + x) * 4;
        return (Rgba[i], Rgba[i + 1], Rgba[i + 2], Rgba[i + 3]);
    }
}

/// <summary>形状 → RGBA バッファ。</summary>
public static class MaskRasterizer
{
    /// <summary>aa モードで不透明から透明に変わるまでの幅（px）。</summary>
    public const double AaWidthPixels = 1.5;

    /// <summary>デバッグ表示の塗り（半透明の赤）。</summary>
    public static readonly (byte R, byte G, byte B, byte A) DebugFill = (255, 0, 0, 110);

    /// <summary>デバッグ表示の枠（白）。</summary>
    public static readonly (byte R, byte G, byte B, byte A) DebugBorder = (255, 255, 255, 255);

    /// <summary>
    /// テクスチャの画素数を決める。長辺を <paramref name="longSide"/> にし、短辺は縦横比に合わせる。
    /// </summary>
    public static (int Width, int Height) ComputeTextureSize(double widthMeters, double heightMeters, int longSide)
    {
        if (widthMeters <= 0 || heightMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthMeters), "Size must be positive.");
        }

        longSide = Math.Clamp(longSide, RenderSettings.MinTextureLongSide, RenderSettings.MaxTextureLongSide);
        if (widthMeters >= heightMeters)
        {
            var h = (int)Math.Round(longSide * heightMeters / widthMeters);
            return (longSide, Math.Max(1, h));
        }

        var w = (int)Math.Round(longSide * widthMeters / heightMeters);
        return (Math.Max(1, w), longSide);
    }

    /// <summary>
    /// 領域のマスクをラスタライズする。板の全面（余白込み）がテクスチャ全体に対応する。
    /// </summary>
    public static MaskTexture Rasterize(MaskRegion region, RenderSettings settings) =>
        Rasterize(region.EffectiveShape, region.QuadWidthMeters, region.QuadHeightMeters, settings);

    public static MaskTexture Rasterize(MaskShape shape, double quadWidthMeters, double quadHeightMeters, RenderSettings settings)
    {
        var (width, height) = ComputeTextureSize(quadWidthMeters, quadHeightMeters, settings.TextureLongSide);
        var rgba = new byte[width * height * 4];

        // 画素 1 つ分の大きさ（メートル）。縦横で僅かに違うので、距離の換算には平均を使う。
        var texelW = quadWidthMeters / width;
        var texelH = quadHeightMeters / height;
        var pixelsPerMeter = 2.0 / (texelW + texelH);
        var borderPixels = Math.Max(2.0, Math.Round(Math.Max(width, height) / 170.0));

        for (var py = 0; py < height; py++)
        {
            // 行 0 が板の上端。
            var y = quadHeightMeters / 2 - (py + 0.5) * texelH;
            for (var px = 0; px < width; px++)
            {
                var x = (px + 0.5) * texelW - quadWidthMeters / 2;
                var d = shape.SignedDistance(x, y) * pixelsPerMeter;
                var color = settings.Debug
                    ? DebugColor(d, borderPixels)
                    : KeyColor(d, settings.KeyColor, settings.EdgeMode);

                var i = (py * width + px) * 4;
                rgba[i] = color.R;
                rgba[i + 1] = color.G;
                rgba[i + 2] = color.B;
                rgba[i + 3] = color.A;
            }
        }

        return new MaskTexture(width, height, rgba);
    }

    private static (byte R, byte G, byte B, byte A) KeyColor(double distancePixels, Rgb key, EdgeMode mode)
    {
        byte alpha;
        if (mode == EdgeMode.Hard)
        {
            alpha = distancePixels <= 0 ? (byte)255 : (byte)0;
        }
        else
        {
            var coverage = Math.Clamp(0.5 - distancePixels / AaWidthPixels, 0.0, 1.0);
            alpha = (byte)Math.Round(coverage * 255);
        }

        // 外側（α=0）の色は使われないが、念のため黒にしておく。
        return alpha == 0 ? ((byte)0, (byte)0, (byte)0, (byte)0) : (key.R, key.G, key.B, alpha);
    }

    private static (byte R, byte G, byte B, byte A) DebugColor(double distancePixels, double borderPixels)
    {
        if (distancePixels > 0)
        {
            return (0, 0, 0, 0);
        }

        return distancePixels > -borderPixels ? DebugBorder : DebugFill;
    }
}
