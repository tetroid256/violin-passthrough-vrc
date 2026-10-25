using ViolinPassthrough.Anchor;
using ViolinPassthrough.Logging;

namespace ViolinPassthrough.Mask;

/// <summary>
/// 設定の領域一覧とオーバーレイを対応させる。領域が増えたら作り、消えたら破棄する。
/// </summary>
public sealed class MaskOverlaySet : IDisposable
{
    private readonly Func<string, IMaskOverlay> _factory;
    private readonly Dictionary<string, IMaskOverlay> _overlays = new(StringComparer.Ordinal);

    public MaskOverlaySet(Func<string, IMaskOverlay> factory)
    {
        _factory = factory;
    }

    public IReadOnlyCollection<string> RegionIds => _overlays.Keys;

    public bool RequiresTracking => _overlays.Values.Any(o => o.RequiresTracking);

    /// <summary>
    /// 領域・描画設定・表示する領域を反映する。変わっていない領域は何もしない。
    /// </summary>
    public void Sync(IReadOnlyList<MaskRegion> regions, RenderSettings settings, IReadOnlySet<string> visibleRegionIds)
    {
        var wanted = regions.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _overlays.Keys.Where(id => !wanted.Contains(id)).ToList())
        {
            Log.Info($"[{id}] Region removed.");
            _overlays[id].Dispose();
            _overlays.Remove(id);
        }

        foreach (var region in regions)
        {
            if (!_overlays.TryGetValue(region.Id, out var overlay))
            {
                overlay = _factory(region.Id);
                _overlays.Add(region.Id, overlay);
            }

            var visible = visibleRegionIds.Contains(region.Id);
            // 隠すときは先に隠してから描き直し、出すときは描き直してから出す。
            if (!visible)
            {
                overlay.SetVisible(false);
            }

            overlay.Apply(region, settings);

            if (visible)
            {
                overlay.SetVisible(true);
            }
        }
    }

    public void Tick(TrackingSnapshot? tracking)
    {
        foreach (var overlay in _overlays.Values)
        {
            overlay.Tick(tracking);
        }
    }

    public void Dispose()
    {
        foreach (var overlay in _overlays.Values)
        {
            overlay.Dispose();
        }

        _overlays.Clear();
    }
}
