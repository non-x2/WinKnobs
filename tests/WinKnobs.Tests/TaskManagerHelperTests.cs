using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// TaskManagerHelper（v0.1.14 システム・メンテナンス「タスクマネージャを使えるようにする」/ DisableTaskMgr）の純粋関数テスト。
/// レジストリ I/O は VM 実機検証の領域でここでは扱わない。CoerceInt は委譲先（RegistryValueCoercionTests）でも検証。
/// </summary>
public class TaskManagerHelperTests
{
    [Fact]
    public void Choices_has_two_entries()
        => Assert.Equal(2, TaskManagerHelper.Choices.Count);

    // ラベルプロパティは Choices の同一参照（リテラル直書きでないこと＝ComboBox 整合の保証）
    [Fact]
    public void Labels_are_same_reference_as_choices()
    {
        Assert.Same(TaskManagerHelper.Choices[0], TaskManagerHelper.EnabledLabel);
        Assert.Same(TaskManagerHelper.Choices[1], TaskManagerHelper.DisabledLabel);
    }

    // ─── IsDisabled：1=無効化 ───

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(-1, false)]
    public void IsDisabled_true_only_for_1(int value, bool expected)
        => Assert.Equal(expected, TaskManagerHelper.IsDisabled(value));

    [Fact]
    public void IsDisabled_null_is_false()   // 未設定＝使える（既定）
        => Assert.False(TaskManagerHelper.IsDisabled(null));

    // ─── ValueForLabel：ラベル→値 ───

    [Fact]
    public void ValueForLabel_disabled_returns_1()
        => Assert.Equal(TaskManagerHelper.ValueDisabled, TaskManagerHelper.ValueForLabel(TaskManagerHelper.DisabledLabel));

    [Fact]
    public void ValueForLabel_enabled_returns_0()
        => Assert.Equal(TaskManagerHelper.ValueEnabled, TaskManagerHelper.ValueForLabel(TaskManagerHelper.EnabledLabel));

    [Theory]
    [InlineData(null)]
    [InlineData("でたらめ")]
    [InlineData("")]
    public void ValueForLabel_unknown_or_null_returns_enabled(string? label)
        => Assert.Equal(TaskManagerHelper.ValueEnabled, TaskManagerHelper.ValueForLabel(label));

    // ─── LabelForValue：値→ラベル（必ず Choices 内・同一参照） ───

    [Fact]
    public void LabelForValue_1_returns_disabled_same_ref()
        => Assert.Same(TaskManagerHelper.Choices[1], TaskManagerHelper.LabelForValue(1));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-5)]
    public void LabelForValue_non1_returns_enabled_same_ref(int value)
        => Assert.Same(TaskManagerHelper.Choices[0], TaskManagerHelper.LabelForValue(value));

    [Fact]
    public void LabelForValue_null_returns_enabled()   // 未設定＝使える
        => Assert.Same(TaskManagerHelper.Choices[0], TaskManagerHelper.LabelForValue(null));

    // 戻り値は必ず Choices に含まれる（ComboBox 整合 invariant）
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(99)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void LabelForValue_always_in_choices(int? value)
        => Assert.Contains(TaskManagerHelper.LabelForValue(value), TaskManagerHelper.Choices);

    // ─── ラウンドトリップ：ラベル→値→ラベル ───

    [Fact]
    public void RoundTrip_label_value_label()
    {
        foreach (var label in TaskManagerHelper.Choices)
        {
            var value = TaskManagerHelper.ValueForLabel(label);
            Assert.Same(label, TaskManagerHelper.LabelForValue(value));
        }
    }

    // ─── CoerceInt は委譲先と同一挙動（REG_SZ 混在の前方互換ラッパ担保） ───

    [Fact]
    public void CoerceInt_delegates_to_shared_coercion()
    {
        Assert.Equal(1, TaskManagerHelper.CoerceInt(1, -1));        // int passthrough
        Assert.Equal(1, TaskManagerHelper.CoerceInt("1", -1));      // REG_SZ "1"
        Assert.Equal(0, TaskManagerHelper.CoerceInt(null, 0));      // 未設定→fallback
        Assert.Equal(0, TaskManagerHelper.CoerceInt(new byte[] { 9 }, 0)); // 異常型→fallback
    }
}
