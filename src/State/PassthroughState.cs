namespace ViolinPassthrough.State;

/// <summary>
/// 現在の状態（有効/無効、プリセット、デバッグ表示、自動モード、バイオリンのモデル）。状態の持ち主はこのクラスだけ。
/// OSC と設定は、メインループのスレッドからこのクラスを更新する。
/// </summary>
public sealed class PassthroughState
{
    public PassthroughState(bool enabled, string activePreset, bool debug, bool auto = false, AutoMethod autoMethod = AutoMethod.LeftHand)
    {
        Enabled = enabled;
        ActivePreset = activePreset;
        Debug = debug;
        Auto = auto;
        AutoMethod = autoMethod;
    }

    public bool Enabled { get; private set; }

    public string ActivePreset { get; private set; }

    public bool Debug { get; private set; }

    /// <summary>自動モード（構えたときだけ表示する）。</summary>
    public bool Auto { get; private set; }

    /// <summary>自動モードで「構えているか」を何で判定するか。</summary>
    public AutoMethod AutoMethod { get; private set; }

    /// <summary>アバターメニューで選んだ、バイオリンのモデルの扱い。</summary>
    public ViolinMode ViolinMode { get; private set; }

    /// <summary>楽器を構えているか（判定の結果）。</summary>
    public bool InPosture { get; private set; }

    /// <summary>
    /// 構えているものとして扱うか。判定の結果か、メニューで「構える」を選んだとき
    /// （判定がうまくいかないときに、手動で構えた状態にできるように）。
    /// </summary>
    public bool IsPlaying => InPosture || ViolinMode == ViolinMode.Play;

    /// <summary>アバターに送る、バイオリンのモデルの状態。</summary>
    public ViolinState ViolinState => ViolinMode switch
    {
        ViolinMode.Hold => InPosture ? ViolinState.Playing : ViolinState.Held,
        ViolinMode.Play => ViolinState.Playing,
        _ => ViolinState.Away,
    };

    /// <summary>構えているかの判定が要るか（自動モードで有効なとき、またはバイオリンを持っているとき）。</summary>
    public bool NeedsPostureDetection => (Auto && Enabled) || ViolinMode == ViolinMode.Hold;

    /// <summary>変更のたびに増える。反映済みかどうかの判定に使う。</summary>
    public int Version { get; private set; }

    /// <summary>
    /// マスクを表示するか。有効でも、自動モードなら構えているとき（<see cref="IsPlaying"/>）だけ表示する。
    /// デバッグ表示中は、位置を確認できるように無効でも表示する。
    /// </summary>
    public bool IsVisible => (Enabled && (!Auto || IsPlaying)) || Debug;

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

    public bool SetAutoMethod(AutoMethod method)
    {
        if (AutoMethod == method)
        {
            return false;
        }

        AutoMethod = method;
        Version++;
        return true;
    }

    public bool SetViolinMode(ViolinMode mode)
    {
        if (ViolinMode == mode)
        {
            return false;
        }

        ViolinMode = mode;
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
        $"enabled={Enabled}, preset={ActivePreset}, debug={Debug}, auto={Auto}, method={AutoMethod}, violin={ViolinMode}, inPosture={InPosture}";
}
