using System.Runtime.InteropServices;
using Valve.VR;
using ViolinPassthrough.Anchor;
using ViolinPassthrough.Logging;

namespace ViolinPassthrough.Mask;

/// <summary>1つのマスク領域を表示するオーバーレイ。テストでは偽物に差し替える。</summary>
public interface IMaskOverlay : IDisposable
{
    string RegionId { get; }

    /// <summary>true なら毎フレーム <see cref="Tick"/> にトラッキング情報を渡す必要がある。</summary>
    bool RequiresTracking { get; }

    /// <summary>領域と描画設定を反映する。前回と同じなら何もしない。</summary>
    void Apply(MaskRegion region, RenderSettings settings);

    /// <summary>表示したいかどうか。実際の表示は置き場所が決まっているかどうかにもよる。</summary>
    void SetVisible(bool visible);

    /// <summary>トラッキング依存のアンカーの置き場所を更新する。</summary>
    void Tick(TrackingSnapshot? tracking);
}

/// <summary>
/// 1領域 = 1 OpenVR Overlay のラッパー。OpenVR の Overlay API を呼ぶのはこのクラスだけ。
/// </summary>
public sealed class MaskOverlay : IMaskOverlay
{
    /// <summary>置き場所の設定に失敗したときに、やり直すまでの間隔。</summary>
    private const long PlacementRetryMilliseconds = 1000;

    private ulong _handle = OpenVR.k_ulOverlayHandleInvalid;
    private MaskRegion? _region;
    private (MaskRegion Region, RenderSettings Settings)? _applied;
    private AnchorSpec? _anchorSpec;
    private IAnchor? _anchor;
    private AnchorPlacement? _appliedPlacement;
    private long? _placementFailedAt;
    private bool _textureReady;
    private bool _wantVisible;
    private bool _shown;
    private bool _disposed;

    public MaskOverlay(string regionId)
    {
        RegionId = regionId;
    }

    public string RegionId { get; }

    public bool RequiresTracking => _anchor?.RequiresTracking ?? false;

    public void Apply(MaskRegion region, RenderSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (region.Id != RegionId)
        {
            throw new ArgumentException($"Region id mismatch: {region.Id} != {RegionId}", nameof(region));
        }

        if (_applied == (region, settings) && _handle != OpenVR.k_ulOverlayHandleInvalid)
        {
            return;
        }

        if (!EnsureCreated(region))
        {
            return;
        }

        _region = region;
        if (_anchor is null || !Equals(region.Anchor, _anchorSpec))
        {
            _anchor = region.Anchor.CreateAnchor();
            _anchorSpec = region.Anchor;
        }

        var ok = UploadTexture(region, settings);

        var overlay = OpenVR.Overlay;
        ok &= Check(overlay.SetOverlayWidthInMeters(_handle, (float)region.QuadWidthMeters), "SetOverlayWidthInMeters");
        // 色の乗算でキー色が変わらないように、α と色は常に 1。
        ok &= Check(overlay.SetOverlayAlpha(_handle, 1.0f), "SetOverlayAlpha");
        ok &= Check(overlay.SetOverlayColor(_handle, 1.0f, 1.0f, 1.0f), "SetOverlayColor");
        ok &= Check(overlay.SetOverlaySortOrder(_handle, settings.SortOrder), "SetOverlaySortOrder");

        // 失敗したら、次に同じ値で呼ばれたときにやり直す。
        _applied = ok ? (region, settings) : null;

        // 姿勢やアンカーが変わったかもしれないので、置き場所を必ず設定し直す。
        _appliedPlacement = null;
        _placementFailedAt = null;
        if (!_anchor.RequiresTracking)
        {
            ApplyPlacement(_anchor.Resolve(region.LocalPose, null));
        }

        UpdateVisibility();
    }

    public void SetVisible(bool visible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _wantVisible = visible;
        UpdateVisibility();
    }

    public void Tick(TrackingSnapshot? tracking)
    {
        if (_disposed || _region is null || _anchor is null || !_anchor.RequiresTracking)
        {
            return;
        }

        ApplyPlacement(_anchor.Resolve(_region.LocalPose, tracking));
        UpdateVisibility();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_handle != OpenVR.k_ulOverlayHandleInvalid && OpenVR.Overlay is { } overlay)
        {
            Check(overlay.DestroyOverlay(_handle), "DestroyOverlay");
        }

