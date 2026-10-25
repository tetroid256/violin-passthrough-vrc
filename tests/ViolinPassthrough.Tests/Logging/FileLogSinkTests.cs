using ViolinPassthrough.Logging;

namespace ViolinPassthrough.Tests.Logging;

public sealed class FileLogSinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vp-log-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 10, 25, 23, 59, 0);

    /// <summary>
    /// 書き込み中のログファイルを読む。Windows では File.ReadAllText だと書き込み側と共有できずに失敗する
    /// （ユーザーがメモ帳などで開くときと同じく、書き込みを許して開く）。
    /// </summary>
    private static string ReadShared(string path)
    {
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        return reader.ReadToEnd();
    }

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
        Assert.Equal("first" + Environment.NewLine + "second" + Environment.NewLine, ReadShared(sink.CurrentPath!));
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
    public void Write_AfterDispose_DoesNothing()
    {
        var sink = new FileLogSink(_dir, now: () => _now);
        sink.Write("before");
        sink.Dispose();

        sink.Write("after");

        Assert.Equal("before" + Environment.NewLine, ReadShared(sink.CurrentPath!));
    }

    [Fact]
    public void Write_RecreatesTheFolderIfItIsMissing()
    {
        using var sink = new FileLogSink(_dir, now: () => _now);
        Directory.Delete(_dir);

        sink.Write("x");

        Assert.Equal("x" + Environment.NewLine, ReadShared(sink.CurrentPath!));
    }

    [Fact]
    public void Write_RecoversAfterAFailedOpen()
    {
        using var sink = new FileLogSink(_dir, now: () => _now);
        sink.Write("first");

        // 翌日のファイル名と同じ名前のフォルダがあると、日付が変わったときに開けない。
        var blocker = Path.Combine(_dir, "ViolinPassthrough-20261026.log");
        Directory.CreateDirectory(blocker);
        _now = _now.AddMinutes(2);
        Assert.ThrowsAny<Exception>(() => sink.Write("lost"));

        // 開けるようになったら、次の書き込みで開き直す（壊れた writer を使い続けない）。
        Directory.Delete(blocker);
        sink.Write("next day");

        Assert.Equal(blocker, sink.CurrentPath);
        Assert.Equal("next day" + Environment.NewLine, ReadShared(blocker));
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
