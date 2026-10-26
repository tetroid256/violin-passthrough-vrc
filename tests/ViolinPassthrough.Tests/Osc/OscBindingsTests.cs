using ViolinPassthrough.Osc;
using ViolinPassthrough.State;

namespace ViolinPassthrough.Tests.Osc;

public class OscBindingsTests
{
    private const string Enabled = "/avatar/parameters/HandPassthrough";
    private const string Preset = "/avatar/parameters/HandPassthroughPreset";
    private const string Debug = "/avatar/parameters/HandPassthroughDebug";
    private const string Auto = "/avatar/parameters/HandPassthroughAuto";
    private const string Calibrate = "/avatar/parameters/HandPassthroughCalibrate";
    private const string AutoMethod = "/avatar/parameters/HandPassthroughAutoMethod";
    private const string Violin = "/avatar/parameters/HandPassthroughViolin";
    private const string ViolinState = "/avatar/parameters/HandPassthroughViolinState";

    private readonly OscBindings _bindings = new(TestConfigs.Default.Osc);
    private readonly PassthroughState _state = new(false, "fingerboard", false);
    private AvatarParameterValues _vrchat = AvatarParameterValues.Unknown;

    private OscApplyResult Apply(string address, params object?[] args) =>
        _bindings.Apply(new OscMessage(address, args), _state, TestConfigs.Default, ref _vrchat);

