using ViolinPassthrough.Anchor;
using ViolinPassthrough.Config;

namespace ViolinPassthrough.Tests.Config;

public class Phase7ConfigTests
{
    private const string DefaultTracking = "\"leftHandSerial\": null,\n    \"rightHandSerial\": null";

    [Fact]
    public void Defaults_UseTheHands()
    {
        Assert.Equal(HandDevices.Default, TestConfigs.Default.Hands);
    }

    [Fact]
    public void OmittedSection_UsesTheHands()
    {
        var json = TestConfigs.DefaultWith(",\n  \"tracking\": {\n    " + DefaultTracking + "\n  }", string.Empty);

        Assert.Equal(HandDevices.Default, TestConfigs.Parse(json).Hands);
    }

    [Fact]
    public void Serials_AreTrimmed()
    {
        var json = TestConfigs.DefaultWith(DefaultTracking, "\"leftHandSerial\": \" LHR-1234ABCD \",\n    \"rightHandSerial\": \"LHR-5678\"");

        Assert.Equal(new HandDevices("LHR-1234ABCD", "LHR-5678"), TestConfigs.Parse(json).Hands);
    }

    [Theory]
    [InlineData("\"leftHandSerial\": \"  \",\n    \"rightHandSerial\": null", "tracking.leftHandSerial must not be empty")]
    [InlineData("\"leftHandSerial\": \"LHR-1\",\n    \"rightHandSerial\": \"LHR-1\"", "must be different devices")]
    [InlineData("\"leftHandSerial\": \"lhr-1\",\n    \"rightHandSerial\": \"LHR-1\"", "must be different devices")]
    [InlineData("\"leftHandSerial\": null,\n    \"rightHandSerial\": null,\n    \"unknown\": 1", "unknown")]
    public void InvalidValues_AreRejected(string tracking, string expected)
    {
        var result = ConfigValidator.Parse(TestConfigs.DefaultWith(DefaultTracking, tracking));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void AutoDeviceSerial_DefaultsToNull_AndIsTrimmed()
    {
        Assert.Null(TestConfigs.Default.Auto.DeviceSerial);

        var json = TestConfigs.DefaultWith("\"deviceSerial\": null", "\"deviceSerial\": \" LHR-VIOLIN \"");

        Assert.Equal("LHR-VIOLIN", TestConfigs.Parse(json).Auto.DeviceSerial);
    }

    [Fact]
    public void AutoDeviceSerial_MustNotBeEmpty()
    {
        var result = ConfigValidator.Parse(TestConfigs.DefaultWith("\"deviceSerial\": null", "\"deviceSerial\": \"\""));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("auto.deviceSerial must not be empty", StringComparison.Ordinal));
    }

    [Fact]
    public void NullSection_IsRejected()
    {
        var result = ConfigValidator.Parse(TestConfigs.DefaultWith("\"tracking\": {\n    " + DefaultTracking + "\n  }", "\"tracking\": null"));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("tracking must be an object", StringComparison.Ordinal));
    }
}
