using ViolinPassthrough.Anchor;
using ViolinPassthrough.App;
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

        using var lifecycle = new AppLifecycle();
        if (!lifecycle.WaitForRuntimeAndInit(cts.Token))
        {
            return 0;
        }

        // Phase 1: 設定はハードコード。
        var region = new MaskRegion(
            Id: "fingerboard",
            Anchor: new HmdAnchorSpec(),
            Shape: new EllipseShape(0.45 / 2, 0.25 / 2),
            Position: new Vec3(-0.15, -0.15, -0.40),
            RotationEulerDeg: new Vec3(15, 20, -15),
            WidthMeters: 0.45,
            HeightMeters: 0.25,
            MarginMeters: 0.03);
        var settings = new RenderSettings(
            KeyColor: new Rgb(255, 0, 255),
            EdgeMode: EdgeMode.Hard,
            TextureLongSide: 512,
            SortOrder: 100,
            Debug: false);

        var overlay = new MaskOverlay(region.Id);
        try
        {
            overlay.Apply(region, settings);
            overlay.SetVisible(true);

            var quitRequested = new MainLoop(lifecycle, new[] { overlay }).Run(cts.Token);
            if (quitRequested)
            {
                lifecycle.AcknowledgeQuit();
            }
        }
        finally
        {
            overlay.Dispose();
        }

        Log.Info("Violin Passthrough exiting.");
        return 0;
    }
}
