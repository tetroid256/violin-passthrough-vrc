namespace ViolinPassthrough.Anchor;

/// <summary>
/// 自動モードとキャリブレーションで「手」として使うデバイス（DESIGN.md §16.2）。
/// シリアル番号を指定すると、手（VD がエミュレートするコントローラー）の代わりにそのデバイス
/// （手首に付けたトラッカーなど）を使う。指定しなければ、左手・右手のロールのデバイスを使う。
/// </summary>
/// <param name="LeftHandSerial">左手の代わりに使うデバイスのシリアル番号。null ならロールで選ぶ。</param>
/// <param name="RightHandSerial">右手の代わりに使うデバイスのシリアル番号。null ならロールで選ぶ。</param>
public sealed record HandDevices(string? LeftHandSerial, string? RightHandSerial)
{
    public static HandDevices Default { get; } = new(null, null);

    /// <summary>左手として使うデバイス。見つからなければ <see cref="TrackingSnapshot.InvalidDeviceIndex"/>。</summary>
    public uint LeftIndex(TrackingSnapshot tracking) => Resolve(tracking, LeftHandSerial, tracking.LeftHandIndex);

    /// <summary>右手として使うデバイス。見つからなければ <see cref="TrackingSnapshot.InvalidDeviceIndex"/>。</summary>
    public uint RightIndex(TrackingSnapshot tracking) => Resolve(tracking, RightHandSerial, tracking.RightHandIndex);

    /// <summary>
    /// アンカーのデバイス（<see cref="DeviceAnchor"/> と同じ選び方）。見つからなければ
    /// <see cref="TrackingSnapshot.InvalidDeviceIndex"/>。
    /// </summary>
    public static uint AnchorIndex(TrackingSnapshot tracking, DeviceAnchorSpec anchor) =>
        anchor.Serial is not null
            ? Resolve(tracking, anchor.Serial, TrackingSnapshot.InvalidDeviceIndex)
            : anchor.Role == DeviceRole.LeftHand ? tracking.LeftHandIndex : tracking.RightHandIndex;

    // シリアル番号を指定したら、見つからなくてもロールのデバイスには戻らない
    // （手首のトラッカーと手では位置がずれるので、混ぜると判定やキャリブレーションが狂う）。
    private static uint Resolve(TrackingSnapshot tracking, string? serial, uint roleIndex) =>
        serial is null
            ? roleIndex
            : tracking.SerialToIndex.TryGetValue(serial, out var index) ? index : TrackingSnapshot.InvalidDeviceIndex;
}
