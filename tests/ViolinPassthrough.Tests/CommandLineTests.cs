namespace ViolinPassthrough.Tests;

public class CommandLineTests
{
    [Fact]
    public void NoArguments_UseDefaults()
    {
        Assert.True(CommandLine.TryParse(Array.Empty<string>(), out var options, out _));

        Assert.Equal(new CommandLineOptions(false, false, false, null), options);
    }

    [Fact]
    public void AllOptions_AreParsed()
    {
        Assert.True(CommandLine.TryParse(new[] { "--console", "--CONFIG", @"C:\x\config.json", "--unregister" }, out var options, out _));

        Assert.Equal(new CommandLineOptions(true, true, false, @"C:\x\config.json"), options);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("/?")]
    public void Help_IsRecognized(string arg)
    {
        Assert.True(CommandLine.TryParse(new[] { arg }, out var options, out _));
        Assert.True(options.ShowHelp);
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("--config")]
    public void InvalidArguments_AreRejected(string arg)
    {
        Assert.False(CommandLine.TryParse(new[] { arg }, out _, out var error));
        Assert.NotEmpty(error);
    }
}
