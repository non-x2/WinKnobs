using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// AdvertisingIdHelper（v0.4.9b プライバシー「広告 ID を無効化」/ AdvertisingInfo\Enabled）の純粋関数テスト。
/// レジストリ I/O は VM 実機検証の領域でここでは扱わない。CoerceInt は委譲先（RegistryValueCoercionTests）でも検証。
/// </summary>
public class AdvertisingIdHelperTests
{
    [Fact]
    public void Choices_has_two_entries()
        => Assert.Equal(2, AdvertisingIdHelper.Choices.Count);

    [Fact]
    public void Labels_are_same_reference_as_choices()
    {
        Assert.Same(AdvertisingIdHelper.Choices[0], AdvertisingIdHelper.DisabledLabel);
        Assert.Same(AdvertisingIdHelper.Choices[1], AdvertisingIdHelper.EnabledLabel);
    }

    // ─── IsDisabled：厳密に 0 の時のみ true（境界値対応） ───

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(-1, false)]
    [InlineData(int.MaxValue, false)]
    public void IsDisabled_true_only_for_0(int value, bool expected)
        => Assert.Equal(expected, AdvertisingIdHelper.IsDisabled(value));

    [Fact]
    public void IsDisabled_null_is_false()   // 未設定＝既定に戻す（有効）
        => Assert.False(AdvertisingIdHelper.IsDisabled(null));

    // ─── ValueForLabel：ラベル→値 ───

    [Fact]
    public void ValueForLabel_disabled_returns_0()
        => Assert.Equal(AdvertisingIdHelper.ValueDisabled, AdvertisingIdHelper.ValueForLabel(AdvertisingIdHelper.DisabledLabel));

    [Fact]
    public void ValueForLabel_enabled_returns_1()
        => Assert.Equal(AdvertisingIdHelper.ValueEnabled, AdvertisingIdHelper.ValueForLabel(AdvertisingIdHelper.EnabledLabel));

    [Theory]
    [InlineData(null)]
    [InlineData("でたらめ")]
    [InlineData("")]
    public void ValueForLabel_unknown_or_null_returns_enabled(string? label)
        => Assert.Equal(AdvertisingIdHelper.ValueEnabled, AdvertisingIdHelper.ValueForLabel(label));

    // ─── LabelForValue：値→ラベル（必ず Choices 内・同一参照・境界値） ───

    [Fact]
    public void LabelForValue_0_returns_disabled_same_ref()
        => Assert.Same(AdvertisingIdHelper.Choices[0], AdvertisingIdHelper.LabelForValue(0));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]     // 0以外の将来値は誤って truthy 判定しない
    [InlineData(-5)]
    public void LabelForValue_non0_returns_enabled_same_ref(int value)
        => Assert.Same(AdvertisingIdHelper.Choices[1], AdvertisingIdHelper.LabelForValue(value));

    [Fact]
    public void LabelForValue_null_returns_enabled()   // 未設定＝既定に戻す（有効）
        => Assert.Same(AdvertisingIdHelper.Choices[1], AdvertisingIdHelper.LabelForValue(null));

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(99)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void LabelForValue_always_in_choices(int? value)
        => Assert.Contains(AdvertisingIdHelper.LabelForValue(value), AdvertisingIdHelper.Choices);

    // ─── ラウンドトリップ：ラベル→値→ラベル ───

    [Fact]
    public void RoundTrip_label_value_label()
    {
        foreach (var label in AdvertisingIdHelper.Choices)
        {
            var value = AdvertisingIdHelper.ValueForLabel(label);
            Assert.Same(label, AdvertisingIdHelper.LabelForValue(value));
        }
    }

    // ─── CoerceInt は委譲先と同一挙動（型違いケース含む） ───

    [Fact]
    public void CoerceInt_delegates_to_shared_coercion()
    {
        Assert.Equal(0, AdvertisingIdHelper.CoerceInt(0, -1));
        Assert.Equal(1, AdvertisingIdHelper.CoerceInt(1, -1));
        Assert.Equal(1, AdvertisingIdHelper.CoerceInt("1", -1));
        Assert.Equal(1, AdvertisingIdHelper.CoerceInt(null, 1));
        Assert.Equal(0, AdvertisingIdHelper.CoerceInt(new byte[] { 9 }, 0));   // 型違い（REG_BINARY 等）は fallback
    }
}
