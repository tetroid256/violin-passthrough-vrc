using ViolinPassthrough.App;
using ViolinPassthrough.Config;
using ViolinPassthrough.Logging;
using ViolinPassthrough.Mask;

namespace ViolinPassthrough;

internal static class Program
{
    private static int Main()
    {
        Log.AddSink(Console.WriteLine);
        Log.Info("Violin Passthrough starting.");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        using var configStore = new ConfigStore(ConfigStore.DefaultPath);
        configStore.Initialize();
        configStore.StartWatching();

        using var lifecycle = new AppLifecycle();
        if (!lifecycle.WaitForRuntimeAndInit(cts.Token))
        {
            return 0;
        }

        var state = MainLoop.CreateInitialState(configStore.Current);
        Log.Info($"Initial state: {state}.");

        using (var overlays = new MaskOverlaySet(id => new MaskOverlay(id)))
        {
            var loop = new MainLoop(lifecycle, configStore, state, overlays);
            if (loop.Run(cts.Token))
            {
                lifecycle.AcknowledgeQuit();
            }
        }

        Log.Info("Violin Passthrough exiting.");
        return 0;
    }
}
