using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// HideCopilotButtonHelper（v0.4.7b デスクトップ「タスクバーの Copilot ボタンを非表示」/ ShowCopilotButton）の純粋関数テスト。
/// レジストリ I/O は VM 実機検証の領域でここでは扱わない。CoerceInt は委譲先（RegistryValueCoercionTests）でも検証。
/// </summary>
public class HideCopilotButtonHelperTests
{
    [Fact]
    public void Choices_has_two_entries()
        => Assert.Equal(2, HideCopilotButtonHelper.Choices.Count);

    [Fact]
    public void Labels_are_same_reference_as_choices()
    {
        Assert.Same(HideCopilotButtonHelper.Choices[0], HideCopilotButtonHelper.ShownLabel);
        Assert.Same(HideCopilotButtonHelper.Choices[1], HideCopilotButtonHelper.HiddenLabel);
    }

    // ─── IsHidden：厳密に 0 の時のみ true ───

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(2, false)]
    [InlineData(-1, false)]
    [InlineData(int.MaxValue, false)]
    public void IsHidden_true_only_for_0(int value, bool expected)
        => Assert.Equal(expected, HideCopilotButtonHelper.IsHidden(value));

    [Fact]
    public void IsHidden_null_is_false()   // 未設定＝表示する（既定）
        => Assert.False(HideCopilotButtonHelper.IsHidden(null));

    // ─── ValueForLabel：ラベル→値 ───

    [Fact]
    public void ValueForLabel_hidden_returns_0()
        => Assert.Equal(HideCopilotButtonHelper.ValueHidden, HideCopilotButtonHelper.ValueForLabel(HideCopilotButtonHelper.HiddenLabel));

    [Fact]
    public void ValueForLabel_shown_returns_1()
        => Assert.Equal(HideCopilotButtonHelper.ValueShown, HideCopilotButtonHelper.ValueForLabel(HideCopilotButtonHelper.ShownLabel));

    [Theory]
    [InlineData(null)]
    [InlineData("でたらめ")]
    [InlineData("")]
    public void ValueForLabel_unknown_or_null_returns_shown(string? label)
        => Assert.Equal(HideCopilotButtonHelper.ValueShown, HideCopilotButtonHelper.ValueForLabel(label));

    // ─── LabelForValue：値→ラベル（必ず Choices 内・同一参照・境界値） ───

    [Fact]
    public void LabelForValue_0_returns_hidden_same_ref()
        => Assert.Same(HideCopilotButtonHelper.Choices[1], HideCopilotButtonHelper.LabelForValue(0));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-5)]
    public void LabelForValue_non0_returns_shown_same_ref(int value)
        => Assert.Same(HideCopilotButtonHelper.Choices[0], HideCopilotButtonHelper.LabelForValue(value));

    [Fact]
    public void LabelForValue_null_returns_shown()   // 未設定＝表示する
        => Assert.Same(HideCopilotButtonHelper.Choices[0], HideCopilotButtonHelper.LabelForValue(null));

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(99)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void LabelForValue_always_in_choices(int? value)
        => Assert.Contains(HideCopilotButtonHelper.LabelForValue(value), HideCopilotButtonHelper.Choices);

    // ─── ラウンドトリップ：ラベル→値→ラベル ───

    [Fact]
    public void RoundTrip_label_value_label()
    {
        foreach (var label in HideCopilotButtonHelper.Choices)
        {
            var value = HideCopilotButtonHelper.ValueForLabel(label);
            Assert.Same(label, HideCopilotButtonHelper.LabelForValue(value));
        }
    }

    // ─── CoerceInt は委譲先と同一挙動 ───

    [Fact]
    public void CoerceInt_delegates_to_shared_coercion()
    {
        Assert.Equal(0, HideCopilotButtonHelper.CoerceInt(0, -1));
        Assert.Equal(0, HideCopilotButtonHelper.CoerceInt("0", -1));
        Assert.Equal(1, HideCopilotButtonHelper.CoerceInt(null, 1));
        Assert.Equal(1, HideCopilotButtonHelper.CoerceInt(new byte[] { 9 }, 1));
    }
}
