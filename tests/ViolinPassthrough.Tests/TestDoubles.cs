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

    public int InvalidCount { get; set; }

    public List<ViolinPassthrough.Calibration.CalibrationResult> Calibrations { get; } = new();

    public string? CalibrationError { get; set; }

    public bool TryApplyCalibration(ViolinPassthrough.Calibration.CalibrationResult result, out string error)
    {
        error = CalibrationError ?? string.Empty;
        if (CalibrationError is not null)
        {
            return false;
        }

        Calibrations.Add(result);
        return true;
    }

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

internal sealed class FakeBeeper : ViolinPassthrough.Feedback.IBeeper
{
    public List<ViolinPassthrough.Feedback.BeepKind> Beeps { get; } = new();

    public void Beep(ViolinPassthrough.Feedback.BeepKind kind) => Beeps.Add(kind);
}

internal sealed class FakeStatusOverlay : ViolinPassthrough.Feedback.IStatusOverlay
{
    public int ShowCount { get; private set; }

    public bool Visible { get; private set; }

    public MaskTexture? Texture { get; private set; }

    public double WidthMeters { get; private set; }

    public Pose3x4 Pose { get; private set; }

    public uint SortOrder { get; private set; }

    public void Show(MaskTexture texture, double widthMeters, Pose3x4 hmdRelativePose, uint sortOrder)
    {
        ShowCount++;
        Visible = true;
        Texture = texture;
        WidthMeters = widthMeters;
        Pose = hmdRelativePose;
        SortOrder = sortOrder;
    }

    public void Hide() => Visible = false;

    public void Dispose()
    {
    }
}

/// <summary>HMD を 0 番、左手を 1 番、右手を 2 番に置いたトラッキング情報を作る。</summary>
internal static class Tracking
{
    public const uint Left = 1;
    public const uint Right = 2;

    public static TrackingSnapshot Make(Pose3x4 hmd, Vec3? leftLocal, Vec3? rightLocal, bool hmdValid = true)
    {
        DevicePose At(Vec3? local) => local is { } p
            ? new DevicePose(true, true, hmd * (Pose3x4.Identity with { M03 = p.X, M13 = p.Y, M23 = p.Z }))
            : new DevicePose(false, false, Pose3x4.Identity);

        var poses = new[] { new DevicePose(true, hmdValid, hmd), At(leftLocal), At(rightLocal) };
        return new TrackingSnapshot(TrackingUniverse.Standing, poses, Left, Right, new Dictionary<string, uint>());
    }

    /// <summary>立って正面を向いた HMD（床から 1.6 m）。</summary>
    public static Pose3x4 StandingHmd(double yawDeg = 0, double rollDeg = 0) =>
        Pose3x4.FromPositionEuler(new Vec3(0.2, 1.6, -0.1), new Vec3(0, yawDeg, rollDeg));
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
