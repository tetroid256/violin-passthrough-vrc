using System.Globalization;

namespace ViolinPassthrough.Logging;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>
/// アプリ全体で使う最小限のロガー。どのスレッドから呼んでもよい。
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly List<Action<string>> Sinks = new();

    public static LogLevel MinLevel { get; set; } = LogLevel.Info;

    public static void AddSink(Action<string> sink)
    {
        lock (Gate)
        {
            Sinks.Add(sink);
        }
    }

    public static void ClearSinks()
    {
        lock (Gate)
        {
            Sinks.Clear();
        }
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warning, message);
    public static void Error(string message) => Write(LogLevel.Error, message);
    public static void Error(string message, Exception ex) => Write(LogLevel.Error, $"{message}: {ex}");

    public static void Write(LogLevel level, string message)
    {
        if (level < MinLevel)
        {
            return;
        }

        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{LevelName(level)}] {message}");

        lock (Gate)
        {
            foreach (var sink in Sinks)
            {
                try
                {
                    sink(line);
                }
                catch
                {
                    // ログ出力の失敗でアプリを止めない。
                }
            }
        }
    }

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO ",
        LogLevel.Warning => "WARN ",
        LogLevel.Error => "ERROR",
        _ => "?????",
    };
}
