using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ViolinPassthrough.Anchor;
using ViolinPassthrough.Logging;
using ViolinPassthrough.Mask;

namespace ViolinPassthrough.Config;

public sealed record ConfigParseResult(ResolvedConfig? Config, IReadOnlyList<string> Errors)
{
    public bool Success => Config is not null;
}

/// <summary>設定の JSON を読み、検証して <see cref="ResolvedConfig"/> に変換する。</summary>
public static partial class ConfigValidator
{
    public const int MaxRegionIdLength = 64;
    public const double MaxSizeMeters = 10.0;
    public const double MaxMarginMeters = 1.0;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // 綴り間違いを見逃さないように、知らない項目はエラーにする。
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public static ConfigParseResult Parse(string json)
    {
        AppConfig? raw;
        try
        {
            raw = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return Fail($"Invalid JSON: {ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            return Fail($"Invalid JSON: {ex.Message}");
        }

        return raw is null ? Fail("Config is null.") : Validate(raw);
    }

    public static ConfigParseResult Validate(AppConfig raw)
    {
        var errors = new List<string>();

        var keyColor = ValidateColor(raw.KeyColor, errors);
        var edgeMode = ParseEdgeMode(raw.EdgeMode, errors);

        if (raw.TextureLongSide is < RenderSettings.MinTextureLongSide or > RenderSettings.MaxTextureLongSide)
        {
            errors.Add($"textureLongSide must be between {RenderSettings.MinTextureLongSide} and {RenderSettings.MaxTextureLongSide} (got {raw.TextureLongSide}).");
        }

        if (raw.SortOrder is < 0 or > uint.MaxValue)
        {
            errors.Add($"sortOrder must be between 0 and {uint.MaxValue} (got {raw.SortOrder}).");
        }

        var regions = ValidateRegions(raw.Regions, errors);
        var presets = ValidatePresets(raw.Presets, regions, errors);

        var activePreset = raw.ActivePreset ?? string.Empty;
        if (presets.Count > 0 && presets.All(p => p.Name != activePreset))
        {
            errors.Add($"activePreset \"{activePreset}\" is not defined in presets.");
        }

        var osc = ValidateOsc(raw.Osc, errors);
        var logLevel = ParseLogLevel(raw.LogLevel, errors);

        if (errors.Count > 0)
        {
            return new ConfigParseResult(null, errors);
        }

        var render = new RenderSettings(keyColor, edgeMode, raw.TextureLongSide, (uint)raw.SortOrder, Debug: false);
        var config = new ResolvedConfig(
            render,
            raw.StartEnabled,
            raw.DebugVisible,
            activePreset,
            presets,
            regions,
            osc!,
            logLevel);
        return new ConfigParseResult(config, Array.Empty<string>());
    }

    private static ConfigParseResult Fail(string error) => new(null, new[] { error });

    private static Rgb ValidateColor(ColorConfig? color, List<string> errors)
    {
        if (color is null)
        {
            errors.Add("keyColor is required.");
            return default;
        }

        foreach (var (name, value) in new[] { ("r", color.R), ("g", color.G), ("b", color.B) })
        {
            if (value is < 0 or > 255)
            {
                errors.Add($"keyColor.{name} must be between 0 and 255 (got {value}).");
            }
        }

        return new Rgb((byte)Math.Clamp(color.R, 0, 255), (byte)Math.Clamp(color.G, 0, 255), (byte)Math.Clamp(color.B, 0, 255));
    }

    private static EdgeMode ParseEdgeMode(string? value, List<string> errors)
    {
        switch (value?.ToLowerInvariant())
        {
            case "hard":
                return EdgeMode.Hard;
            case "aa":
                return EdgeMode.Aa;
            default:
                errors.Add($"edgeMode must be \"hard\" or \"aa\" (got \"{value}\").");
                return EdgeMode.Hard;
        }
    }

    private static LogLevel ParseLogLevel(string? value, List<string> errors)
    {
        switch (value?.ToLowerInvariant())
        {
            case "debug":
                return LogLevel.Debug;
            case "info":
                return LogLevel.Info;
            case "warning":
            case "warn":
                return LogLevel.Warning;
            case "error":
                return LogLevel.Error;
            default:
                errors.Add($"logLevel must be one of Debug, Info, Warning, Error (got \"{value}\").");
                return LogLevel.Info;
        }
    }

    private static List<MaskRegion> ValidateRegions(List<RegionConfig>? regions, List<string> errors)
    {
        var result = new List<MaskRegion>();
        if (regions is null || regions.Count == 0)
        {
            errors.Add("regions must contain at least one region.");
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            if (region is null)
            {
                errors.Add($"regions[{i}] is null.");
                continue;
            }

            var label = string.IsNullOrEmpty(region.Id) ? $"regions[{i}]" : $"region \"{region.Id}\"";
            var before = errors.Count;

            if (string.IsNullOrEmpty(region.Id) || !RegionIdPattern().IsMatch(region.Id))
            {
                errors.Add($"{label}: id must be 1-{MaxRegionIdLength} characters of A-Z, a-z, 0-9, '_' or '-'.");
            }
            else if (!seen.Add(region.Id))
            {
                errors.Add($"{label}: duplicate id.");
            }

            var anchor = ValidateAnchor(region.Anchor, label, errors);
            var position = ValidateVector(region.Position, "position", label, required: true, errors);
            var rotation = ValidateVector(region.RotationEulerDeg, "rotationEulerDeg", label, required: false, errors);

            ValidateRange(region.WidthMeters, "widthMeters", label, minExclusive: 0, MaxSizeMeters, errors);
            ValidateRange(region.HeightMeters, "heightMeters", label, minExclusive: 0, MaxSizeMeters, errors);
            if (!double.IsFinite(region.MarginMeters) || region.MarginMeters < 0 || region.MarginMeters > MaxMarginMeters)
            {
                errors.Add($"{label}: marginMeters must be between 0 and {MaxMarginMeters} (got {region.MarginMeters}).");
            }

            var shape = ValidateShape(region.Shape, region.WidthMeters, region.HeightMeters, label, errors);

            if (errors.Count == before && shape is not null && anchor is not null)
            {
                result.Add(new MaskRegion(
                    region.Id!,
                    anchor,
                    shape,
                    position,
                    rotation,
                    region.WidthMeters,
                    region.HeightMeters,
                    region.MarginMeters));
            }
        }

        return result;
    }

    private static AnchorSpec? ValidateAnchor(AnchorConfig? anchor, string label, List<string> errors)
    {
        if (anchor is null)
        {
            return new HmdAnchorSpec();
        }

        switch (anchor.Type?.ToLowerInvariant())
        {
            case "hmd":
                return new HmdAnchorSpec();
            default:
                errors.Add($"{label}: anchor.type must be \"hmd\" (got \"{anchor.Type}\").");
                return null;
        }
    }

    private static MaskShape? ValidateShape(ShapeConfig? shape, double width, double height, string label, List<string> errors)
    {
        var type = shape?.Type ?? "ellipse";
        switch (type.ToLowerInvariant())
        {
            case "ellipse":
                if (shape?.CornerRadius is not null)
                {
                    errors.Add($"{label}: shape.cornerRadius is only valid for roundedRect.");
                }

                return new EllipseShape(width / 2, height / 2);

            case "roundedrect":
            {
                var radius = shape?.CornerRadius ?? 0;
                if (!double.IsFinite(radius) || radius < 0)
                {
                    errors.Add($"{label}: shape.cornerRadius must be >= 0 (got {radius}).");
                    return null;
                }

                return new RoundedRectShape(width / 2, height / 2, radius);
            }

            default:
                errors.Add($"{label}: shape.type must be \"ellipse\" or \"roundedRect\" (got \"{type}\").");
                return null;
        }
    }

    private static Vec3 ValidateVector(double[]? values, string name, string label, bool required, List<string> errors)
    {
        if (values is null)
        {
            if (required)
            {
                errors.Add($"{label}: {name} is required ([x, y, z]).");
            }

            return default;
        }

        if (values.Length != 3 || values.Any(v => !double.IsFinite(v)))
        {
            errors.Add($"{label}: {name} must be an array of 3 finite numbers.");
            return default;
        }

        return new Vec3(values[0], values[1], values[2]);
    }

    private static void ValidateRange(double value, string name, string label, double minExclusive, double maxInclusive, List<string> errors)
    {
        if (!double.IsFinite(value) || value <= minExclusive || value > maxInclusive)
        {
            errors.Add($"{label}: {name} must be greater than {minExclusive} and at most {maxInclusive} (got {value}).");
        }
    }

    private static List<PresetDefinition> ValidatePresets(List<PresetConfig>? presets, List<MaskRegion> regions, List<string> errors)
    {
        var result = new List<PresetDefinition>();
        if (presets is null || presets.Count == 0)
        {
            errors.Add("presets must contain at least one preset.");
            return result;
        }

        var regionIds = regions.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var preset in presets)
        {
            if (string.IsNullOrEmpty(preset.Name))
            {
                errors.Add("presets: preset name must not be empty.");
                continue;
            }

            if (!names.Add(preset.Name))
            {
                errors.Add($"preset \"{preset.Name}\": duplicate name.");
                continue;
            }

            var ids = new List<string>();
            foreach (var id in preset.Regions)
            {
                if (id is null || !regionIds.Contains(id))
                {
                    errors.Add($"preset \"{preset.Name}\": unknown region id \"{id}\".");
                }
                else if (!ids.Contains(id))
                {
                    ids.Add(id);
                }
            }

            result.Add(new PresetDefinition(preset.Name, ids));
        }

        return result;
    }

