using ViolinPassthrough.Anchor;
using ViolinPassthrough.Config;
using ViolinPassthrough.Logging;
using ViolinPassthrough.Mask;

namespace ViolinPassthrough.Feedback;

public enum StatusKind
{
    Info,
    Success,
    Error,
}

/// <summary>通知表示のオーバーレイ（OpenVR 側）。テストでは偽物に差し替える。</summary>
public interface IStatusOverlay : IDisposable
{
    /// <summary>テクスチャを HMD 基準の姿勢で表示する。</summary>
    void Show(MaskTexture texture, double widthMeters, Pose3x4 hmdRelativePose, uint sortOrder);

    void Hide();
}

/// <summary>
/// VR 内に短い文字列を出す（DESIGN.md §15.4）。表示する文字列と消す時刻を管理し、描画は <see cref="IStatusOverlay"/> に任せる。
/// </summary>
public sealed class StatusDisplay : IDisposable
{
    /// <summary>通常の文字の色。</summary>
    public static readonly (byte R, byte G, byte B, byte A) InfoColor = (255, 255, 255, 255);

    public static readonly (byte R, byte G, byte B, byte A) SuccessColor = (120, 255, 140, 255);

    public static readonly (byte R, byte G, byte B, byte A) ErrorColor = (255, 110, 90, 255);

    /// <summary>背景（半透明の黒）。</summary>
    public static readonly (byte R, byte G, byte B, byte A) BackgroundColor = (0, 0, 0, 170);

    private readonly IStatusOverlay? _overlay;
    private readonly TimeProvider _time;
    private FeedbackSettings _settings = new(true, new Vec3(0, 0.08, -0.5), 0.025);
    private uint _sortOrder = 101;
    private (string Text, StatusKind Kind)? _shown;
    private long? _hideAt;

    public StatusDisplay(IStatusOverlay? overlay, TimeProvider? timeProvider = null)
    {
        _overlay = overlay;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>今表示している文字列（テスト・ログ用）。</summary>
    public string? CurrentText => _shown?.Text;

    public void Configure(FeedbackSettings settings, uint maskSortOrder)
    {
        var changed = settings != _settings || maskSortOrder + 1 != _sortOrder;
        _settings = settings;
        _sortOrder = maskSortOrder == uint.MaxValue ? uint.MaxValue : maskSortOrder + 1;
        if (changed && _shown is { } shown)
        {
            Draw(shown.Text, shown.Kind);
        }
    }

    /// <summary>
    /// 文字列を <paramref name="duration"/> の間だけ出す。<paramref name="isToast"/> のものは、通知を切っていれば出さない。
    /// </summary>
    public void Show(string text, StatusKind kind, TimeSpan duration, bool isToast = true)
    {
        if (isToast && !_settings.Toasts)
        {
            return;
        }

        Log.Debug($"Status: {text}");
        _hideAt = _time.GetTimestamp() + (long)(duration.TotalSeconds * _time.TimestampFrequency);
        if (_shown != (text, kind))
        {
            Draw(text, kind);
        }
    }

    /// <summary>時間が来たら消す。メインループから毎フレーム呼ぶ。</summary>
    public void Update()
    {
        if (_shown is not null && _hideAt is { } hideAt && _time.GetTimestamp() >= hideAt)
        {
            Clear();
        }
    }

    public void Clear()
    {
        _hideAt = null;
        if (_shown is null)
        {
            return;
        }

        _shown = null;
        _overlay?.Hide();
    }

    public void Dispose() => _overlay?.Dispose();

    private void Draw(string text, StatusKind kind)
    {
        _shown = (text, kind);
        if (_overlay is null)
        {
            return;
        }

        var color = kind switch
        {
            StatusKind.Success => SuccessColor,
            StatusKind.Error => ErrorColor,
            _ => InfoColor,
        };
        var texture = BitmapFont.Render(text, color, BackgroundColor);

        // 文字の高さ（GlyphHeight の点）が TextHeightMeters になるように、板の幅を決める。
        var metersPerDot = _settings.TextHeightMeters / BitmapFont.GlyphHeight;
        var widthMeters = BitmapFont.MeasureDots(text) * metersPerDot;
        var pose = Pose3x4.Identity with
        {
            M03 = _settings.Position.X,
            M13 = _settings.Position.Y,
            M23 = _settings.Position.Z,
        };
        _overlay.Show(texture, widthMeters, pose, _sortOrder);
    }
}
