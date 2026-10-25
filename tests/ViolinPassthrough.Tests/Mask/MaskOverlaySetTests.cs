using ViolinPassthrough.Mask;

namespace ViolinPassthrough.Tests.Mask;

public class MaskOverlaySetTests
{
    private static readonly HashSet<string> None = new();

    [Fact]
    public void Sync_CreatesOneOverlayPerRegionAndShowsOnlyVisibleOnes()
    {
        var factory = new FakeOverlayFactory();
        using var set = new MaskOverlaySet(factory.Create);
        var config = TestConfigs.Default;

        set.Sync(config.Regions, config.Render, new HashSet<string> { "fingerboard" });

        Assert.Equal(new[] { "fingerboard", "body" }, factory.Created.Select(o => o.RegionId));
        Assert.True(factory["fingerboard"].Visible);
        Assert.False(factory["body"].Visible);
        Assert.Equal(config.Regions[0], factory["fingerboard"].Region);
        Assert.Equal(config.Render, factory["fingerboard"].Settings);
    }

    [Fact]
    public void Sync_RemovesOverlaysOfDeletedRegions()
    {
        var factory = new FakeOverlayFactory();
        using var set = new MaskOverlaySet(factory.Create);
        var config = TestConfigs.Default;
        set.Sync(config.Regions, config.Render, None);

        set.Sync(config.Regions.Where(r => r.Id == "fingerboard").ToList(), config.Render, None);

        Assert.True(factory["body"].Disposed);
        Assert.False(factory["fingerboard"].Disposed);
        Assert.Equal(new[] { "fingerboard" }, set.RegionIds);
    }

    [Fact]
    public void Sync_DoesNotRecreateUnchangedOverlays()
    {
        var factory = new FakeOverlayFactory();
        using var set = new MaskOverlaySet(factory.Create);
        var config = TestConfigs.Default;

        set.Sync(config.Regions, config.Render, None);
        set.Sync(config.Regions, config.Render, None);

        Assert.Equal(2, factory.Created.Count);
        Assert.All(factory.Created, o => Assert.Equal(1, o.ApplyCount));
    }

    [Fact]
    public void Sync_ReappliesWhenSettingsChange()
    {
        var factory = new FakeOverlayFactory();
        using var set = new MaskOverlaySet(factory.Create);
        var config = TestConfigs.Default;
        set.Sync(config.Regions, config.Render, None);

        set.Sync(config.Regions, config.Render with { Debug = true }, None);

        Assert.All(factory.Created, o => Assert.True(o.Settings!.Debug));
        Assert.All(factory.Created, o => Assert.Equal(2, o.ApplyCount));
    }

    [Fact]
    public void Dispose_DisposesAllOverlays()
    {
        var factory = new FakeOverlayFactory();
        var set = new MaskOverlaySet(factory.Create);
        var config = TestConfigs.Default;
        set.Sync(config.Regions, config.Render, None);

        set.Dispose();

        Assert.All(factory.Created, o => Assert.True(o.Disposed));
    }

    [Fact]
    public void RequiresTracking_ReflectsOverlays()
    {
        var factory = new FakeOverlayFactory();
        using var set = new MaskOverlaySet(factory.Create);
        var config = TestConfigs.Default;
        set.Sync(config.Regions, config.Render, None);
        Assert.False(set.RequiresTracking);

        factory["body"].RequiresTracking = true;
        set.Tick(null);

        Assert.True(set.RequiresTracking);
        Assert.All(factory.Created, o => Assert.Equal(1, o.TickCount));
    }
}
