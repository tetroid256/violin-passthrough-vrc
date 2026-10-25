namespace ViolinPassthrough.Logging;

/// <summary>
/// 日付ごとのファイルにログを書く（ViolinPassthrough-yyyyMMdd.log）。古いファイルは消す。
/// 実行中でもエディタで開けるように、読み書き共有で開く。
/// </summary>
public sealed class FileLogSink : IDisposable
{
    public const string FilePrefix = "ViolinPassthrough-";
    public const string FileExtension = ".log";

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly int _keepDays;
    private readonly Func<DateTime> _now;
    private StreamWriter? _writer;
    private DateOnly _currentDate;

    public FileLogSink(string directory, int keepDays = 7, Func<DateTime>? now = null)
    {
        _directory = directory;
        _keepDays = keepDays;
        _now = now ?? (() => DateTime.Now);
        Directory.CreateDirectory(directory);
    }

    /// <summary>%APPDATA%\ViolinPassthrough\logs</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ViolinPassthrough",
        "logs");

    public string? CurrentPath { get; private set; }

    public void Write(string line)
    {
        lock (_gate)
        {
            var today = DateOnly.FromDateTime(_now());
            if (_writer is null || today != _currentDate)
            {
                Open(today);
            }

            _writer!.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Open(DateOnly date)
    {
        _writer?.Dispose();
        _currentDate = date;
        CurrentPath = Path.Combine(_directory, $"{FilePrefix}{date:yyyyMMdd}{FileExtension}");
        var stream = new FileStream(CurrentPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(stream) { AutoFlush = true };
        DeleteOldFiles(date);
    }

    private void DeleteOldFiles(DateOnly today)
    {
        var oldest = today.AddDays(-(_keepDays - 1));
        foreach (var file in Directory.EnumerateFiles(_directory, FilePrefix + "*" + FileExtension))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)[FilePrefix.Length..];
            if (DateOnly.TryParseExact(stamp, "yyyyMMdd", out var date) && date < oldest)
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // 他のプロセスが開いているなど。次の機会に消す。
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
