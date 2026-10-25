using ViolinPassthrough.Anchor;
using ViolinPassthrough.App;
using ViolinPassthrough.Config;
using ViolinPassthrough.Mask;
using ViolinPassthrough.Osc;

namespace ViolinPassthrough.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private TimeSpan _wallOffset;

    public override DateTimeOffset GetUtcNow() => _now + _wallOffset;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _now.UtcTicks;

    /// <summary>壁時計だけを動かす（単調時計は動かさない）。</summary>
    public void StepWallClock(TimeSpan delta) => _wallOffset += delta;

    public void Advance(TimeSpan delta) => _now += delta;
}

internal sealed class FakeOverlay : IMaskOverlay
{
    public FakeOverlay(string regionId)
    {
        RegionId = regionId;
    }

    public string RegionId { get; }

    public bool RequiresTracking { get; set; }

    public MaskRegion? Region { get; private set; }

    public RenderSettings? Settings { get; private set; }

    public bool Visible { get; private set; }

    public bool Disposed { get; private set; }

    public int ApplyCount { get; private set; }

    public int TickCount { get; private set; }

    public TrackingSnapshot? LastTracking { get; private set; }

    public void Apply(MaskRegion region, RenderSettings settings)
    {
        if (Equals(region, Region) && Equals(settings, Settings))
        {
            return;
        }

        Region = region;
        Settings = settings;
        ApplyCount++;
    }

    public void SetVisible(bool visible) => Visible = visible;

    public void Tick(TrackingSnapshot? tracking)
    {
        TickCount++;
        LastTracking = tracking;
    }

    public void Dispose() => Disposed = true;
}

internal sealed class FakeOverlayFactory
{
    public List<FakeOverlay> Created { get; } = new();

    public FakeOverlay this[string id] => Created.Last(o => o.RegionId == id);

    public IMaskOverlay Create(string id)
    {
        var overlay = new FakeOverlay(id);
        Created.Add(overlay);
        return overlay;
    }
}

internal sealed class FakeLifecycle : IAppLifecycle
{
    public AppEvents NextEvents { get; set; }

    public TrackingSnapshot Snapshot { get; set; } = new(
        TrackingUniverse.Standing,
        Array.Empty<DevicePose>(),
        TrackingSnapshot.InvalidDeviceIndex,
        TrackingSnapshot.InvalidDeviceIndex,
        new Dictionary<string, uint>());

    public int SnapshotCount { get; private set; }

    public TrackingSnapshot GetTrackingSnapshot()
    {
        SnapshotCount++;
        return Snapshot;
    }

    public AppEvents PollEvents()
    {
        var events = NextEvents;
        NextEvents = AppEvents.None;
        return events;
    }
}

internal sealed class FakeConfigSource : IConfigSource
{
    private ResolvedConfig? _pending;

    public FakeConfigSource(ResolvedConfig initial)
    {
        Current = initial;
    }

    public ResolvedConfig Current { get; private set; }

    public void Publish(ResolvedConfig config) => _pending = config;

    public bool TryReload(out ResolvedConfig? config)
    {
        config = _pending;
        _pending = null;
        if (config is null)
        {
            return false;
        }

        Current = config;
        return true;
    }
}

internal sealed class FakeOscEndpoint : IOscEndpoint
{
    public FakeOscEndpoint(OscSettings settings)
    {
        Settings = settings;
    }

    public OscSettings Settings { get; }

    public Queue<OscMessage> Incoming { get; } = new();

    public List<OscMessage> Sent { get; } = new();

    public bool Disposed { get; private set; }

    public bool TryReceive([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out OscMessage? message) =>
        Incoming.TryDequeue(out message);

    public void Send(OscMessage message) => Sent.Add(message);

    public void Dispose() => Disposed = true;

    /// <summary>送ったメッセージを取り出して空にする。</summary>
    public List<string> TakeSent()
    {
        var sent = Sent.Select(m => m.ToString()).ToList();
        Sent.Clear();
        return sent;
    }
}

internal static class TestConfigs
{
    public static string DefaultJson => ConfigStore.ReadDefaultConfigText();

    public static ResolvedConfig Default => Parse(DefaultJson);

    public static ResolvedConfig Parse(string json)
    {
        var result = ConfigValidator.Parse(json);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        return result.Config!;
    }

    /// <summary>既定の設定の JSON の一部を置き換える。</summary>
    public static string DefaultWith(string oldText, string newText)
    {
        var json = DefaultJson;
        Assert.Contains(oldText, json);
        return json.Replace(oldText, newText);
    }
}