    private static OscSettings? ValidateOsc(OscConfig? osc, List<string> errors)
    {
        if (osc is null)
        {
            errors.Add("osc is required.");
            return null;
        }

        ValidatePort(osc.FallbackListenPort, "osc.fallbackListenPort", errors);
        ValidatePort(osc.VrchatSendPort, "osc.vrchatSendPort", errors);
        if (string.IsNullOrWhiteSpace(osc.VrchatSendHost))
        {
            errors.Add("osc.vrchatSendHost must not be empty.");
        }

        var parameters = osc.Parameters;
        if (parameters is null)
        {
            errors.Add("osc.parameters is required.");
            return null;
        }

        var names = new[] { ("enabled", parameters.Enabled), ("preset", parameters.Preset), ("debug", parameters.Debug) };
        foreach (var (key, value) in names)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace))
            {
                errors.Add($"osc.parameters.{key} must be a non-empty name without spaces (got \"{value}\").");
            }
        }

        if (names.Select(n => n.Item2).Distinct(StringComparer.Ordinal).Count() != names.Length)
        {
            errors.Add("osc.parameters must use different names for enabled, preset and debug.");
        }

        return new OscSettings(
            osc.UseOscQuery,
            osc.FallbackListenPort,
            osc.VrchatSendHost ?? string.Empty,
            osc.VrchatSendPort,
            parameters.Enabled ?? string.Empty,
            parameters.Preset ?? string.Empty,
            parameters.Debug ?? string.Empty);
    }

    private static void ValidatePort(int port, string name, List<string> errors)
    {
        if (port is < 1 or > 65535)
        {
            errors.Add($"{name} must be between 1 and 65535 (got {port}).");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex RegionIdPattern();
}
