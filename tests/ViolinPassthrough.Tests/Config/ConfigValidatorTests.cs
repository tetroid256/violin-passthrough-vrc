using ViolinPassthrough.Anchor;
using ViolinPassthrough.Config;
using ViolinPassthrough.Logging;
using ViolinPassthrough.Mask;

namespace ViolinPassthrough.Tests.Config;

public class ConfigValidatorTests
{
    [Fact]
    public void DefaultConfig_IsValidAndMatchesDesign()
    {
        var config = TestConfigs.Default;

        Assert.Equal(new Rgb(255, 0, 255), config.Render.KeyColor);
        Assert.Equal(EdgeMode.Hard, config.Render.EdgeMode);
        Assert.Equal(512, config.Render.TextureLongSide);
        Assert.Equal(100u, config.Render.SortOrder);
        Assert.False(config.Render.Debug);
        Assert.False(config.StartEnabled);
        Assert.False(config.DebugVisible);
        Assert.Equal("fingerboard", config.ActivePreset);
        Assert.Equal(LogLevel.Info, config.LogLevel);

        var fingerboard = Assert.Single(config.Regions, r => r.Id == "fingerboard");
        Assert.IsType<HmdAnchorSpec>(fingerboard.Anchor);
        Assert.Equal(new EllipseShape(0.225, 0.125), fingerboard.Shape);
        Assert.Equal(new Vec3(-0.15, -0.15, -0.40), fingerboard.Position);
        Assert.Equal(new Vec3(15, 20, -15), fingerboard.RotationEulerDeg);
        Assert.Equal(0.45, fingerboard.WidthMeters);
        Assert.Equal(0.25, fingerboard.HeightMeters);
        Assert.Equal(0.03, fingerboard.MarginMeters);

        var body = Assert.Single(config.Regions, r => r.Id == "body");
        Assert.Equal(new RoundedRectShape(0.15, 0.10, 0.05), body.Shape);

        Assert.Equal(
            new[] { ("fingerboard", "fingerboard"), ("whole", "fingerboard,body") },
            config.Presets.Select(p => (p.Name, string.Join(",", p.RegionIds))));

        Assert.Equal(
            new OscSettings(true, 9011, "127.0.0.1", 9000, "HandPassthrough", "HandPassthroughPreset", "HandPassthroughDebug"),
            config.Osc);
    }

    [Fact]
    public void Presets_KeepDocumentOrder()
    {
        var json = TestConfigs.DefaultWith(
            "\"fingerboard\": [\"fingerboard\"],\n    \"whole\": [\"fingerboard\", \"body\"]",
            "\"z\": [\"body\"], \"whole\": [\"fingerboard\", \"body\"], \"a\": [], \"fingerboard\": [\"fingerboard\"]");

        var config = TestConfigs.Parse(json);

        Assert.Equal(new[] { "z", "whole", "a", "fingerboard" }, config.Presets.Select(p => p.Name));
        Assert.Equal(3, config.IndexOfPreset("fingerboard"));
        Assert.Empty(config.FindPreset("a")!.RegionIds);
    }

