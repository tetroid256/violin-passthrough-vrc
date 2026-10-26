using System.Text.Json.Nodes;
using ViolinPassthrough.Anchor;
using ViolinPassthrough.Calibration;
using ViolinPassthrough.Config;

namespace ViolinPassthrough.Tests.Config;

public sealed class Phase6ConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vp-cal-" + Guid.NewGuid().ToString("N"));

    private static readonly CalibrationResult Result = new(
        "fingerboard",
        new Vec3(-0.17512, -0.17, -0.32499),
        new Vec3(12.345, 25.04, -18.96),
        0.56789,
        new Vec3(-0.3504, -0.1, -0.35),
        new Vec3(0.34202, 0.93969, -0.0000001));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Defaults_MatchTheDesign()
    {
        var config = TestConfigs.Default;

        Assert.False(config.Auto.Enabled);
        Assert.True(config.Auto.UseLeftHand);
        Assert.False(config.Auto.UseHeadTilt);
        Assert.Equal(new Vec3(-0.35, -0.10, -0.35), config.Auto.LeftHandPosition);
        Assert.Null(config.Auto.HeadUp);
        Assert.Equal(0.15, config.Auto.RadiusMeters);
        Assert.Equal(new CalibrationSettings("fingerboard", 5, 0.05, 5, true), config.Calibration);
        Assert.Equal(new FeedbackSettings(true, new Vec3(0, 0.08, -0.5), 0.015), config.Feedback);
        Assert.Equal("HandPassthroughAuto", config.Osc.AutoParameter);
        Assert.Equal("HandPassthroughCalibrate", config.Osc.CalibrateParameter);
    }

    [Fact]
    public void Phase5Config_WithoutNewSections_StillLoads()
    {
        var json = TestConfigs.DefaultJson;
        var root = JsonNode.Parse(json)!.AsObject();
        root.Remove("auto");
        root.Remove("calibration");
        root.Remove("feedback");
        root["osc"]!["parameters"]!.AsObject().Remove("auto");
        root["osc"]!["parameters"]!.AsObject().Remove("calibrate");

        var config = TestConfigs.Parse(root.ToJsonString());

        Assert.Equal(TestConfigs.Default.Auto, config.Auto);
        Assert.Equal(TestConfigs.Default.Calibration, config.Calibration);
    }

    [Theory]
    [InlineData("\"useLeftHand\": true,\n    \"useHeadTilt\": false", "\"useLeftHand\": false,\n    \"useHeadTilt\": false", "at least one")]
    [InlineData("\"radiusMeters\": 0.15", "\"radiusMeters\": 0", "auto.radiusMeters")]
    [InlineData("\"headTiltToleranceDeg\": 15", "\"headTiltToleranceDeg\": 120", "auto.headTiltToleranceDeg")]
    [InlineData("\"exitDelaySeconds\": 2.0", "\"exitDelaySeconds\": -1", "auto.exitDelaySeconds")]
    [InlineData("\"countdownSeconds\": 5", "\"countdownSeconds\": 0", "calibration.countdownSeconds")]
    [InlineData("\"padMeters\": 0.05", "\"padMeters\": 0.9", "calibration.padMeters")]
    [InlineData("\"regionId\": \"fingerboard\"", "\"regionId\": \"\"", "calibration.regionId")]
    [InlineData("\"textHeightMeters\": 0.015", "\"textHeightMeters\": 1", "feedback.textHeightMeters")]
    [InlineData("\"position\": [0, 0.08, -0.5]", "\"position\": [0, 0.08]", "feedback: position")]
    [InlineData("\"calibrate\": \"HandPassthroughCalibrate\"", "\"calibrate\": \"HandPassthroughAuto\"", "different name")]
    public void InvalidValues_AreRejected(string oldText, string newText, string expected)
    {
        var result = ConfigValidator.Parse(TestConfigs.DefaultWith(oldText, newText));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void HeadUp_IsNormalized()
    {
        var json = TestConfigs.DefaultWith("\"useHeadTilt\": false", "\"useHeadTilt\": true, \"headUp\": [0, 2, 0]");

        Assert.Equal(new Vec3(0, 1, 0), TestConfigs.Parse(json).Auto.HeadUp);
    }

    [Fact]
    public void CalibrationRegion_IsCheckedAtRuntime_NotOnLoad()
    {
        // キャリブレーションを使わない人が領域名を変えても、設定はエラーにならない。
        var json = TestConfigs.DefaultWith("\"regionId\": \"fingerboard\"", "\"regionId\": \"nope\"");

        Assert.True(ConfigValidator.Parse(json).Success);
    }

    [Fact]
    public void UpdateCalibrationJson_WritesRoundedValuesAndKeepsTheRest()
    {
        var json = ConfigStore.UpdateCalibrationJson(TestConfigs.DefaultJson, Result, out var error);

        Assert.NotNull(json);
        Assert.Contains("\"position\": [-0.175, -0.17, -0.325]", json);
        Assert.Contains("\"rotationEulerDeg\": [12.3, 25, -19]", json);
        Assert.Contains("\"widthMeters\": 0.568", json);
        Assert.Contains("\"leftHandPosition\": [-0.35, -0.1, -0.35]", json);
        Assert.Contains("\"headUp\": [0.342, 0.94, 0]", json);
        Assert.DoesNotContain("-0]", json);

        var config = TestConfigs.Parse(json!);
        var fingerboard = config.Regions.Single(r => r.Id == "fingerboard");
        Assert.Equal(new Vec3(-0.175, -0.17, -0.325), fingerboard.Position);
        Assert.Equal(0.568, fingerboard.WidthMeters);
        Assert.Equal(0.25, fingerboard.HeightMeters);
        Assert.Equal(TestConfigs.Default.Regions.Single(r => r.Id == "body"), config.Regions.Single(r => r.Id == "body"));
        Assert.Equal(TestConfigs.Default.Osc, config.Osc);
        Assert.Equal(TestConfigs.Default.Presets.Select(p => p.Name), config.Presets.Select(p => p.Name));
    }

    [Fact]
    public void UpdateCalibrationJson_WithoutAnAutoReference_KeepsLeftHandPosition()
    {
        // auto.deviceSerial のデバイスが見えず、自動モードが頭の傾きだけのとき（DESIGN.md §16.2）。
        var json = ConfigStore.UpdateCalibrationJson(TestConfigs.DefaultJson, Result with { LeftHandPosition = null }, out _);

        var config = TestConfigs.Parse(json!);
        Assert.Equal(TestConfigs.Default.Auto.LeftHandPosition, config.Auto.LeftHandPosition);
        Assert.NotNull(config.Auto.HeadUp);
    }

    [Fact]
    public void UpdateCalibrationJson_AddsAutoSectionAndAcceptsCommentsAndOtherCasing()
    {
        const string text = """
            {
              // 手で書いた設定
              "Presets": { "only": ["Main"] },
              "activePreset": "only",
              "Regions": [
                { "Id": "Main", "Position": [0, 0, -0.5], "WidthMeters": 0.2, "HeightMeters": 0.1, },
              ],
            }
            """;
        var result = Result with { RegionId = "Main" };

        var json = ConfigStore.UpdateCalibrationJson(text, result, out _);

        var config = TestConfigs.Parse(json!);
        Assert.Equal(new Vec3(-0.175, -0.17, -0.325), config.Regions[0].Position);
        Assert.Equal(new Vec3(-0.35, -0.1, -0.35), config.Auto.LeftHandPosition);
        // 元の綴り（Position）のまま書き換え、position を別に足さない。
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json!, "\"[Pp]osition\""));
        Assert.Contains("\"Position\": [-0.175, -0.17, -0.325]", json);
    }

    [Fact]
    public void UpdateCalibrationJson_UnknownRegion_Fails()
    {
        Assert.Null(ConfigStore.UpdateCalibrationJson(TestConfigs.DefaultJson, Result with { RegionId = "nope" }, out var error));
        Assert.Equal("REGION NOT FOUND", error);
    }

    [Fact]
    public void TryApplyCalibration_BacksUpAndReloads()
    {
        var path = Path.Combine(_dir, "config.json");
        var time = new ManualTimeProvider();
        using var store = new ConfigStore(path, time);
        store.Initialize();
        var original = File.ReadAllText(path);

        Assert.True(store.TryApplyCalibration(Result, out var error), error);

        Assert.Equal(original, File.ReadAllText(store.BackupPath));
        Assert.False(File.Exists(path + ".tmp"));
        time.Advance(ConfigStore.Debounce);
        Assert.True(store.TryReload(out var reloaded));
        Assert.Equal(new Vec3(-0.175, -0.17, -0.325), reloaded!.Regions[0].Position);
        Assert.Equal(new Vec3(0.342, 0.94, 0).Normalized(), reloaded.Auto.HeadUp!.Value);
    }

    [Fact]
    public void TryApplyCalibration_KeepsTheFirstOriginal_AndTheLatestBackup()
    {
        var path = Path.Combine(_dir, "config.json");
        Directory.CreateDirectory(_dir);
        var handWritten = "// 手で書いたコメント\n" + TestConfigs.DefaultJson;
        File.WriteAllText(path, handWritten);
        using var store = new ConfigStore(path, new ManualTimeProvider());
        store.Initialize();

        Assert.True(store.TryApplyCalibration(Result, out _));
        var afterFirst = File.ReadAllText(path);
        Assert.True(store.TryApplyCalibration(Result with { WidthMeters = 0.6 }, out _));

        Assert.Equal(handWritten, File.ReadAllText(store.OriginalPath));
        Assert.Equal(afterFirst, File.ReadAllText(store.BackupPath));
        Assert.Contains("\"widthMeters\": 0.6", File.ReadAllText(path));
    }

    [Fact]
    public void TryApplyCalibration_ConfigWithErrors_IsNotTouched()
    {
        var path = Path.Combine(_dir, "config.json");
        Directory.CreateDirectory(_dir);
        var broken = TestConfigs.DefaultWith("\"logLevel\": \"Info\"", "\"logLevel\": \"Verbose\"");
        File.WriteAllText(path, broken);
        using var store = new ConfigStore(path, new ManualTimeProvider());
        store.Initialize();

        Assert.False(store.TryApplyCalibration(Result, out var error));

        Assert.Equal("FIX CONFIG FIRST", error);
        Assert.Equal(broken, File.ReadAllText(path));
        Assert.False(File.Exists(store.BackupPath));
    }

    [Fact]
    public void TryApplyCalibration_DuplicateKeysWithDifferentCase_FailsWithoutThrowing()
    {
        var path = Path.Combine(_dir, "config.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, TestConfigs.DefaultJson);
        using var store = new ConfigStore(path, new ManualTimeProvider());
        store.Initialize();
        // 検証を通らない書き方だが、手で書き換えた直後（まだ再読み込みしていない）にキャリブレーションが走った場合。
        File.WriteAllText(path, TestConfigs.DefaultWith("\"widthMeters\": 0.45", "\"widthMeters\": 0.45, \"WidthMeters\": 0.5"));

        Assert.False(store.TryApplyCalibration(Result, out var error));
        Assert.NotEmpty(error);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void UpdateCalibrationJson_LeavesBracketsInsideStringsAlone()
    {
        var json = TestConfigs.DefaultWith("\"whole\": [\"fingerboard\", \"body\"]", "\"p[1,2]\": [\"fingerboard\", \"body\"]")
            .Replace("\"debug\": \"HandPassthroughDebug\"", "\"debug\": \"Hand[1,2]\"");

        var updated = ConfigStore.UpdateCalibrationJson(json, Result, out _);

        Assert.Contains("\"p[1,2]\": [", updated);
        Assert.Contains("\"debug\": \"Hand[1,2]\"", updated);
        Assert.True(ConfigValidator.Parse(updated!).Success);
    }

    [Fact]
    public void UpdateCalibrationJson_KeepsPolygonVerticesReadable()
    {
        var json = TestConfigs.DefaultWith(
            "{ \"type\": \"roundedRect\", \"cornerRadius\": 0.05 }",
            "{ \"type\": \"polygon\", \"vertices\": [[-0.5, -0.5], [0.5, -0.2], [0.3, 0.5]] }");

        var updated = ConfigStore.UpdateCalibrationJson(json, Result, out _)!;

        Assert.Contains("[-0.5, -0.5],", updated);
        Assert.Equal(TestConfigs.Parse(json).Regions[1], TestConfigs.Parse(updated).Regions[1]);
    }

    [Fact]
    public void TryApplyCalibration_MissingFile_Fails()
    {
        using var store = new ConfigStore(Path.Combine(_dir, "missing", "config.json"), new ManualTimeProvider());

        Assert.False(store.TryApplyCalibration(Result, out var error));
        Assert.Equal("CANNOT WRITE", error);
    }

    [Fact]
    public void InvalidCount_CountsInvalidReloads()
    {
        var path = Path.Combine(_dir, "config.json");
        var time = new ManualTimeProvider();
        using var store = new ConfigStore(path, time);
        store.Initialize();

        File.WriteAllText(path, "{ broken");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
        time.Advance(ConfigStore.PollInterval);
        store.TryReload(out _);
        time.Advance(ConfigStore.Debounce);
        store.TryReload(out _);

        Assert.Equal(1, store.InvalidCount);
    }
}
