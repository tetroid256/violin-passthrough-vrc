namespace ViolinPassthrough.State;

/// <summary>
/// 現在の状態（有効/無効、プリセット、デバッグ表示）。状態の持ち主はこのクラスだけ。
/// OSC と設定は、メインループのスレッドからこのクラスを更新する。
/// </summary>
public sealed class PassthroughState
{
    public PassthroughState(bool enabled, string activePreset, bool debug, bool auto = false)
    {
        Enabled = enabled;
        ActivePreset = activePreset;
        Debug = debug;
        Auto = auto;
    }

    public bool Enabled { get; private set; }

    public string ActivePreset { get; private set; }

    public bool Debug { get; private set; }

    /// <summary>自動モード（構えたときだけ表示する）。</summary>
    public bool Auto { get; private set; }

    /// <summary>楽器を構えているか（自動モードの判定結果）。</summary>
    public bool InPosture { get; private set; }

    /// <summary>変更のたびに増える。反映済みかどうかの判定に使う。</summary>
    public int Version { get; private set; }

    /// <summary>
    /// マスクを表示するか。有効でも、自動モードなら構えているときだけ表示する。
    /// デバッグ表示中は、位置を確認できるように無効でも表示する。
    /// </summary>
    public bool IsVisible => (Enabled && (!Auto || InPosture)) || Debug;

    public bool SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
        {
            return false;
        }

        Enabled = enabled;
        Version++;
        return true;
    }

    public bool SetActivePreset(string preset)
    {
        if (ActivePreset == preset)
        {
            return false;
        }

        ActivePreset = preset;
        Version++;
        return true;
    }

    public bool SetDebug(bool debug)
    {
        if (Debug == debug)
        {
            return false;
        }

        Debug = debug;
        Version++;
        return true;
    }

    public bool SetAuto(bool auto)
    {
        if (Auto == auto)
        {
            return false;
        }

        Auto = auto;
        Version++;
        return true;
    }

    public bool SetInPosture(bool inPosture)
    {
        if (InPosture == inPosture)
        {
            return false;
        }

        InPosture = inPosture;
        Version++;
        return true;
    }

    public override string ToString() =>
        $"enabled={Enabled}, preset={ActivePreset}, debug={Debug}, auto={Auto}, inPosture={InPosture}";
}