    [Fact]
    public void CommentsAndTrailingCommas_AreAllowed()
    {
        var json = TestConfigs.DefaultWith("\"edgeMode\": \"hard\",", "// コメント\n  \"edgeMode\": \"aa\", /* block */");
        json = json.Replace("\"logLevel\": \"Info\"", "\"logLevel\": \"Debug\",");

        var config = TestConfigs.Parse(json);

        Assert.Equal(EdgeMode.Aa, config.Render.EdgeMode);
        Assert.Equal(LogLevel.Debug, config.LogLevel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{ \"keyColor\": ")]
    public void BrokenJson_IsRejected(string json)
    {
        var result = ConfigValidator.Parse(json);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void UnknownProperty_IsRejectedWithItsName()
    {
        var json = TestConfigs.DefaultWith("\"widthMeters\": 0.45", "\"widhtMeters\": 0.45");

        var result = ConfigValidator.Parse(json);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("widhtMeters"));
    }

    [Theory]
    [InlineData("\"r\": 255, \"g\": 0", "\"r\": 256, \"g\": 0", "keyColor.r")]
    [InlineData("\"edgeMode\": \"hard\"", "\"edgeMode\": \"soft\"", "edgeMode")]
    [InlineData("\"textureLongSide\": 512", "\"textureLongSide\": 8", "textureLongSide")]
    [InlineData("\"sortOrder\": 100", "\"sortOrder\": -1", "sortOrder")]
    [InlineData("\"activePreset\": \"fingerboard\"", "\"activePreset\": \"nope\"", "activePreset")]
    [InlineData("\"widthMeters\": 0.45", "\"widthMeters\": 0", "widthMeters")]
    [InlineData("\"heightMeters\": 0.25", "\"heightMeters\": -1", "heightMeters")]
    [InlineData("\"marginMeters\": 0.03\n    },\n    {", "\"marginMeters\": -0.01\n    },\n    {", "marginMeters")]
    [InlineData("\"position\": [-0.15, -0.15, -0.40]", "\"position\": [-0.15, -0.15]", "position")]
    [InlineData("\"position\": [-0.15, -0.15, -0.40]", "\"position\": null", "position")]
    [InlineData("\"rotationEulerDeg\": [15, 20, -15]", "\"rotationEulerDeg\": [15, 20, -15, 0]", "rotationEulerDeg")]
    [InlineData("\"id\": \"body\"", "\"id\": \"fingerboard\"", "duplicate id")]
    [InlineData("\"id\": \"body\"", "\"id\": \"bo dy\"", "id must be")]
    [InlineData("{ \"type\": \"ellipse\" }", "{ \"type\": \"star\" }", "shape.type")]
    [InlineData("{ \"type\": \"ellipse\" }", "{ \"type\": \"ellipse\", \"cornerRadius\": 0.1 }", "cornerRadius")]
    [InlineData("\"cornerRadius\": 0.05", "\"cornerRadius\": -0.05", "cornerRadius")]
    [InlineData("\"anchor\": { \"type\": \"hmd\" },\n      \"shape\": { \"type\": \"ellipse\" }", "\"anchor\": { \"type\": \"moon\" },\n      \"shape\": { \"type\": \"ellipse\" }", "anchor.type")]
    [InlineData("\"whole\": [\"fingerboard\", \"body\"]", "\"whole\": [\"fingerboard\", \"neck\"]", "unknown region id \"neck\"")]
    [InlineData("\"fallbackListenPort\": 9011", "\"fallbackListenPort\": 70000", "fallbackListenPort")]
    [InlineData("\"vrchatSendPort\": 9000", "\"vrchatSendPort\": 0", "vrchatSendPort")]
    [InlineData("\"vrchatSendHost\": \"127.0.0.1\"", "\"vrchatSendHost\": \"\"", "vrchatSendHost")]
    [InlineData("\"debug\": \"HandPassthroughDebug\"", "\"debug\": \"HandPassthrough\"", "different names")]
    [InlineData("\"debug\": \"HandPassthroughDebug\"", "\"debug\": \"Hand Debug\"", "osc.parameters.debug")]
    [InlineData("\"logLevel\": \"Info\"", "\"logLevel\": \"Verbose\"", "logLevel")]
    [InlineData("\"keyColor\": { \"r\": 255, \"g\": 0, \"b\": 255 }", "\"keyColor\": null", "keyColor")]
    [InlineData("\"osc\": {", "\"oscX\": null, \"osc\": {", "oscX")]
    public void InvalidValues_AreRejectedWithHelpfulErrors(string oldText, string newText, string expectedInError)
    {
        var json = TestConfigs.DefaultWith(oldText, newText);

        var result = ConfigValidator.Parse(json);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains(expectedInError, StringComparison.Ordinal));
    }

    [Fact]
    public void MissingOptionalFields_UseDefaults()
    {
        const string json = """
            {
              "presets": { "only": ["a"] },
              "activePreset": "only",
              "regions": [
                { "id": "a", "position": [0, 0, -0.5], "widthMeters": 0.2, "heightMeters": 0.1 }
              ]
            }
            """;

        var config = TestConfigs.Parse(json);
        var region = Assert.Single(config.Regions);

        Assert.IsType<HmdAnchorSpec>(region.Anchor);
        Assert.Equal(new EllipseShape(0.1, 0.05), region.Shape);
        Assert.Equal(default, region.RotationEulerDeg);
        Assert.Equal(0, region.MarginMeters);
        Assert.Equal(new Rgb(255, 0, 255), config.Render.KeyColor);
        Assert.True(config.Osc.UseOscQuery);
    }

    [Fact]
    public void EmptyRegionsOrPresets_AreRejected()
    {
        Assert.False(ConfigValidator.Parse("""{ "regions": [], "presets": { "a": [] }, "activePreset": "a" }""").Success);
        Assert.False(ConfigValidator.Parse("""
            { "regions": [ { "id": "a", "position": [0,0,-1], "widthMeters": 1, "heightMeters": 1 } ], "presets": {} }
            """).Success);
    }
}
