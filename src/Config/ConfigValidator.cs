using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ViolinPassthrough.Anchor;
using ViolinPassthrough.Auto;
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

    /// <summary>多角形の頂点数の上限。ラスタライズの時間は頂点数に比例し、メインループで行うため。</summary>
    public const int MaxPolygonVertices = 128;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // 綴り間違いを見逃さないように、知らない項目と同じ項目の重複はエラーにする。
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
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
        var auto = ValidateAuto(raw.Auto, errors);
        var calibration = ValidateCalibration(raw.Calibration, errors);
        var feedback = ValidateFeedback(raw.Feedback, errors);
        var hands = ValidateTracking(raw.Tracking, errors);
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
            auto!,
            calibration!,
            feedback!,
            hands!,
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

            var sizeOk = ValidateRange(region.WidthMeters, "widthMeters", label, minExclusive: 0, MaxSizeMeters, errors);
            sizeOk &= ValidateRange(region.HeightMeters, "heightMeters", label, minExclusive: 0, MaxSizeMeters, errors);
            if (!double.IsFinite(region.MarginMeters) || region.MarginMeters < 0 || region.MarginMeters > MaxMarginMeters)
            {
                errors.Add($"{label}: marginMeters must be between 0 and {MaxMarginMeters} (got {region.MarginMeters}).");
            }

            // 形状は大きさが正しいときだけ作る（不正な大きさで形状を作らない）。
            var shape = sizeOk ? ValidateShape(region.Shape, region.WidthMeters, region.HeightMeters, label, errors) : null;

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

        var before = errors.Count;
        var type = anchor.Type?.ToLowerInvariant();
        void Forbid(string? value, string name, string validFor)
        {
            if (value is not null)
            {
                errors.Add($"{label}: anchor.{name} is only valid for {validFor} anchors.");
            }
        }

        switch (type)
        {
            case "hmd":
                Forbid(anchor.Universe, "universe", "world");
                Forbid(anchor.Role, "role", "device");
                Forbid(anchor.Serial, "serial", "device");
                Forbid(anchor.OnLost, "onLost", "device");
                return errors.Count == before ? new HmdAnchorSpec() : null;

            case "world":
            {
                Forbid(anchor.Role, "role", "device");
                Forbid(anchor.Serial, "serial", "device");
                Forbid(anchor.OnLost, "onLost", "device");
                TrackingUniverse universe;
                switch (anchor.Universe?.ToLowerInvariant())
                {
                    case null:
                    case "standing":
                        universe = TrackingUniverse.Standing;
                        break;
                    case "seated":
                        universe = TrackingUniverse.Seated;
                        break;
                    default:
                        errors.Add($"{label}: anchor.universe must be \"standing\" or \"seated\" (got \"{anchor.Universe}\").");
                        return null;
                }

                return errors.Count == before ? new WorldAnchorSpec(universe) : null;
            }

            case "device":
            {
                Forbid(anchor.Universe, "universe", "world");
                DeviceRole? role = null;
                switch (anchor.Role?.ToLowerInvariant())
                {
                    case null:
                        break;
                    case "lefthand":
                        role = DeviceRole.LeftHand;
                        break;
                    case "righthand":
                        role = DeviceRole.RightHand;
                        break;
                    default:
                        errors.Add($"{label}: anchor.role must be \"leftHand\" or \"rightHand\" (got \"{anchor.Role}\").");
                        break;
                }

                var serial = anchor.Serial?.Trim();
                if (serial is not null && serial.Length == 0)
                {
                    errors.Add($"{label}: anchor.serial must not be empty.");
                }

                if ((anchor.Role is null) == (anchor.Serial is null))
                {
                    errors.Add($"{label}: a device anchor needs exactly one of anchor.role or anchor.serial.");
                }

                var onLost = LostBehavior.Hold;
                switch (anchor.OnLost?.ToLowerInvariant())
                {
                    case null:
                    case "hold":
                        break;
                    case "hide":
                        onLost = LostBehavior.Hide;
                        break;
                    default:
                        errors.Add($"{label}: anchor.onLost must be \"hold\" or \"hide\" (got \"{anchor.OnLost}\").");
                        break;
                }

                return errors.Count == before ? new DeviceAnchorSpec(role, serial, onLost) : null;
            }

            default:
                errors.Add($"{label}: anchor.type must be \"hmd\", \"world\" or \"device\" (got \"{anchor.Type}\").");
                return null;
        }
    }

    private static MaskShape? ValidateShape(ShapeConfig? shape, double width, double height, string label, List<string> errors)
    {
        var type = shape?.Type ?? "ellipse";
        var before = errors.Count;
        var lower = type.ToLowerInvariant();
        if (shape?.CornerRadius is not null && lower != "roundedrect")
        {
            errors.Add($"{label}: shape.cornerRadius is only valid for roundedRect.");
        }

        if (shape?.Vertices is not null && lower != "polygon")
        {
            errors.Add($"{label}: shape.vertices is only valid for polygon.");
        }

        switch (lower)
        {
            case "ellipse":
                return errors.Count == before ? new EllipseShape(width / 2, height / 2) : null;

            case "roundedrect":
            {
                var radius = shape?.CornerRadius ?? 0;
                if (!double.IsFinite(radius) || radius < 0)
                {
                    errors.Add($"{label}: shape.cornerRadius must be >= 0 (got {radius}).");
                }

                return errors.Count == before ? new RoundedRectShape(width / 2, height / 2, radius) : null;
            }

            case "polygon":
                return ValidatePolygon(shape?.Vertices, width, height, label, errors);

            default:
                errors.Add($"{label}: shape.type must be \"ellipse\", \"roundedRect\" or \"polygon\" (got \"{type}\").");
                return null;
        }
    }

    private static PolygonShape? ValidatePolygon(double[][]? vertices, double width, double height, string label, List<string> errors)
    {
        if (vertices is null || vertices.Length < 3)
        {
            errors.Add($"{label}: shape.vertices must have at least 3 points ([[x, y], ...]).");
            return null;
        }

        if (vertices.Length > MaxPolygonVertices)
        {
            errors.Add($"{label}: shape.vertices must have at most {MaxPolygonVertices} points (got {vertices.Length}).");
            return null;
        }

        var points = new List<(double X, double Y)>(vertices.Length);
        for (var i = 0; i < vertices.Length; i++)
        {
            var v = vertices[i];
            if (v is null || v.Length != 2 || !double.IsFinite(v[0]) || !double.IsFinite(v[1])
                || Math.Abs(v[0]) > 0.5 || Math.Abs(v[1]) > 0.5)
            {
                errors.Add($"{label}: shape.vertices[{i}] must be [x, y] with -0.5 <= x, y <= 0.5 (fractions of width/height).");
                return null;
            }

            points.Add((v[0] * width, v[1] * height));
        }

        // すべての点が一直線上にある多角形は何も描けない。
        // 符号付き面積は自己交差（8 の字など）で打ち消し合って 0 になるので使わず、
        // 最初の点から最も遠い点を通る直線から、他の点がどれだけ離れているかで判断する。
        var (x0, y0) = points[0];
        var far = points.MaxBy(p => (p.X - x0) * (p.X - x0) + (p.Y - y0) * (p.Y - y0));
        var spread = points.Max(p => Math.Abs((far.X - x0) * (p.Y - y0) - (far.Y - y0) * (p.X - x0)));
        if (spread < 1e-6 * width * height)
        {
            errors.Add($"{label}: shape.vertices must enclose an area (the points are on one line).");
            return null;
        }

        return new PolygonShape(points);
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

    private static bool ValidateRange(double value, string name, string label, double minExclusive, double maxInclusive, List<string> errors)
    {
        if (!double.IsFinite(value) || value <= minExclusive || value > maxInclusive)
        {
            errors.Add($"{label}: {name} must be greater than {minExclusive} and at most {maxInclusive} (got {value}).");
            return false;
        }

        return true;
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

        var names = new[]
        {
            ("enabled", parameters.Enabled),
            ("preset", parameters.Preset),
            ("debug", parameters.Debug),
            ("auto", parameters.Auto),
            ("calibrate", parameters.Calibrate),
            ("autoMethod", parameters.AutoMethod),
            ("violin", parameters.Violin),
            ("violinState", parameters.ViolinState),
        };
        foreach (var (key, value) in names)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace))
            {
                errors.Add($"osc.parameters.{key} must be a non-empty name without spaces (got \"{value}\").");
            }
        }

        if (names.Select(n => n.Item2).Distinct(StringComparer.Ordinal).Count() != names.Length)
        {
            errors.Add("osc.parameters must use a different name for each parameter.");
        }

        return new OscSettings(
            osc.UseOscQuery,
            osc.FallbackListenPort,
            osc.VrchatSendHost ?? string.Empty,
            osc.VrchatSendPort,
            parameters.Enabled ?? string.Empty,
            parameters.Preset ?? string.Empty,
            parameters.Debug ?? string.Empty,
            parameters.Auto ?? string.Empty,
            parameters.Calibrate ?? string.Empty,
            parameters.AutoMethod ?? string.Empty,
            parameters.Violin ?? string.Empty,
            parameters.ViolinState ?? string.Empty);
    }

    private static AutoSettings? ValidateAuto(AutoConfig? auto, List<string> errors)
    {
        if (auto is null)
        {
            errors.Add("auto must be an object (or omitted).");
            return null;
        }

        var before = errors.Count;
        // 判定方法は OSC でも切り替えるので、左手を使わない設定でも基準の位置は持っておく（null なら既定の位置）。
        var leftHand = ValidateVector(auto.LeftHandPosition ?? new AutoConfig().LeftHandPosition, "leftHandPosition", "auto", required: false, errors);
        Vec3? headUp = null;
        if (auto.HeadUp is not null)
        {
            var up = ValidateVector(auto.HeadUp, "headUp", "auto", required: true, errors);
            if (up.Length < 1e-6)
            {
                errors.Add("auto: headUp must not be [0, 0, 0].");
            }
            else
            {
                headUp = up.Normalized();
            }
        }

        var deviceSerial = auto.DeviceSerial?.Trim();
        if (deviceSerial is not null && deviceSerial.Length == 0)
        {
            errors.Add("auto.deviceSerial must not be empty (use null to use the left hand).");
        }

        if (!auto.UseLeftHand && !auto.UseHeadTilt)
        {
            errors.Add("auto: enable at least one of useLeftHand or useHeadTilt.");
        }

        ValidateBetween(auto.RadiusMeters, "auto.radiusMeters", 0.02, 1.0, errors);
        ValidateBetween(auto.HeadTiltToleranceDeg, "auto.headTiltToleranceDeg", 1, 90, errors);
        ValidateBetween(auto.EnterDelaySeconds, "auto.enterDelaySeconds", 0, 10, errors);
        ValidateBetween(auto.ExitDelaySeconds, "auto.exitDelaySeconds", 0, 30, errors);

        return errors.Count == before
            ? new AutoSettings(
                auto.Enabled,
                auto.UseLeftHand,
                auto.UseHeadTilt,
                leftHand,
                headUp,
                auto.RadiusMeters,
                auto.HeadTiltToleranceDeg,
                auto.EnterDelaySeconds,
                auto.ExitDelaySeconds,
                deviceSerial)
            : null;
    }

    private static CalibrationSettings? ValidateCalibration(CalibrationConfig? calibration, List<string> errors)
    {
        if (calibration is null)
        {
            errors.Add("calibration must be an object (or omitted).");
            return null;
        }

        var before = errors.Count;
        // 領域があるか・キャリブレーションできるアンカー（HMD かデバイス）かは、キャリブレーションを実行するときに確かめる
        // （キャリブレーションを使わない人の設定をエラーにしないため）。
        if (string.IsNullOrWhiteSpace(calibration.RegionId))
        {
            errors.Add("calibration.regionId must not be empty.");
        }

        if (calibration.CountdownSeconds is < 1 or > 30)
        {
            errors.Add($"calibration.countdownSeconds must be between 1 and 30 (got {calibration.CountdownSeconds}).");
        }

        ValidateBetween(calibration.PadMeters, "calibration.padMeters", 0, 0.5, errors);
        ValidateBetween(calibration.PreviewSeconds, "calibration.previewSeconds", 0, 60, errors);

        return errors.Count == before
            ? new CalibrationSettings(
                calibration.RegionId ?? string.Empty,
                calibration.CountdownSeconds,
                calibration.PadMeters,
                calibration.PreviewSeconds,
                calibration.Beep)
            : null;
    }

    private static FeedbackSettings? ValidateFeedback(FeedbackConfig? feedback, List<string> errors)
    {
        if (feedback is null)
        {
            errors.Add("feedback must be an object (or omitted).");
            return null;
        }

        var before = errors.Count;
        var position = ValidateVector(feedback.Position, "position", "feedback", required: true, errors);
        ValidateBetween(feedback.TextHeightMeters, "feedback.textHeightMeters", 0.005, 0.2, errors);
        return errors.Count == before ? new FeedbackSettings(feedback.Toasts, position, feedback.TextHeightMeters) : null;
    }

    private static HandDevices? ValidateTracking(TrackingConfig? tracking, List<string> errors)
    {
        if (tracking is null)
        {
            errors.Add("tracking must be an object (or omitted).");
            return null;
        }

        var before = errors.Count;
        string? Serial(string? value, string name)
        {
            var serial = value?.Trim();
            if (serial is not null && serial.Length == 0)
            {
                errors.Add($"tracking.{name} must not be empty (use null to use the hand).");
            }

            return serial;
        }

        var left = Serial(tracking.LeftHandSerial, "leftHandSerial");
        var right = Serial(tracking.RightHandSerial, "rightHandSerial");
        // シリアル番号は大文字・小文字を区別せずに探すので、比べるときも区別しない。
        if (left is not null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("tracking.leftHandSerial and tracking.rightHandSerial must be different devices.");
        }

        return errors.Count == before ? new HandDevices(left, right) : null;
    }

    private static void ValidateBetween(double value, string name, double min, double max, List<string> errors)
    {
        if (!double.IsFinite(value) || value < min || value > max)
        {
            errors.Add($"{name} must be between {min} and {max} (got {value}).");
        }
    }

    private static void ValidatePort(int port, string name, List<string> errors)
    {
        if (port is < 1 or > 65535)
        {
            errors.Add($"{name} must be between 1 and 65535 (got {port}).");
        }
    }

    // $ は末尾の改行の前にも一致するので、\z で文字列の終わりを指定する。
    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}\z")]
    private static partial Regex RegionIdPattern();
}
