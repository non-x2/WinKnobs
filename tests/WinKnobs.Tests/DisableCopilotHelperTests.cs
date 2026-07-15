using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// DisableCopilotHelper（v0.4.7a プライバシー「Copilot を無効化」/ TurnOffWindowsCopilot）の純粋関数テスト。
/// レジストリ I/O は VM 実機検証の領域でここでは扱わない。CoerceInt は委譲先（RegistryValueCoercionTests）でも検証。
/// </summary>
public class DisableCopilotHelperTests
{
    [Fact]
    public void Choices_has_two_entries()
        => Assert.Equal(2, DisableCopilotHelper.Choices.Count);

    [Fact]
    public void Labels_are_same_reference_as_choices()
    {
        Assert.Same(DisableCopilotHelper.Choices[0], DisableCopilotHelper.DisabledLabel);
        Assert.Same(DisableCopilotHelper.Choices[1], DisableCopilotHelper.EnabledLabel);
    }

    // ─── IsDisabled：厳密に 1 の時のみ true（境界値対応） ───

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(-1, false)]
    [InlineData(int.MaxValue, false)]
    public void IsDisabled_true_only_for_1(int value, bool expected)
        => Assert.Equal(expected, DisableCopilotHelper.IsDisabled(value));

    [Fact]
    public void IsDisabled_null_is_false()   // 未設定＝有効にする（既定）
        => Assert.False(DisableCopilotHelper.IsDisabled(null));

    // ─── ValueForLabel：ラベル→値 ───

    [Fact]
    public void ValueForLabel_disabled_returns_1()
        => Assert.Equal(DisableCopilotHelper.ValueDisabled, DisableCopilotHelper.ValueForLabel(DisableCopilotHelper.DisabledLabel));

    [Fact]
    public void ValueForLabel_enabled_returns_0()
        => Assert.Equal(DisableCopilotHelper.ValueEnabled, DisableCopilotHelper.ValueForLabel(DisableCopilotHelper.EnabledLabel));

    [Theory]
    [InlineData(null)]
    [InlineData("でたらめ")]
    [InlineData("")]
    public void ValueForLabel_unknown_or_null_returns_enabled(string? label)
        => Assert.Equal(DisableCopilotHelper.ValueEnabled, DisableCopilotHelper.ValueForLabel(label));

    // ─── LabelForValue：値→ラベル（必ず Choices 内・同一参照・境界値） ───

    [Fact]
    public void LabelForValue_1_returns_disabled_same_ref()
        => Assert.Same(DisableCopilotHelper.Choices[0], DisableCopilotHelper.LabelForValue(1));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]     // 1以外の将来値は誤って truthy 判定しない
    [InlineData(-5)]
    public void LabelForValue_non1_returns_enabled_same_ref(int value)
        => Assert.Same(DisableCopilotHelper.Choices[1], DisableCopilotHelper.LabelForValue(value));

    [Fact]
    public void LabelForValue_null_returns_enabled()   // 未設定＝有効にする
        => Assert.Same(DisableCopilotHelper.Choices[1], DisableCopilotHelper.LabelForValue(null));

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(99)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void LabelForValue_always_in_choices(int? value)
        => Assert.Contains(DisableCopilotHelper.LabelForValue(value), DisableCopilotHelper.Choices);

    // ─── ラウンドトリップ：ラベル→値→ラベル ───

    [Fact]
    public void RoundTrip_label_value_label()
    {
        foreach (var label in DisableCopilotHelper.Choices)
        {
            var value = DisableCopilotHelper.ValueForLabel(label);
            Assert.Same(label, DisableCopilotHelper.LabelForValue(value));
        }
    }

    // ─── CoerceInt は委譲先と同一挙動 ───

    [Fact]
    public void CoerceInt_delegates_to_shared_coercion()
    {
        Assert.Equal(1, DisableCopilotHelper.CoerceInt(1, -1));
        Assert.Equal(1, DisableCopilotHelper.CoerceInt("1", -1));
        Assert.Equal(0, DisableCopilotHelper.CoerceInt(null, 0));
        Assert.Equal(0, DisableCopilotHelper.CoerceInt(new byte[] { 9 }, 0));
    }
}
