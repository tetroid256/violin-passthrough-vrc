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
    string DebugParameter);

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
