using System.Diagnostics;
using ViolinPassthrough.Logging;
using ViolinPassthrough.Mask;

namespace ViolinPassthrough.App;

/// <summary>約 30Hz の更新ループ（イベント処理・状態反映）。</summary>
public sealed class MainLoop
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(1000.0 / 30.0);

    private readonly AppLifecycle _lifecycle;
    private readonly IReadOnlyList<IMaskOverlay> _overlays;

    public MainLoop(AppLifecycle lifecycle, IReadOnlyList<IMaskOverlay> overlays)
    {
        _lifecycle = lifecycle;
        _overlays = overlays;
    }

    /// <summary>SteamVR から終了を求められるか、キャンセルされるまで回る。</summary>
    /// <returns>SteamVR から終了を求められたら true。</returns>
    public bool Run(CancellationToken cancellationToken)
    {
        var stopwatch = new Stopwatch();
        while (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Restart();

            var events = _lifecycle.PollEvents();
            if (events.HasFlag(AppEvents.QuitRequested))
            {
                return true;
            }

            foreach (var overlay in _overlays)
            {
                overlay.Tick(null);
            }

            var remaining = TickInterval - stopwatch.Elapsed;
            if (remaining > TimeSpan.Zero && cancellationToken.WaitHandle.WaitOne(remaining))
            {
                break;
            }
        }

        Log.Info("Main loop cancelled.");
        return false;
    }
}