    [Fact]
    public void Addresses_UseConfiguredParameterNames()
    {
        Assert.Equal(Enabled, _bindings.EnabledAddress);
        Assert.Equal(Preset, _bindings.PresetAddress);
        Assert.Equal(Debug, _bindings.DebugAddress);
        Assert.Equal(
            new[] { "/avatar/change", Enabled, Auto, AutoMethod, Calibrate, Debug, Preset, Violin },
            _bindings.ReceiveAddresses.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EnabledToggle_ChangesState()
    {
        Assert.Equal(OscApplyResult.StateChanged, Apply(Enabled, true));
        Assert.True(_state.Enabled);
        Assert.Equal(OscApplyResult.None, Apply(Enabled, true));
        Assert.Equal(OscApplyResult.StateChanged, Apply(Enabled, false));
        Assert.False(_state.Enabled);
        Assert.False(_vrchat.Enabled);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(0.9f, true)]
    [InlineData(0.1f, false)]
    public void Enabled_AcceptsIntAndFloat(object value, bool expected)
    {
        _state.SetEnabled(!expected);

        Apply(Enabled, value);

        Assert.Equal(expected, _state.Enabled);
    }

    [Fact]
    public void Preset_SelectsByIndexInConfigOrder()
    {
        Assert.Equal(OscApplyResult.StateChanged, Apply(Preset, 1));
        Assert.Equal("whole", _state.ActivePreset);
        Assert.Equal(OscApplyResult.StateChanged, Apply(Preset, 0));
        Assert.Equal("fingerboard", _state.ActivePreset);
        Assert.Equal(0, _vrchat.PresetIndex);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public void Preset_OutOfRangeIsIgnoredButRemembered(int index)
    {
        Assert.Equal(OscApplyResult.None, Apply(Preset, index));

        Assert.Equal("fingerboard", _state.ActivePreset);
        // VRChat 側は範囲外の値を持っているので、送り返して直す必要がある。
        Assert.Equal(index, _vrchat.PresetIndex);
        Assert.Single(_bindings.BuildSyncMessages(_state, TestConfigs.Default, _vrchat), m => m.Address == Preset);
    }

    [Theory]
    [InlineData(0.8f, "whole")]
    [InlineData(0.5f, "whole")]
    [InlineData(0.49f, "fingerboard")]
    public void Preset_AcceptsFloat_RoundingHalfUp(float value, string expected)
    {
        _state.SetActivePreset(expected == "whole" ? "fingerboard" : "whole");

        Apply(Preset, value);

        Assert.Equal(expected, _state.ActivePreset);
    }

    [Fact]
    public void Debug_ChangesState()
    {
        Assert.Equal(OscApplyResult.StateChanged, Apply(Debug, true));
        Assert.True(_state.Debug);
    }

    [Fact]
    public void AvatarChange_DoesNotChangeState()
    {
        _state.SetEnabled(true);

        Assert.Equal(OscApplyResult.AvatarChanged, Apply("/avatar/change", "avtr_xxx"));

        Assert.True(_state.Enabled);
    }

    [Theory]
    [InlineData("/avatar/parameters/Other")]
    [InlineData("/avatar/parameters/HandPassthroughX")]
    public void UnrelatedParameters_AreIgnored(string address)
    {
        Assert.Equal(OscApplyResult.None, Apply(address, true));
        Assert.Equal(AvatarParameterValues.Unknown, _vrchat);
    }

    [Fact]
    public void WrongArgumentTypes_AreIgnored()
    {
        Assert.Equal(OscApplyResult.None, Apply(Enabled, "yes"));
        Assert.Equal(OscApplyResult.None, Apply(Enabled));
        Assert.Equal(OscApplyResult.None, Apply(Preset, float.NaN));
        Assert.False(_state.Enabled);
    }

    [Fact]
    public void BuildSyncMessages_SendsOnlyValuesVrchatDoesNotHave()
    {
        _state.SetEnabled(true);
        _state.SetActivePreset("whole");

        var all = _bindings.BuildSyncMessages(_state, TestConfigs.Default, AvatarParameterValues.Unknown);
        Assert.Equal(
            new[]
            {
                $"{Enabled} True", $"{Preset} 1", $"{Debug} False", $"{Auto} False", $"{Calibrate} False",
                $"{AutoMethod} 0", $"{Violin} 0", $"{ViolinState} 0",
            },
            all.Select(m => m.ToString()));

        var known = OscBindings.ValuesOf(_state, TestConfigs.Default);
        Assert.Empty(_bindings.BuildSyncMessages(_state, TestConfigs.Default, known));

        _state.SetDebug(true);
        Assert.Equal(new[] { $"{Debug} True" }, _bindings.BuildSyncMessages(_state, TestConfigs.Default, known).Select(m => m.ToString()));
    }

    [Fact]
    public void SentMessages_UseVrchatTypes()
    {
        var messages = _bindings.BuildSyncMessages(_state, TestConfigs.Default, AvatarParameterValues.Unknown);

        Assert.IsType<bool>(messages.Single(m => m.Address == Enabled).Arguments[0]);
        Assert.IsType<int>(messages.Single(m => m.Address == Preset).Arguments[0]);
        Assert.IsType<bool>(messages.Single(m => m.Address == Debug).Arguments[0]);
    }

    [Theory]
    [InlineData(0, ViolinPassthrough.State.AutoMethod.LeftHand)]
    [InlineData(1, ViolinPassthrough.State.AutoMethod.LeftHandAndHead)]
    [InlineData(2, ViolinPassthrough.State.AutoMethod.Head)]
    public void AutoMethod_IsSetFromTheMenu(int value, ViolinPassthrough.State.AutoMethod expected)
    {
        var result = Apply(AutoMethod, value);

        Assert.Equal(expected == ViolinPassthrough.State.AutoMethod.LeftHand ? OscApplyResult.None : OscApplyResult.StateChanged, result);
        Assert.Equal(expected, _state.AutoMethod);
        Assert.Equal(value, _vrchat.AutoMethod);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void OutOfRangeValues_AreIgnored_AndTheMenuIsCorrected(int value)
    {
        Assert.Equal(OscApplyResult.None, Apply(AutoMethod, value));
        Assert.Equal(OscApplyResult.None, Apply(Violin, value));

        Assert.Equal(ViolinPassthrough.State.AutoMethod.LeftHand, _state.AutoMethod);
        Assert.Equal(ViolinMode.Away, _state.ViolinMode);
        var messages = _bindings.BuildSyncMessages(_state, TestConfigs.Default, _vrchat).Select(m => m.ToString()).ToList();
        Assert.Contains($"{AutoMethod} 0", messages);
        Assert.Contains($"{Violin} 0", messages);
    }

    [Fact]
    public void ViolinMode_IsSetFromTheMenu_AndTheStateIsSent()
    {
        var known = OscBindings.ValuesOf(_state, TestConfigs.Default);

        Assert.Equal(OscApplyResult.StateChanged, Apply(Violin, 1));
        Assert.Equal(ViolinMode.Hold, _state.ViolinMode);

        // メニューの値（Violin）は VRChat が持っているので、状態（ViolinState）だけ送る。
        var known2 = known with { Violin = 1 };
        Assert.Equal(new[] { $"{ViolinState} 1" }, _bindings.BuildSyncMessages(_state, TestConfigs.Default, known2).Select(m => m.ToString()));

        _state.SetInPosture(true);
        Assert.Equal(new[] { $"{ViolinState} 2" }, _bindings.BuildSyncMessages(_state, TestConfigs.Default, known2).Select(m => m.ToString()));
    }

    [Fact]
    public void ViolinState_FromVrchat_IsNotAccepted()
    {
        // アニメーターが読むだけの出力。VRChat から来た値（自分が送った値のエコーなど）で状態を変えない。
        Assert.DoesNotContain(ViolinState, _bindings.ReceiveAddresses);
        Assert.Equal(OscApplyResult.None, Apply(ViolinState, 2));
    }

    [Fact]
    public void NewParameters_AreSentAsInt()
    {
        var messages = _bindings.BuildSyncMessages(_state, TestConfigs.Default, AvatarParameterValues.Unknown);

        Assert.IsType<int>(messages.Single(m => m.Address == AutoMethod).Arguments[0]);
        Assert.IsType<int>(messages.Single(m => m.Address == Violin).Arguments[0]);
        Assert.IsType<int>(messages.Single(m => m.Address == ViolinState).Arguments[0]);
    }
}
