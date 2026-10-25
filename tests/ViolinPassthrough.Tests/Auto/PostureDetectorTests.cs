using ViolinPassthrough.Anchor;
using ViolinPassthrough.Auto;

namespace ViolinPassthrough.Tests.Auto;

public class PostureDetectorTests
{
    private static readonly Vec3 Reference = new(-0.35, -0.10, -0.35);
    private static readonly Pose3x4 Hmd = Tracking.StandingHmd(yawDeg: 30);

    private readonly ManualTimeProvider _time = new();

    private static AutoSettings Settings(bool leftHand = true, bool headTilt = false, Vec3? headUp = null) =>
        new(true, leftHand, headTilt, Reference, headUp, 0.15, 15, 0.3, 2.0);

    private bool Step(PostureDetector detector, AutoSettings settings, TrackingSnapshot tracking, double seconds = 0.1)
    {
        var result = detector.Update(settings, tracking);
        _time.Advance(TimeSpan.FromSeconds(seconds));
        return result;
    }

    [Fact]
    public void Enters_AfterTheEnterDelay()
    {
        var detector = new PostureDetector(_time);
        var playing = Tracking.Make(Hmd, Reference + new Vec3(0.05, 0, 0), null);

        Assert.False(Step(detector, Settings(), playing)); // 0.0 s
        Assert.False(Step(detector, Settings(), playing)); // 0.1 s
        Assert.False(Step(detector, Settings(), playing)); // 0.2 s
        Assert.True(Step(detector, Settings(), playing));  // 0.3 s
    }

    [Fact]
    public void BriefMovementOutside_DoesNotHide_ButLongerOneDoes()
    {
        var detector = new PostureDetector(_time);
        var playing = Tracking.Make(Hmd, Reference, null);
        var lowered = Tracking.Make(Hmd, new Vec3(-0.2, -0.6, -0.1), null);
        for (var i = 0; i < 5; i++)
        {
            Step(detector, Settings(), playing);
        }

        Assert.True(detector.InPosture);

        // 1 秒だけ外れて戻る: 表示のまま。
        for (var i = 0; i < 10; i++)
        {
            Assert.True(Step(detector, Settings(), lowered));
        }

        Assert.True(Step(detector, Settings(), playing));

        // 2 秒以上外れる: 隠す。
        var results = Enumerable.Range(0, 22).Select(_ => Step(detector, Settings(), lowered)).ToList();
        Assert.True(results[0]);
        Assert.False(results[^1]);
    }

    [Fact]
    public void Hysteresis_KeepsPostureSlightlyOutsideTheRadius()
    {
        var detector = new PostureDetector(_time);
        var settings = Settings() with { ExitDelaySeconds = 0 };
        for (var i = 0; i < 5; i++)
        {
            Step(detector, settings, Tracking.Make(Hmd, Reference, null));
        }

        // 半径 0.15 の 1.2 倍: 入るときは外だが、構えている間は内側とみなす。
        var slightlyOut = Tracking.Make(Hmd, Reference + new Vec3(0.18, 0, 0), null);
        Assert.True(Step(detector, settings, slightlyOut));
        Assert.Equal(false, PostureDetector.Evaluate(settings, slightlyOut, currentlyInPosture: false));

        var farOut = Tracking.Make(Hmd, Reference + new Vec3(0.21, 0, 0), null);
        Assert.False(Step(detector, settings, farOut));
    }

    [Fact]
    public void LostTracking_KeepsTheLastDecision()
    {
        var detector = new PostureDetector(_time);
        for (var i = 0; i < 5; i++)
        {
            Step(detector, Settings(), Tracking.Make(Hmd, Reference, null));
        }

        var handLost = Tracking.Make(Hmd, null, null);
        var hmdLost = Tracking.Make(Hmd, Reference, null, hmdValid: false);
        for (var i = 0; i < 50; i++)
        {
            Assert.True(Step(detector, Settings(), i % 2 == 0 ? handLost : hmdLost));
        }
    }

    [Fact]
    public void WorksRegardlessOfWhereThePlayerFaces()
    {
        foreach (var yaw in new[] { 0.0, 90, 180, -120 })
        {
            var hmd = Tracking.StandingHmd(yawDeg: yaw);
            Assert.Equal(true, PostureDetector.Evaluate(Settings(), Tracking.Make(hmd, Reference, null), false));
        }
    }

    [Fact]
    public void HeadTilt_UsesWorldUpSeenFromTheHmd()
    {
        // 構えたとき頭を左に 20° 傾けていた。
        var tilted = Tracking.StandingHmd(rollDeg: 20);
        var headUp = tilted.Inverse().TransformDirection(new Vec3(0, 1, 0));
        var settings = Settings(leftHand: false, headTilt: true, headUp: headUp);

        Assert.Equal(true, PostureDetector.Evaluate(settings, Tracking.Make(Tracking.StandingHmd(yawDeg: 140, rollDeg: 25), null, null), false));
        Assert.Equal(false, PostureDetector.Evaluate(settings, Tracking.Make(Tracking.StandingHmd(), null, null), false));
    }

    [Fact]
    public void BothConditions_MustHold()
    {
        var tilted = Tracking.StandingHmd(rollDeg: 20);
        var headUp = tilted.Inverse().TransformDirection(new Vec3(0, 1, 0));
        var settings = Settings(leftHand: true, headTilt: true, headUp: headUp);

        Assert.Equal(true, PostureDetector.Evaluate(settings, Tracking.Make(tilted, Reference, null), false));
        Assert.Equal(false, PostureDetector.Evaluate(settings, Tracking.Make(Tracking.StandingHmd(), Reference, null), false));
        Assert.Equal(false, PostureDetector.Evaluate(settings, Tracking.Make(tilted, new Vec3(0.3, -0.5, 0), null), false));
    }

    [Fact]
    public void Reset_ClearsPosture()
    {
        var detector = new PostureDetector(_time);
        for (var i = 0; i < 5; i++)
        {
            Step(detector, Settings(), Tracking.Make(Hmd, Reference, null));
        }

        detector.Reset();

        Assert.False(detector.InPosture);
    }
}
