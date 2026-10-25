using ViolinPassthrough.Anchor;
using ViolinPassthrough.Auto;
using ViolinPassthrough.Logging;
using ViolinPassthrough.Mask;

namespace ViolinPassthrough.Config;

/// <summary>プリセット。有効にする領域 ID の集合。</summary>
public sealed record PresetDefinition(string Name, IReadOnlyList<string> RegionIds);

/// <summary>OSC の設定（検証済み）。</summary>
public sealed record OscSettings(
    bool UseOscQuery,
    int FallbackListenPort,
    string VrchatSendHost,
    int VrchatSendPort,
    string EnabledParameter,
    string PresetParameter,
    string DebugParameter,
    string AutoParameter,
    string CalibrateParameter);

/// <summary>キャリブレーションの設定（検証済み）。</summary>
public sealed record CalibrationSettings(
    string RegionId,
    int CountdownSeconds,
    double PadMeters,
    double PreviewSeconds,
    bool Beep);

/// <summary>VR 内の通知表示の設定（検証済み）。</summary>
public sealed record FeedbackSettings(bool Toasts, Vec3 Position, double TextHeightMeters);

/// <summary>
/// 検証済みで、実行時の型に変換した設定。
/// </summary>
public sealed record ResolvedConfig(
    RenderSettings Render,
    bool StartEnabled,
    bool DebugVisible,
    string ActivePreset,
    IReadOnlyList<PresetDefinition> Presets,
    IReadOnlyList<MaskRegion> Regions,
    OscSettings Osc,
    AutoSettings Auto,
    CalibrationSettings Calibration,
    FeedbackSettings Feedback,
    LogLevel LogLevel)
{
    public PresetDefinition? FindPreset(string name) => Presets.FirstOrDefault(p => p.Name == name);

    public int IndexOfPreset(string name)
    {
        for (var i = 0; i < Presets.Count; i++)
        {
            if (Presets[i].Name == name)
            {
                return i;
            }
        }

        return -1;
    }
}
