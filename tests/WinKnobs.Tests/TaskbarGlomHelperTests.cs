using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// TaskbarGlomHelper（v0.3.15 タスクバー ボタンの結合・TaskbarGlomLevel）の純粋関数テスト。
/// レジストリ I/O は VM/実機検証の領域でここでは扱わない（LaunchToHelperTests と同方針）。
/// 検証点＝「値＝index の identity 変換」と「管理外（未設定/範囲外/非数値）の判定」。
/// identity だからこそ抜けやすい非自明ケース（CoerceInt 経由・TryGetManagedIndex(null)・範囲外）を明示する。
/// </summary>
public class TaskbarGlomHelperTests
{
    [Fact]
    public void Choices_has_three_entries()
        => Assert.Equal(3, TaskbarGlomHelper.Choices.Count);

    [Fact]
    public void Registry_values_are_canonical()
    {
        Assert.Equal(0, TaskbarGlomHelper.ValueAlways);
        Assert.Equal(1, TaskbarGlomHelper.ValueWhenFull);
        Assert.Equal(2, TaskbarGlomHelper.ValueNever);
    }

    // ─── IndexForValue：0/1/2 は identity・3 等は -1（管理外） ───
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, -1)]
    [InlineData(-1, -1)]
    [InlineData(99, -1)]
    public void IndexForValue_is_identity_within_range(int value, int expectedIndex)
        => Assert.Equal(expectedIndex, TaskbarGlomHelper.IndexForValue(value));

    // ─── ValueForIndex：0/1/2 は identity・範囲外は -1 ───
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, -1)]
    [InlineData(-1, -1)]
    public void ValueForIndex_is_identity_within_range(int index, int expectedValue)
        => Assert.Equal(expectedValue, TaskbarGlomHelper.ValueForIndex(index));

    // ─── ValueForLabel：ラベル→値（0/1/2）／不正・センチネル・null は -1 ───
    [Fact]
    public void ValueForLabel_maps_each_choice_to_registry_value()
    {
        Assert.Equal(0, TaskbarGlomHelper.ValueForLabel(TaskbarGlomHelper.Choices[0]));
        Assert.Equal(1, TaskbarGlomHelper.ValueForLabel(TaskbarGlomHelper.Choices[1]));
        Assert.Equal(2, TaskbarGlomHelper.ValueForLabel(TaskbarGlomHelper.Choices[2]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("でたらめ")]
    [InlineData("（変更しない・現在のまま）")]
    public void ValueForLabel_unknown_returns_minus_one(string? label)
        => Assert.Equal(-1, TaskbarGlomHelper.ValueForLabel(label));

    // ─── ラウンドトリップ：値→index→値（identity 保持） ───
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Value_index_value_roundtrip(int value)
    {
        int idx = TaskbarGlomHelper.IndexForValue(value);
        Assert.InRange(idx, 0, 2);
        Assert.Equal(value, TaskbarGlomHelper.ValueForIndex(idx));
    }

    // ─── label→value→index→label（同一参照） ───
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Label_value_index_label_roundtrip(int index)
    {
        var label = TaskbarGlomHelper.Choices[index];
        int value = TaskbarGlomHelper.ValueForLabel(label);
        int idx = TaskbarGlomHelper.IndexForValue(value);
        Assert.Same(label, TaskbarGlomHelper.Choices[idx]);
    }

    // ─── CoerceInt（REG_SZ "1" 混在対応） ───
    [Fact]
    public void CoerceInt_parses_string_and_int()
    {
        Assert.Equal(1, TaskbarGlomHelper.CoerceInt("1", -1));
        Assert.Equal(2, TaskbarGlomHelper.CoerceInt(2, -1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void CoerceInt_invalid_returns_fallback(object? v)
        => Assert.Equal(-1, TaskbarGlomHelper.CoerceInt(v, -1));

    // ─── TryGetManagedIndex：0/1/2 は管理対象・REG_SZ も解釈 ───
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public void TryGetManagedIndex_in_range_is_managed(int value, int expectedIndex)
    {
        Assert.True(TaskbarGlomHelper.TryGetManagedIndex(value, out var idx));
        Assert.Equal(expectedIndex, idx);
    }

    [Fact]
    public void TryGetManagedIndex_reg_sz_string_is_managed()
    {
        Assert.True(TaskbarGlomHelper.TryGetManagedIndex("2", out var idx));
        Assert.Equal(2, idx);
    }

    // ─── 未設定（null）は管理外＝Read fallback で choices[0] に丸める前提 ───
    [Fact]
    public void TryGetManagedIndex_null_is_unmanaged()
        => Assert.False(TaskbarGlomHelper.TryGetManagedIndex(null, out _));

    [Theory]
    [InlineData(3)]     // 範囲外
    [InlineData(-1)]
    [InlineData(99)]
    public void TryGetManagedIndex_out_of_range_is_unmanaged(int value)
        => Assert.False(TaskbarGlomHelper.TryGetManagedIndex(value, out _));

    [Theory]
    [InlineData("abc")]
    [InlineData("00 00 00 00")]
    public void TryGetManagedIndex_non_numeric_is_unmanaged(object raw)
        => Assert.False(TaskbarGlomHelper.TryGetManagedIndex(raw, out _));

    [Fact]
    public void TryGetManagedIndex_unmanaged_yields_zero_index_but_false()
    {
        bool managed = TaskbarGlomHelper.TryGetManagedIndex("abc", out var idx);
        Assert.False(managed);
        Assert.Equal(0, idx);   // Read 側はこの 0（＝常に結合・OS 既定）に丸める
    }
}
