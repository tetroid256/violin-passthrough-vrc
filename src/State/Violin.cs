namespace ViolinPassthrough.State;

/// <summary>
/// 自動モードで「構えているか」を何で判定するか（DESIGN.md §17.1）。値は OSC の Int と同じ。
/// </summary>
public enum AutoMethod
{
    /// <summary>左手の位置だけ。</summary>
    LeftHand = 0,

    /// <summary>左手の位置と頭の傾きの両方。左手を見失っている間は頭の傾きだけ。</summary>
    LeftHandAndHead = 1,

    /// <summary>頭の傾きだけ（ハンドトラッキングを使わない）。</summary>
    Head = 2,
}

/// <summary>アバターメニューで選ぶ、バイオリンのモデルの扱い（DESIGN.md §17.2）。値は OSC の Int と同じ。</summary>
public enum ViolinMode
{
    /// <summary>しまう（モデルを出さない）。</summary>
    Away = 0,

    /// <summary>持つ。構えたかどうかは自動で判定する。</summary>
    Hold = 1,

    /// <summary>構える（判定を使わずに、構えた状態のままにする）。</summary>
    Play = 2,
}

/// <summary>アバターに送る、バイオリンのモデルの状態（DESIGN.md §17.2）。値は OSC の Int と同じ。</summary>
public enum ViolinState
{
    /// <summary>出さない。</summary>
    Away = 0,

    /// <summary>左手で持つ（本体は左手に追従）。</summary>
    Held = 1,

    /// <summary>構える（本体は頭か胸に追従。アバター側で左手をアニメーションに固定してもよい）。</summary>
    Playing = 2,
}

public static class AutoMethods
{
    /// <summary>設定ファイルの <c>auto.useLeftHand</c> / <c>auto.useHeadTilt</c> から。</summary>
    public static AutoMethod FromFlags(bool useLeftHand, bool useHeadTilt) => (useLeftHand, useHeadTilt) switch
    {
        (true, true) => AutoMethod.LeftHandAndHead,
        (false, true) => AutoMethod.Head,
        _ => AutoMethod.LeftHand,
    };

    public static bool UsesLeftHand(this AutoMethod method) => method != AutoMethod.Head;

    public static bool UsesHead(this AutoMethod method) => method != AutoMethod.LeftHand;

    /// <summary>通知表示の文字列。</summary>
    public static string Label(this AutoMethod method) => method switch
    {
        AutoMethod.LeftHandAndHead => "HAND+HEAD",
        AutoMethod.Head => "HEAD",
        _ => "HAND",
    };
}
