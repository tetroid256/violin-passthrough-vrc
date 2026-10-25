using ViolinPassthrough.Config;
using ViolinPassthrough.Logging;
using ViolinPassthrough.State;

namespace ViolinPassthrough.Osc;

/// <summary><see cref="OscBindings.Apply"/> の結果。</summary>
public enum OscApplyResult
{
    /// <summary>関係ないメッセージ、または状態が変わらなかった。</summary>
    None,

    /// <summary>状態を変えた。</summary>
    StateChanged,

    /// <summary>アバターが変わった（状態は変えない）。</summary>
    AvatarChanged,
}

/// <summary>VRChat 側が今持っているはずの値。送り返すかどうかの判定に使う。</summary>
public sealed record AvatarParameterValues(bool? Enabled, int? PresetIndex, bool? Debug)
{
    public static AvatarParameterValues Unknown { get; } = new(null, null, null);
}

/// <summary>
/// アバターパラメータ名 → アクションの対応付け。
/// </summary>
public sealed class OscBindings
{
    public const string AvatarChangeAddress = "/avatar/change";
    public const string ParameterPrefix = "/avatar/parameters/";

    public OscBindings(OscSettings settings)
    {
        EnabledAddress = ParameterPrefix + settings.EnabledParameter;
        PresetAddress = ParameterPrefix + settings.PresetParameter;
        DebugAddress = ParameterPrefix + settings.DebugParameter;
    }

    public string EnabledAddress { get; }

    public string PresetAddress { get; }

    public string DebugAddress { get; }

    /// <summary>受け取るアドレス。これ以外は受信時に捨てる。</summary>
    public IReadOnlySet<string> ReceiveAddresses =>
        new HashSet<string>(StringComparer.Ordinal) { EnabledAddress, PresetAddress, DebugAddress, AvatarChangeAddress };

    /// <summary>
    /// 受け取ったメッセージを状態に反映する。
    /// </summary>
    /// <param name="vrchat">VRChat 側の値。受け取った値で更新する。</param>
    public OscApplyResult Apply(
        OscMessage message,
        PassthroughState state,
        ResolvedConfig config,
        ref AvatarParameterValues vrchat)
    {
        if (message.Address == AvatarChangeAddress)
        {
            var avatarId = message.Arguments.Count > 0 ? message.Arguments[0] : null;
            Log.Info($"Avatar changed ({avatarId ?? "unknown"}).");
            return OscApplyResult.AvatarChanged;
        }

        if (message.Arguments.Count == 0)
        {
            return OscApplyResult.None;
        }

        var argument = message.Arguments[0];
        if (message.Address == EnabledAddress)
        {
            if (!TryGetBool(argument, out var enabled))
            {
                return Unsupported(message);
            }

            vrchat = vrchat with { Enabled = enabled };
            if (!state.SetEnabled(enabled))
            {
                return OscApplyResult.None;
            }

            Log.Info($"Passthrough {(enabled ? "enabled" : "disabled")} (OSC).");
            return OscApplyResult.StateChanged;
        }

        if (message.Address == PresetAddress)
        {
            if (!TryGetInt(argument, out var index))
            {
                return Unsupported(message);
            }

            vrchat = vrchat with { PresetIndex = index };
            if (index < 0 || index >= config.Presets.Count)
            {
                Log.Warn($"Preset index {index} from OSC is out of range (0-{config.Presets.Count - 1}); ignored.");
                return OscApplyResult.None;
            }

            var preset = config.Presets[index].Name;
            if (!state.SetActivePreset(preset))
            {
                return OscApplyResult.None;
            }

            Log.Info($"Preset changed to \"{preset}\" (OSC {index}).");
            return OscApplyResult.StateChanged;
        }

        if (message.Address == DebugAddress)
        {
            if (!TryGetBool(argument, out var debug))
            {
                return Unsupported(message);
            }

            vrchat = vrchat with { Debug = debug };
            if (!state.SetDebug(debug))
            {
                return OscApplyResult.None;
            }

            Log.Info($"Debug display {(debug ? "on" : "off")} (OSC).");
            return OscApplyResult.StateChanged;
        }

        return OscApplyResult.None;
    }

    /// <summary>
    /// 状態のうち、VRChat 側の値と違うものを送るメッセージにする。
    /// </summary>
    public List<OscMessage> BuildSyncMessages(PassthroughState state, ResolvedConfig config, AvatarParameterValues vrchat)
    {
        var messages = new List<OscMessage>();
        if (vrchat.Enabled != state.Enabled)
        {
            messages.Add(new OscMessage(EnabledAddress, state.Enabled));
        }

        var index = config.IndexOfPreset(state.ActivePreset);
        if (index >= 0 && vrchat.PresetIndex != index)
        {
            messages.Add(new OscMessage(PresetAddress, index));
        }

        if (vrchat.Debug != state.Debug)
        {
            messages.Add(new OscMessage(DebugAddress, state.Debug));
        }

        return messages;
    }

    /// <summary>送ったあとの VRChat 側の値。</summary>
    public static AvatarParameterValues ValuesOf(PassthroughState state, ResolvedConfig config)
    {
        var index = config.IndexOfPreset(state.ActivePreset);
        return new AvatarParameterValues(state.Enabled, index >= 0 ? index : null, state.Debug);
    }

    private static OscApplyResult Unsupported(OscMessage message)
    {
        Log.Warn($"Unsupported OSC argument for {message}; ignored.");
        return OscApplyResult.None;
    }

    // パラメータの型を間違えて作っても動くように、bool / int / float のどれでも受け取る。
    private static bool TryGetBool(object? value, out bool result)
    {
        switch (value)
        {
            case bool b:
                result = b;
                return true;
            case int i:
                result = i != 0;
                return true;
            case float f:
                result = f >= 0.5f;
                return true;
            default:
                result = false;
                return false;
        }
    }

    private static bool TryGetInt(object? value, out int result)
    {
        switch (value)
        {
            case int i:
                result = i;
                return true;
            case float f when float.IsFinite(f):
                result = (int)MathF.Round(f);
                return true;
            case bool b:
                result = b ? 1 : 0;
                return true;
            default:
                result = 0;
                return false;
        }
    }
}
