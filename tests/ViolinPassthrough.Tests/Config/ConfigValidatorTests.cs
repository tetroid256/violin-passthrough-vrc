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
    [InlineData("\"widthMeters\": 0.30", "\"widthMeters\": -0.30", "widthMeters")]
    [InlineData("\"heightMeters\": 0.20", "\"heightMeters\": -0.20", "heightMeters")]
    [InlineData("\"widthMeters\": 0.30", "\"widthMeters\": 0", "widthMeters")]
    [InlineData("\"widthMeters\": 0.45", "\"widthMeters\": 0.60, \"widthMeters\": 0.45", "widthMeters")]
    [InlineData("\"edgeMode\": \"hard\"", "\"edgeMode\": \"hard\", \"EdgeMode\": \"aa\"", "dgeMode")]
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
    public void RegionIdWithTrailingNewline_IsRejected()
    {
        var json = TestConfigs.DefaultWith("\"id\": \"body\"", "\"id\": \"body\\n\"")
            .Replace("\"whole\": [\"fingerboard\", \"body\"]", "\"whole\": [\"fingerboard\", \"body\\n\"]");

        var result = ConfigValidator.Parse(json);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("id must be"));
    }

    [Fact]
    public void Parse_NeverThrows_ForMutatedDefaultConfig()
    {
        // 既定の設定の数値・文字をあちこち書き換えても、例外ではなく検証エラーになること。
        var source = TestConfigs.DefaultJson;
        var random = new Random(42);
        var replacements = new[] { "-1", "0", "1e309", "null", "\"x\"", "[]", "{}", "-0.5", "true", "[0,0]", "1e-320" };
        for (var i = 0; i < 3000; i++)
        {
            var json = source;
            var position = random.Next(json.Length);
            var digit = json.IndexOfAny("0123456789".ToCharArray(), position);
            if (digit >= 0)
            {
                var end = digit;
                while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.'))
                {
                    end++;
                }

                json = json[..digit] + replacements[random.Next(replacements.Length)] + json[end..];
            }

            if (random.Next(3) == 0)
            {
                var chars = json.ToCharArray();
                const string noise = "{}[]:,\"-0a ";
                chars[random.Next(chars.Length)] = noise[random.Next(noise.Length)];
                json = new string(chars);
            }

            var exception = Record.Exception(() => ConfigValidator.Parse(json));
            Assert.True(exception is null, $"Parse threw for input:\n{json}\n{exception}");
        }
    }

    private static ConfigParseResult ParseWithFingerboard(string anchor, string shape) =>
        ConfigValidator.Parse(TestConfigs.DefaultWith(
            "\"anchor\": { \"type\": \"hmd\" },\n      \"shape\": { \"type\": \"ellipse\" }",
            $"\"anchor\": {anchor},\n      \"shape\": {shape}"));

    [Fact]
    public void Polygon_VerticesAreScaledByWidthAndHeight()
    {
        var result = ParseWithFingerboard("{ \"type\": \"hmd\" }", "{ \"type\": \"polygon\", \"vertices\": [[-0.5, -0.5], [0.5, -0.2], [0.3, 0.5]] }");

        Assert.True(result.Success, string.Join("\n", result.Errors));
        var shape = Assert.IsType<PolygonShape>(result.Config!.Regions[0].Shape);
        Assert.Equal(new[] { (-0.225, -0.125), (0.225, -0.05), (0.135, 0.125) }, shape.Vertices.Select(v => (Math.Round(v.X, 9), Math.Round(v.Y, 9))));
    }

    [Theory]
    [InlineData("{ \"type\": \"polygon\" }", "at least 3")]
    [InlineData("{ \"type\": \"polygon\", \"vertices\": [[0, 0], [0.1, 0]] }", "at least 3")]
    [InlineData("{ \"type\": \"polygon\", \"vertices\": [[0, 0], [0.6, 0], [0, 0.1]] }", "vertices[1]")]
    [InlineData("{ \"type\": \"polygon\", \"vertices\": [[0, 0], [0.1], [0, 0.1]] }", "vertices[1]")]
    [InlineData("{ \"type\": \"polygon\", \"vertices\": [[0, 0], null, [0, 0.1]] }", "vertices[1]")]
    [InlineData("{ \"type\": \"polygon\", \"vertices\": [[0, 0], [0.1, 0.1], [0.2, 0.2]] }", "enclose an area")]
    [InlineData("{ \"type\": \"polygon\", \"vertices\": [[0.1, 0.1], [0.1, 0.1], [0.1, 0.1]] }", "enclose an area")]
    [InlineData("{ \"type\": \"polygon\", \"vertices\": [[0, 0], [0.2, 0.2], [-0.1, -0.1], [0.4, 0.4]] }", "enclose an area")]
    [InlineData("{ \"type\": \"polygon\", \"cornerRadius\": 0.1, \"vertices\": [[0, 0], [0.1, 0], [0, 0.1]] }", "cornerRadius")]
    [InlineData("{ \"type\": \"ellipse\", \"vertices\": [[0, 0], [0.1, 0], [0, 0.1]] }", "vertices is only valid")]
    public void Polygon_InvalidDefinitions(string shape, string expected)
    {
        var result = ParseWithFingerboard("{ \"type\": \"hmd\" }", shape);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Polygon_SelfIntersectingBowtie_IsAccepted()
    {
        // 符号付き面積は 0 になるが、偶奇規則で2つの三角形として描ける。
        var result = ParseWithFingerboard("{ \"type\": \"hmd\" }", "{ \"type\": \"polygon\", \"vertices\": [[-0.5, -0.5], [0.5, 0.5], [0.5, -0.5], [-0.5, 0.5]] }");

        Assert.True(result.Success, string.Join("\n", result.Errors));
        var shape = result.Config!.Regions[0].Shape;
        Assert.True(shape.SignedDistance(0.3 * 0.45, 0) < 0);
        Assert.True(shape.SignedDistance(0, 0.3 * 0.25) > 0);
    }

    [Theory]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void Polygon_VertexCountIsLimited(int count, bool valid)
    {
        var points = Enumerable.Range(0, count).Select(i =>
        {
            var t = 2 * Math.PI * i / count;
            return FormattableString.Invariant($"[{0.5 * Math.Cos(t):0.######}, {0.5 * Math.Sin(t):0.######}]");
        });

        var result = ParseWithFingerboard("{ \"type\": \"hmd\" }", $"{{ \"type\": \"polygon\", \"vertices\": [{string.Join(", ", points)}] }}");

        Assert.Equal(valid, result.Success);
        if (!valid)
        {
            Assert.Contains(result.Errors, e => e.Contains("at most 128"));
        }
    }

    [Fact]
    public void DeviceSerial_IsTrimmed()
    {
        var result = ParseWithFingerboard("{ \"type\": \"device\", \"serial\": \"  LHR-1 \" }", "{ \"type\": \"ellipse\" }");

        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.Equal(new DeviceAnchorSpec(null, "LHR-1", LostBehavior.Hold), result.Config!.Regions[0].Anchor);
    }

    [Fact]
    public void ParsingTheSameConfigTwice_GivesEqualRegions()
    {
        // 設定を保存し直しても、変わっていない領域は描き直さない（MaskOverlay.Apply が同値判定で何もしない）。
        const string anchor = "{ \"type\": \"device\", \"role\": \"leftHand\", \"onLost\": \"hide\" }";
        const string shape = "{ \"type\": \"polygon\", \"vertices\": [[-0.5, -0.5], [0.5, -0.2], [0.3, 0.5]] }";

        var a = ParseWithFingerboard(anchor, shape).Config!;
        var b = ParseWithFingerboard(anchor, shape).Config!;

        Assert.Equal(a.Regions, b.Regions);
        Assert.Equal(a.Regions[0].GetHashCode(), b.Regions[0].GetHashCode());
        Assert.Equal(a.Render, b.Render);
    }

    [Theory]
    [InlineData("{ \"type\": \"world\" }", TrackingUniverse.Standing)]
    [InlineData("{ \"type\": \"world\", \"universe\": \"standing\" }", TrackingUniverse.Standing)]
    [InlineData("{ \"type\": \"world\", \"universe\": \"Seated\" }", TrackingUniverse.Seated)]
    public void WorldAnchor_IsParsed(string anchor, TrackingUniverse universe)
    {
        var result = ParseWithFingerboard(anchor, "{ \"type\": \"ellipse\" }");

        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.Equal(new WorldAnchorSpec(universe), result.Config!.Regions[0].Anchor);
    }

    [Theory]
    [InlineData("{ \"type\": \"device\", \"role\": \"leftHand\" }", DeviceRole.LeftHand, null, LostBehavior.Hold)]
    [InlineData("{ \"type\": \"device\", \"role\": \"RightHand\", \"onLost\": \"hide\" }", DeviceRole.RightHand, null, LostBehavior.Hide)]
    [InlineData("{ \"type\": \"device\", \"serial\": \"LHR-ABC\", \"onLost\": \"hold\" }", null, "LHR-ABC", LostBehavior.Hold)]
    public void DeviceAnchor_IsParsed(string anchor, DeviceRole? role, string? serial, LostBehavior onLost)
    {
        var result = ParseWithFingerboard(anchor, "{ \"type\": \"ellipse\" }");

        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.Equal(new DeviceAnchorSpec(role, serial, onLost), result.Config!.Regions[0].Anchor);
    }

    [Theory]
    [InlineData("{ \"type\": \"device\" }", "exactly one")]
    [InlineData("{ \"type\": \"device\", \"role\": \"leftHand\", \"serial\": \"x\" }", "exactly one")]
    [InlineData("{ \"type\": \"device\", \"role\": \"foot\" }", "anchor.role")]
    [InlineData("{ \"type\": \"device\", \"serial\": \" \" }", "anchor.serial")]
    [InlineData("{ \"type\": \"device\", \"role\": \"leftHand\", \"onLost\": \"fade\" }", "anchor.onLost")]
    [InlineData("{ \"type\": \"device\", \"role\": \"leftHand\", \"universe\": \"seated\" }", "anchor.universe")]
    [InlineData("{ \"type\": \"world\", \"universe\": \"space\" }", "anchor.universe")]
    [InlineData("{ \"type\": \"world\", \"role\": \"leftHand\" }", "anchor.role")]
    [InlineData("{ \"type\": \"hmd\", \"onLost\": \"hide\" }", "anchor.onLost")]
    [InlineData("{ \"type\": \"hmd\", \"universe\": \"seated\" }", "anchor.universe")]
    public void Anchor_InvalidDefinitions(string anchor, string expected)
    {
        var result = ParseWithFingerboard(anchor, "{ \"type\": \"ellipse\" }");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains(expected, StringComparison.Ordinal));
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
