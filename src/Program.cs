using System.Runtime.InteropServices;
using ViolinPassthrough.App;
using ViolinPassthrough.Config;
using ViolinPassthrough.Logging;
using ViolinPassthrough.Mask;
using ViolinPassthrough.Osc;

namespace ViolinPassthrough;

internal static partial class Program
{
    /// <summary>多重起動防止の Mutex 名（ログオンセッション内で一意）。</summary>
    private const string MutexName = @"Local\ViolinPassthrough.SingleInstance";

    private const string Usage = """
        Usage: ViolinPassthrough.exe [options]
          --console      ログをコンソール窓にも出す
          --config PATH  設定ファイルの場所（既定: %APPDATA%\ViolinPassthrough\config.json）
          --unregister   SteamVR の自動起動とマニフェストの登録を解除して終了する
          --help         この説明を出す
        """;

    private static int Main(string[] args)
    {
        if (!CommandLine.TryParse(args, out var options, out var error))
        {
            AttachConsole();
            Console.Error.WriteLine(error);
            Console.Error.WriteLine(Usage);
            return 2;
        }

        if (options.ShowHelp)
        {
            AttachConsole();
            Console.WriteLine(Usage);
            return 0;
        }

        if (options.Console)
        {
            AttachConsole();
            Log.AddSink(Console.WriteLine);
        }

        using var fileLog = TryCreateFileLog();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("Unhandled exception", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));

        Log.Info($"Violin Passthrough {typeof(Program).Assembly.GetName().Version} starting ({Environment.ProcessPath}).");

        using var mutex = new Mutex(initiallyOwned: false, MutexName);
        if (!TryAcquire(mutex))
        {
            Log.Info("Another instance is already running. Exiting.");
            return 0;
        }

        try
        {
            return options.Unregister ? Unregister() : Run(options);
        }
        catch (Exception ex)
        {
            Log.Error("Fatal error", ex);
            return 1;
        }
        finally
        {
            mutex.ReleaseMutex();
            Log.Info("Violin Passthrough exiting.");
        }
    }

    private static int Run(CommandLineOptions options)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

        using var configStore = new ConfigStore(options.ConfigPath ?? ConfigStore.DefaultPath);
        configStore.Initialize();
        configStore.StartWatching();
        Log.MinLevel = configStore.Current.LogLevel;

        using var lifecycle = new AppLifecycle();
        if (!lifecycle.WaitForRuntimeAndInit(cts.Token))
        {
            return 0;
        }

        lifecycle.RegisterManifest(ManifestPath, Environment.ProcessPath ?? string.Empty);

        var state = MainLoop.CreateInitialState(configStore.Current);
        Log.Info($"Initial state: {state}.");

        using (var overlays = new MaskOverlaySet(id => new MaskOverlay(id)))
        using (var loop = new MainLoop(lifecycle, configStore, state, overlays, StartOsc))
        {
            if (loop.Run(cts.Token))
            {
                // SteamVR の終了: 応答してから、オーバーレイを破棄して終了する。
                lifecycle.AcknowledgeQuit();
            }
        }

        return 0;
    }

    private static int Unregister()
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        Log.Info("Unregistering. Start SteamVR if it is not running.");
        using var lifecycle = new AppLifecycle();
        if (!lifecycle.WaitForRuntimeAndInit(cts.Token))
        {
            return 1;
        }

        lifecycle.UnregisterManifest(ManifestPath);
        return 0;
    }

    private static string ManifestPath => Path.Combine(AppContext.BaseDirectory, AppLifecycle.ManifestFileName);

    private static IOscEndpoint? StartOsc(OscSettings settings)
    {
        try
        {
            return OscService.Start(settings);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to start OSC", ex);
            return null;
        }
    }

    private static FileLogSink? TryCreateFileLog()
    {
        try
        {
            var sink = new FileLogSink(FileLogSink.DefaultDirectory);
            Log.AddSink(sink.Write);
            return sink;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ログが書けなくても動作は続ける。
            Console.Error.WriteLine($"Cannot write log files: {ex.Message}");
            return null;
        }
    }

    private static bool TryAcquire(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // 前のインスタンスが異常終了した。所有権はこちらに移っている。
            Log.Warn("The previous instance did not exit cleanly.");
            return true;
        }
    }

    /// <summary>WinExe なのでコンソールが無い。親のコンソールに繋ぐか、新しく開く。</summary>
    private static void AttachConsole()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const int AttachParentProcess = -1;
        if (!AttachConsoleNative(AttachParentProcess))
        {
            AllocConsoleNative();
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "AttachConsole")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsoleNative(int processId);

    [LibraryImport("kernel32.dll", EntryPoint = "AllocConsole")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsoleNative();
}

internal sealed record CommandLineOptions(bool Console, bool Unregister, bool ShowHelp, string? ConfigPath);

internal static class CommandLine
{
    public static bool TryParse(string[] args, out CommandLineOptions options, out string error)
    {
        var console = false;
        var unregister = false;
        var help = false;
        string? configPath = null;
        error = string.Empty;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--console":
                    console = true;
                    break;
                case "--unregister":
                    unregister = true;
                    break;
                case "--help":
                case "-h":
                case "/?":
                    help = true;
                    break;
                case "--config":
                    if (i + 1 >= args.Length)
                    {
                        error = "--config requires a path.";
                        options = new CommandLineOptions(false, false, false, null);
                        return false;
                    }

                    configPath = args[++i];
                    break;
                default:
                    error = $"Unknown option: {args[i]}";
                    options = new CommandLineOptions(false, false, false, null);
                    return false;
            }
        }

        options = new CommandLineOptions(console, unregister, help, configPath);
        return true;
    }
}
