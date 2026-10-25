using System.Runtime.InteropServices;
using ViolinPassthrough.App;
using ViolinPassthrough.Config;
using ViolinPassthrough.Feedback;
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

    private enum ConsoleKind
    {
        None,
        Attached,
        Allocated,
    }

    private static int Main(string[] args)
    {
        if (!CommandLine.TryParse(args, out var options, out var error))
        {
            var kind = AttachConsole();
            Console.Error.WriteLine(error);
            Console.Error.WriteLine(Usage);
            PauseIfAllocated(kind);
            return 2;
        }

        if (options.ShowHelp)
        {
            var kind = AttachConsole();
            Console.WriteLine(Usage);
            PauseIfAllocated(kind);
            return 0;
        }

        if (options.Unregister)
        {
            // 起動中のインスタンス（SteamVR が自動起動したもの）がいても実行できるように、Mutex は取らない。
            // そのインスタンスのログを壊さないように、ファイルには書かずコンソールにだけ出す。
            var kind = AttachConsole();
            Log.AddSink(Console.WriteLine);
            var code = Unregister();
            PauseIfAllocated(kind);
            return code;
        }

        if (options.Console)
        {
            AttachConsole();
            Log.AddSink(Console.WriteLine);
        }

        using var mutex = new Mutex(initiallyOwned: false, MutexName);
        if (!TryAcquire(mutex, out var abandoned))
        {
            // 起動中のインスタンスのログを壊さないように、ファイルログを開く前に終える。
            Log.Info("Another instance is already running. Exiting.");
            return 0;
        }

        try
        {
            using var fileLog = TryCreateFileLog();
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Log.Error("Unhandled exception", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));

            Log.Info($"Violin Passthrough {typeof(Program).Assembly.GetName().Version} starting ({Environment.ProcessPath}).");
            if (abandoned)
            {
                Log.Warn("The previous instance did not exit cleanly.");
            }

            try
            {
                return Run(options);
            }
            catch (Exception ex)
            {
                Log.Error("Fatal error", ex);
                return 1;
            }
            finally
            {
                Log.Info("Violin Passthrough exiting.");
            }
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static int Run(CommandLineOptions options)
    {
        using var cts = new CancellationTokenSource();
        // Ctrl+C（--console のとき）で終了する。cts を破棄した後に呼ばれないよう、終わったら外す。
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
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

            var status = new StatusDisplay(new VrStatusOverlay());
            using (var overlays = new MaskOverlaySet(id => new MaskOverlay(id)))
            using (var loop = new MainLoop(lifecycle, configStore, state, overlays, StartOsc, status: status, beeper: new WindowsBeeper()))
            {
                if (loop.Run(cts.Token))
                {
                    // SteamVR の終了: 応答してから、オーバーレイを破棄して終了する。
                    lifecycle.AcknowledgeQuit();
                }
            }

            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    private static int Unregister()
    {
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            Log.Info("Unregistering from SteamVR. Start SteamVR if it is not running (Ctrl+C to cancel).");
            using var lifecycle = new AppLifecycle();
            if (!lifecycle.WaitForRuntimeAndInit(cts.Token))
            {
                return 1;
            }

            return lifecycle.UnregisterManifest(ManifestPath) ? 0 : 1;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
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

    private static bool TryAcquire(Mutex mutex, out bool abandoned)
    {
        abandoned = false;
        try
        {
            return mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // 前のインスタンスが異常終了した。所有権はこちらに移っている。
            abandoned = true;
            return true;
        }
    }

    /// <summary>WinExe なのでコンソールが無い。親のコンソールに繋ぐか、新しく開く。</summary>
    private static ConsoleKind AttachConsole()
    {
        if (!OperatingSystem.IsWindows())
        {
            return ConsoleKind.Attached;
        }

        const int AttachParentProcess = -1;
        if (AttachConsoleNative(AttachParentProcess))
        {
            return ConsoleKind.Attached;
        }

        return AllocConsoleNative() ? ConsoleKind.Allocated : ConsoleKind.None;
    }

    /// <summary>新しく開いた窓は終了と同時に閉じるので、読めるようにキー入力を待つ。</summary>
    private static void PauseIfAllocated(ConsoleKind kind)
    {
        if (kind != ConsoleKind.Allocated)
        {
            return;
        }

        try
        {
            Console.WriteLine("Press any key to close.");
            Console.ReadKey(intercept: true);
        }
        catch (InvalidOperationException)
        {
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