        _handle = OpenVR.k_ulOverlayHandleInvalid;
    }

    private bool EnsureCreated(MaskRegion region)
    {
        if (_handle != OpenVR.k_ulOverlayHandleInvalid)
        {
            return true;
        }

        var overlay = OpenVR.Overlay;
        if (overlay is null)
        {
            Log.Error($"[{RegionId}] OpenVR overlay interface is not available.");
            return false;
        }

        ulong handle = OpenVR.k_ulOverlayHandleInvalid;
        var name = $"Violin Passthrough ({region.Id})";
        if (!Check(overlay.CreateOverlay(region.OverlayKey, name, ref handle), "CreateOverlay"))
        {
            return false;
        }

        _handle = handle;
        _shown = false;
        _textureReady = false;
        Check(overlay.HideOverlay(_handle), "HideOverlay");
        Log.Info($"[{RegionId}] Overlay created (key={region.OverlayKey}).");
        return true;
    }

    private bool UploadTexture(MaskRegion region, RenderSettings settings)
    {
        var texture = MaskRasterizer.Rasterize(region, settings);
        var overlay = OpenVR.Overlay;
        var pin = GCHandle.Alloc(texture.Rgba, GCHandleType.Pinned);
        try
        {
            _textureReady = Check(
                overlay.SetOverlayRaw(_handle, pin.AddrOfPinnedObject(), (uint)texture.Width, (uint)texture.Height, 4),
                "SetOverlayRaw");
        }
        finally
        {
            pin.Free();
        }

        var aspectOk = Check(overlay.SetOverlayTexelAspect(_handle, (float)TexelAspect(region, texture)), "SetOverlayTexelAspect");

        Log.Debug($"[{RegionId}] Texture {texture.Width}x{texture.Height} uploaded (debug={settings.Debug}).");
        return _textureReady && aspectOk;
    }

    /// <summary>テクセルの縦横比（幅 / 高さ）。画素数の丸めで板の縦横比がずれないように補正する。</summary>
    internal static double TexelAspect(MaskRegion region, MaskTexture texture) =>
        (region.QuadWidthMeters / texture.Width) / (region.QuadHeightMeters / texture.Height);

    private void ApplyPlacement(AnchorPlacement placement)
    {
        if (Equals(placement, _appliedPlacement))
        {
            return;
        }

        // 失敗が続くときに毎フレームやり直してログが溢れないようにする。
        var now = Environment.TickCount64;
        if (_placementFailedAt is { } failedAt && now - failedAt < PlacementRetryMilliseconds)
        {
            return;
        }

        var overlay = OpenVR.Overlay;
        bool ok;
        switch (placement)
        {
            case AnchorPlacement.DeviceRelative relative:
            {
                var m = ToHmdMatrix(relative.Pose);
                ok = Check(overlay.SetOverlayTransformTrackedDeviceRelative(_handle, relative.DeviceIndex, ref m),
                    "SetOverlayTransformTrackedDeviceRelative");
                Log.Debug($"[{RegionId}] Placed relative to device {relative.DeviceIndex}.");
                break;
            }

            case AnchorPlacement.Absolute absolute:
            {
                var m = ToHmdMatrix(absolute.Pose);
                ok = Check(overlay.SetOverlayTransformAbsolute(_handle, ToOrigin(absolute.Universe), ref m),
                    "SetOverlayTransformAbsolute");
                Log.Debug($"[{RegionId}] Placed absolute ({absolute.Universe}).");
                break;
            }

            default:
                ok = true;
                Log.Debug($"[{RegionId}] No placement available; hiding.");
                break;
        }

        // 失敗したら置き場所は未設定のまま（既定の位置に表示されないように隠したままにする）。
        _appliedPlacement = ok ? placement : null;
        _placementFailedAt = ok ? null : now;
    }

    private void UpdateVisibility()
    {
        if (_handle == OpenVR.k_ulOverlayHandleInvalid)
        {
            return;
        }

        var show = _wantVisible
            && _textureReady
            && _appliedPlacement is not null and not AnchorPlacement.Hidden;
        if (show == _shown)
        {
            return;
        }

        var overlay = OpenVR.Overlay;
        var ok = show
            ? Check(overlay.ShowOverlay(_handle), "ShowOverlay")
            : Check(overlay.HideOverlay(_handle), "HideOverlay");
        if (ok)
        {
            _shown = show;
            Log.Info($"[{RegionId}] {(show ? "Shown" : "Hidden")}.");
        }
    }

    private bool Check(EVROverlayError error, string operation)
    {
        if (error == EVROverlayError.None)
        {
            return true;
        }

        var name = OpenVR.Overlay?.GetOverlayErrorNameFromEnum(error) ?? error.ToString();
        Log.Error($"[{RegionId}] {operation} failed: {name} ({(int)error})");
        return false;
    }

    internal static HmdMatrix34_t ToHmdMatrix(Pose3x4 p) => new()
    {
        m0 = (float)p.M00, m1 = (float)p.M01, m2 = (float)p.M02, m3 = (float)p.M03,
        m4 = (float)p.M10, m5 = (float)p.M11, m6 = (float)p.M12, m7 = (float)p.M13,
        m8 = (float)p.M20, m9 = (float)p.M21, m10 = (float)p.M22, m11 = (float)p.M23,
    };

    private static ETrackingUniverseOrigin ToOrigin(TrackingUniverse universe) => universe switch
    {
        TrackingUniverse.Seated => ETrackingUniverseOrigin.TrackingUniverseSeated,
        _ => ETrackingUniverseOrigin.TrackingUniverseStanding,
    };
}
