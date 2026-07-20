using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// DisableBackgroundAppsHelper（v0.4.8「バックグラウンドアプリを一括無効化」/ GlobalUserDisabled）の純粋関数テスト。
/// レジストリ I/O は VM 実機検証の領域でここでは扱わない。CoerceInt は委譲先（RegistryValueCoercionTests）でも検証。
/// </summary>
public class DisableBackgroundAppsHelperTests
{
    [Fact]
    public void Choices_has_two_entries()
        => Assert.Equal(2, DisableBackgroundAppsHelper.Choices.Count);

    [Fact]
    public void Labels_are_same_reference_as_choices()
    {
        Assert.Same(DisableBackgroundAppsHelper.Choices[0], DisableBackgroundAppsHelper.DisabledLabel);
        Assert.Same(DisableBackgroundAppsHelper.Choices[1], DisableBackgroundAppsHelper.EnabledLabel);
    }

    // ─── IsDisabled：厳密に 1 の時のみ true（境界値対応） ───

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(-1, false)]
    [InlineData(int.MaxValue, false)]
    public void IsDisabled_true_only_for_1(int value, bool expected)
        => Assert.Equal(expected, DisableBackgroundAppsHelper.IsDisabled(value));

    [Fact]
    public void IsDisabled_null_is_false()   // 未設定＝有効にする（既定）
        => Assert.False(DisableBackgroundAppsHelper.IsDisabled(null));

    // ─── ValueForLabel：ラベル→値 ───

    [Fact]
    public void ValueForLabel_disabled_returns_1()
        => Assert.Equal(DisableBackgroundAppsHelper.ValueDisabled, DisableBackgroundAppsHelper.ValueForLabel(DisableBackgroundAppsHelper.DisabledLabel));

    [Fact]
    public void ValueForLabel_enabled_returns_0()
        => Assert.Equal(DisableBackgroundAppsHelper.ValueEnabled, DisableBackgroundAppsHelper.ValueForLabel(DisableBackgroundAppsHelper.EnabledLabel));

    [Theory]
    [InlineData(null)]
    [InlineData("でたらめ")]
    [InlineData("")]
    public void ValueForLabel_unknown_or_null_returns_enabled(string? label)
        => Assert.Equal(DisableBackgroundAppsHelper.ValueEnabled, DisableBackgroundAppsHelper.ValueForLabel(label));

    // ─── LabelForValue：値→ラベル（必ず Choices 内・同一参照・境界値） ───

    [Fact]
    public void LabelForValue_1_returns_disabled_same_ref()
        => Assert.Same(DisableBackgroundAppsHelper.Choices[0], DisableBackgroundAppsHelper.LabelForValue(1));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]     // 1以外の将来値は誤って truthy 判定しない
    [InlineData(-5)]
    public void LabelForValue_non1_returns_enabled_same_ref(int value)
        => Assert.Same(DisableBackgroundAppsHelper.Choices[1], DisableBackgroundAppsHelper.LabelForValue(value));

    [Fact]
    public void LabelForValue_null_returns_enabled()   // 未設定＝有効にする
        => Assert.Same(DisableBackgroundAppsHelper.Choices[1], DisableBackgroundAppsHelper.LabelForValue(null));

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(99)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void LabelForValue_always_in_choices(int? value)
        => Assert.Contains(DisableBackgroundAppsHelper.LabelForValue(value), DisableBackgroundAppsHelper.Choices);

    // ─── ラウンドトリップ：ラベル→値→ラベル ───

    [Fact]
    public void RoundTrip_label_value_label()
    {
        foreach (var label in DisableBackgroundAppsHelper.Choices)
        {
            var value = DisableBackgroundAppsHelper.ValueForLabel(label);
            Assert.Same(label, DisableBackgroundAppsHelper.LabelForValue(value));
        }
    }

    // ─── CoerceInt は委譲先と同一挙動 ───

    [Fact]
    public void CoerceInt_delegates_to_shared_coercion()
    {
        Assert.Equal(1, DisableBackgroundAppsHelper.CoerceInt(1, -1));
        Assert.Equal(1, DisableBackgroundAppsHelper.CoerceInt("1", -1));
        Assert.Equal(0, DisableBackgroundAppsHelper.CoerceInt(null, 0));
        Assert.Equal(0, DisableBackgroundAppsHelper.CoerceInt(new byte[] { 9 }, 0));
    }
}
