using System.Text.Json;
using System.Text.Json.Serialization;

namespace ViolinPassthrough.Config;

/// <summary>
/// 設定ファイル（config.json）のデータモデル。JSON と1対1に対応する。
/// 値の検証と実行時の型への変換は <see cref="ConfigValidator"/> が行う。
/// </summary>
public sealed class AppConfig
{
    public ColorConfig KeyColor { get; set; } = new();

    /// <summary>"hard" または "aa"。</summary>
    public string EdgeMode { get; set; } = "hard";

    public int TextureLongSide { get; set; } = 512;

    public long SortOrder { get; set; } = 100;

    /// <summary>起動時に有効にするか。起動時だけ読む。</summary>
    public bool StartEnabled { get; set; }

    /// <summary>デバッグ表示。値が変わったときに状態へ反映する。</summary>
    public bool DebugVisible { get; set; }

    /// <summary>起動時のプリセット。値が変わったときに状態へ反映する。</summary>
    public string ActivePreset { get; set; } = "fingerboard";

    /// <summary>プリセット名 → 有効にする領域 ID。JSON のオブジェクトの順番を保つ（OSC の番号に対応）。</summary>
    [JsonConverter(typeof(OrderedPresetsConverter))]
    public List<PresetConfig> Presets { get; set; } = new();

    public List<RegionConfig> Regions { get; set; } = new();

    public OscConfig Osc { get; set; } = new();

    /// <summary>構えたときだけ表示する自動モード（DESIGN.md §15.3）。</summary>
    public AutoConfig Auto { get; set; } = new();

    /// <summary>手の位置からのキャリブレーション（DESIGN.md §15.2）。</summary>
    public CalibrationConfig Calibration { get; set; } = new();

    /// <summary>VR 内の通知表示（DESIGN.md §15.4）。</summary>
    public FeedbackConfig Feedback { get; set; } = new();

    /// <summary>"Debug" / "Info" / "Warning" / "Error"。</summary>
    public string LogLevel { get; set; } = "Info";
}

public sealed class ColorConfig
{
    public int R { get; set; } = 255;

    public int G { get; set; }

    public int B { get; set; } = 255;
}

public sealed record PresetConfig(string Name, List<string> Regions);

public sealed class RegionConfig
{
    public string? Id { get; set; }

    public AnchorConfig? Anchor { get; set; }

    public ShapeConfig? Shape { get; set; }

    public double[]? Position { get; set; }

    public double[]? RotationEulerDeg { get; set; }

    public double WidthMeters { get; set; }

    public double HeightMeters { get; set; }

    public double MarginMeters { get; set; }
}

public sealed class AnchorConfig
{
    /// <summary>"hmd" / "world" / "device"。</summary>
    public string Type { get; set; } = "hmd";

    /// <summary>world: "standing"（既定）/ "seated"。</summary>
    public string? Universe { get; set; }

    /// <summary>device: "leftHand" / "rightHand"。serial とどちらか一方。</summary>
    public string? Role { get; set; }

    /// <summary>device: トラッカーなどのシリアル番号。role とどちらか一方。</summary>
    public string? Serial { get; set; }

    /// <summary>device: 見失ったとき "hold"（既定。最後の場所に置いておく）/ "hide"（隠す）。</summary>
    public string? OnLost { get; set; }
}

public sealed class ShapeConfig
{
    /// <summary>"ellipse" / "roundedRect" / "polygon"。</summary>
    public string Type { get; set; } = "ellipse";

    /// <summary>roundedRect の角の半径（メートル）。</summary>
    public double? CornerRadius { get; set; }

    /// <summary>polygon の頂点 [[x, y], ...]。幅・高さに対する割合で、中心が 0、範囲は -0.5〜0.5（+x 右、+y 上）。</summary>
    public double[][]? Vertices { get; set; }
}

public sealed class OscConfig
{
    public bool UseOscQuery { get; set; } = true;

    public int FallbackListenPort { get; set; } = 9011;

    public string VrchatSendHost { get; set; } = "127.0.0.1";

    public int VrchatSendPort { get; set; } = 9000;

    public OscParametersConfig Parameters { get; set; } = new();
}

public sealed class OscParametersConfig
{
    public string Enabled { get; set; } = "HandPassthrough";

    public string Preset { get; set; } = "HandPassthroughPreset";

    public string Debug { get; set; } = "HandPassthroughDebug";

    public string Auto { get; set; } = "HandPassthroughAuto";

    public string Calibrate { get; set; } = "HandPassthroughCalibrate";
}

public sealed class AutoConfig
{
    /// <summary>起動時に自動モードにするか。値が変わったときに状態へ反映する。</summary>
    public bool Enabled { get; set; }

    public bool UseLeftHand { get; set; } = true;

    public bool UseHeadTilt { get; set; }

    /// <summary>HMD から見た、構えたときの左手の位置（キャリブレーションで書き込む）。</summary>
    public double[]? LeftHandPosition { get; set; } = { -0.35, -0.10, -0.35 };

    /// <summary>HMD から見た、構えたときの「上」の向き（キャリブレーションで書き込む）。</summary>
    public double[]? HeadUp { get; set; }

    public double RadiusMeters { get; set; } = 0.15;

    public double HeadTiltToleranceDeg { get; set; } = 15;

    public double EnterDelaySeconds { get; set; } = 0.3;

    public double ExitDelaySeconds { get; set; } = 2.0;
}

public sealed class CalibrationConfig
{
    /// <summary>置き直す領域（HMD 基準のもの）。</summary>
    public string RegionId { get; set; } = "fingerboard";

    public int CountdownSeconds { get; set; } = 5;

    /// <summary>両手の外側に足す余裕（片側、メートル）。</summary>
    public double PadMeters { get; set; } = 0.05;

    /// <summary>キャリブレーションの後にデバッグ表示で結果を見せる時間。</summary>
    public double PreviewSeconds { get; set; } = 5;

    public bool Beep { get; set; } = true;
}

public sealed class FeedbackConfig
{
    /// <summary>カウントダウンと結果以外の通知（自動モードの切り替えなど）も出すか。</summary>
    public bool Toasts { get; set; } = true;

    /// <summary>通知表示の板の中心（HMD 基準、メートル）。</summary>
    public double[]? Position { get; set; } = { 0, 0.08, -0.5 };

    /// <summary>文字の高さ（メートル）。</summary>
    public double TextHeightMeters { get; set; } = 0.015;
}

/// <summary>
/// presets を <c>{ "名前": ["領域ID", ...] }</c> の形で読み書きする。Dictionary と違い、順番を保証する。
/// </summary>
public sealed class OrderedPresetsConverter : JsonConverter<List<PresetConfig>>
{
    public override List<PresetConfig> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("\"presets\" must be an object of { \"name\": [\"regionId\", ...] }.");
        }

        var result = new List<PresetConfig>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return result;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Unexpected token in \"presets\".");
            }

            var name = reader.GetString() ?? string.Empty;
            reader.Read();
            var regions = JsonSerializer.Deserialize<List<string>>(ref reader, options)
                ?? throw new JsonException($"Preset \"{name}\" must be an array of region ids.");
            result.Add(new PresetConfig(name, regions));
        }

        throw new JsonException("Unterminated \"presets\" object.");
    }

    public override void Write(Utf8JsonWriter writer, List<PresetConfig> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var preset in value)
        {
            writer.WritePropertyName(preset.Name);
            JsonSerializer.Serialize(writer, preset.Regions, options);
        }

        writer.WriteEndObject();
    }
}
