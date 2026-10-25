using ViolinPassthrough.Logging;

namespace ViolinPassthrough.Tests.Logging;

public sealed class FileLogSinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vp-log-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 10, 25, 23, 59, 0);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Write_AppendsToDailyFile_ReadableWhileOpen()
    {
        using var sink = new FileLogSink(_dir, now: () => _now);

        sink.Write("first");
        sink.Write("second");

        Assert.Equal(Path.Combine(_dir, "ViolinPassthrough-20261025.log"), sink.CurrentPath);
        using var reader = new StreamReader(new FileStream(sink.CurrentPath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        Assert.Equal("first" + Environment.NewLine + "second" + Environment.NewLine, reader.ReadToEnd());
    }

    [Fact]
    public void Write_RollsOverAtMidnight()
    {
        using var sink = new FileLogSink(_dir, now: () => _now);
        sink.Write("old day");

        _now = _now.AddMinutes(2);
        sink.Write("new day");

        Assert.EndsWith("ViolinPassthrough-20261026.log", sink.CurrentPath);
        Assert.Equal(2, Directory.GetFiles(_dir).Length);
    }

    [Fact]
    public void Open_DeletesFilesOlderThanKeepDays()
    {
        Directory.CreateDirectory(_dir);
        foreach (var day in new[] { "20261010", "20261018", "20261019", "20261024" })
        {
            File.WriteAllText(Path.Combine(_dir, $"ViolinPassthrough-{day}.log"), "x");
        }

        File.WriteAllText(Path.Combine(_dir, "other.log"), "keep");
        File.WriteAllText(Path.Combine(_dir, "ViolinPassthrough-notadate.log"), "keep");

        using var sink = new FileLogSink(_dir, keepDays: 7, now: () => _now);
        sink.Write("x");

        var names = Directory.GetFiles(_dir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(
            new[] { "ViolinPassthrough-20261019.log", "ViolinPassthrough-20261024.log", "ViolinPassthrough-20261025.log", "ViolinPassthrough-notadate.log", "other.log" },
            names);
    }
}
