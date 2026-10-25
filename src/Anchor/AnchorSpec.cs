namespace ViolinPassthrough.Anchor;

/// <summary>
/// 設定から作るアンカーの定義（値オブジェクト）。比較して変更を検出できる。
/// 実行時の状態を持つ <see cref="IAnchor"/> は <see cref="CreateAnchor"/> で作る。
/// </summary>
public abstract record AnchorSpec
{
    public abstract IAnchor CreateAnchor();
}

public sealed record HmdAnchorSpec : AnchorSpec
{
    public override IAnchor CreateAnchor() => new HmdAnchor();
}

public sealed record WorldAnchorSpec(TrackingUniverse Universe) : AnchorSpec
{
    public override IAnchor CreateAnchor() => new WorldAnchor(Universe);
}

public sealed record DeviceAnchorSpec(DeviceRole? Role, string? Serial, LostBehavior OnLost) : AnchorSpec
{
    public override IAnchor CreateAnchor() => new DeviceAnchor(Role, Serial, OnLost);
}
