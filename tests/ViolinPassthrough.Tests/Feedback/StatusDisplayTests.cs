using ViolinPassthrough.Anchor;
using ViolinPassthrough.Config;
using ViolinPassthrough.Feedback;

namespace ViolinPassthrough.Tests.Feedback;

public class StatusDisplayTests
{
    private readonly ManualTimeProvider _time = new();
    private readonly FakeStatusOverlay _overlay = new();

    private StatusDisplay Create(bool toasts = true)
    {
        var display = new StatusDisplay(_overlay, _time);
        display.Configure(new FeedbackSettings(toasts, new Vec3(0, 0.08, -0.5), 0.025), 100);
        return display;
    }

    [Fact]
    public void Show_DrawsTextAndHidesAfterDuration()
    {
        var display = Create();

        display.Show("AUTO ON", StatusKind.Info, TimeSpan.FromSeconds(1));

        Assert.True(_overlay.Visible);
        Assert.Equal("AUTO ON", display.CurrentText);
        Assert.Equal(101u, _overlay.SortOrder);
        Assert.Equal(-0.5, _overlay.Pose.M23);
        // 文字の高さ 0.025 m: 1 点 = 0.025 / 7 m。
        Assert.Equal(BitmapFont.MeasureDots("AUTO ON") * 0.025 / 7, _overlay.WidthMeters, 9);

        _time.Advance(TimeSpan.FromSeconds(0.9));
        display.Update();
        Assert.True(_overlay.Visible);

        _time.Advance(TimeSpan.FromSeconds(0.2));
        display.Update();
        Assert.False(_overlay.Visible);
        Assert.Null(display.CurrentText);
    }

    [Fact]
    public void SameText_DoesNotRedraw_ButExtendsTheTime()
    {
        var display = Create();
        display.Show("X", StatusKind.Info, TimeSpan.FromSeconds(1));
        _time.Advance(TimeSpan.FromSeconds(0.8));

        display.Show("X", StatusKind.Info, TimeSpan.FromSeconds(1));
        _time.Advance(TimeSpan.FromSeconds(0.8));
        display.Update();

        Assert.Equal(1, _overlay.ShowCount);
        Assert.True(_overlay.Visible);
    }

    [Fact]
    public void ToastsCanBeDisabled_ButResultsStillShow()
    {
        var display = Create(toasts: false);

        display.Show("AUTO ON", StatusKind.Info, TimeSpan.FromSeconds(1));
        Assert.False(_overlay.Visible);

        display.Show("CALIBRATE 3", StatusKind.Info, TimeSpan.FromSeconds(1), isToast: false);
        Assert.True(_overlay.Visible);
    }

    [Fact]
    public void Colors_DependOnKind_AndNeverUseTheKeyColor()
    {
        var display = Create();

        display.Show("OK", StatusKind.Success, TimeSpan.FromSeconds(1));
        var success = Pixels(_overlay.Texture!).ToHashSet();
        display.Show("NO", StatusKind.Error, TimeSpan.FromSeconds(1));
        var failure = Pixels(_overlay.Texture!).ToHashSet();

        Assert.Contains(StatusDisplay.SuccessColor, success);
        Assert.Contains(StatusDisplay.ErrorColor, failure);
        Assert.Contains(StatusDisplay.BackgroundColor, failure);
        Assert.DoesNotContain(((byte)255, (byte)0, (byte)255, (byte)255), success.Concat(failure));
    }

    [Fact]
    public void WithoutOverlay_DoesNothing()
    {
        var display = new StatusDisplay(null, _time);

        display.Show("X", StatusKind.Info, TimeSpan.FromSeconds(1));
        display.Update();
        display.Clear();
    }

    private static IEnumerable<(byte, byte, byte, byte)> Pixels(ViolinPassthrough.Mask.MaskTexture texture)
    {
        for (var y = 0; y < texture.Height; y++)
        {
            for (var x = 0; x < texture.Width; x++)
            {
                yield return texture.GetPixel(x, y);
            }
        }
    }
}

public class BitmapFontTests
{
    private static readonly (byte, byte, byte, byte) White = (255, 255, 255, 255);
    private static readonly (byte, byte, byte, byte) Clear = (0, 0, 0, 0);

    [Fact]
    public void Render_SizeFollowsTextLength()
    {
        var texture = BitmapFont.Render("AB", White, Clear, scale: 2);

        // 2 文字 × 5 点 + 間 1 点 + 余白 2 × 2 点 = 15 点。高さ 7 + 4 = 11 点。
        Assert.Equal(15 * 2, texture.Width);
        Assert.Equal(11 * 2, texture.Height);
        Assert.Equal(15, BitmapFont.MeasureDots("AB"));
    }

    [Fact]
    public void Render_DrawsGlyphDots()
    {
        var texture = BitmapFont.Render("1", White, Clear, scale: 1);

        // "1" の 1 行目は "..#.."（余白 2 点の分だけずれる）。
        Assert.Equal(White, texture.GetPixel(2 + 2, 2));
        Assert.Equal(Clear, texture.GetPixel(2 + 0, 2));
        Assert.Equal(Clear, texture.GetPixel(0, 0));
    }

    [Fact]
    public void Lowercase_IsDrawnAsUppercase_AndUnknownAsQuestionMark()
    {
        Assert.Equal(BitmapFont.Render("ON", White, Clear).Rgba, BitmapFont.Render("on", White, Clear).Rgba);
        Assert.Equal(BitmapFont.Render("?", White, Clear).Rgba, BitmapFont.Render("é", White, Clear).Rgba);
    }

    [Fact]
    public void AllGlyphs_Are5x7()
    {
        foreach (var c in "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ -.:!?/()+")
        {
            Assert.True(BitmapFont.IsSupported(c), $"'{c}' is missing");
            var texture = BitmapFont.Render(c.ToString(), White, Clear, scale: 1);
            Assert.Equal(9, texture.Width);
            Assert.Equal(11, texture.Height);
        }
    }
}
