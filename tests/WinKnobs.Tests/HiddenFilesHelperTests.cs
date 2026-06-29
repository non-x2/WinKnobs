using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// HiddenFilesHelper（v0.3.12 隠しファイル・フォルダーを表示・Hidden）の純粋関数テスト。
/// レジストリ I/O は VM/実機検証の領域でここでは扱わない（SearchBoxModeHelperTests と同方針）。
/// 最大の検証点＝「レジストリ値（1/2）≠ 選択肢 index（0/1）」の対応が崩れていないこと。
/// </summary>
public class HiddenFilesHelperTests
{
    [Fact]
    public void Choices_has_two_entries()
        => Assert.Equal(2, HiddenFilesHelper.Choices.Count);

    [Fact]
    public void Registry_values_are_one_and_two()
    {
        Assert.Equal(1, HiddenFilesHelper.ValueShow);
        Assert.Equal(2, HiddenFilesHelper.ValueHide);
    }

    // ─── IndexForValue：1→0 / 2→1 / それ以外（0 含む）は -1 ───
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(0, -1)]   // 0 は Windows 標準でない＝管理外
    [InlineData(3, -1)]
    [InlineData(-1, -1)]
    [InlineData(99, -1)]
    public void IndexForValue_maps_only_1_and_2(int value, int expectedIndex)
        => Assert.Equal(expectedIndex, HiddenFilesHelper.IndexForValue(value));

    // ─── ValueForIndex：0→1 / 1→2 / 範囲外は -1 ───
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(-1, -1)]
    [InlineData(2, -1)]
    [InlineData(99, -1)]
    public void ValueForIndex_maps_only_0_and_1(int index, int expectedValue)
        => Assert.Equal(expectedValue, HiddenFilesHelper.ValueForIndex(index));

    // ─── ValueForLabel：ラベル→1/2／不正・センチネル・null は -1 ───
    [Fact]
    public void ValueForLabel_maps_each_choice_to_registry_value()
    {
        Assert.Equal(1, HiddenFilesHelper.ValueForLabel(HiddenFilesHelper.Choices[0]));  // 表示する
        Assert.Equal(2, HiddenFilesHelper.ValueForLabel(HiddenFilesHelper.Choices[1]));  // 表示しない
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("でたらめ")]
    [InlineData("（変更しない・現在のまま）")]   // ViewModel 側のセンチネルは Helper の Choices に無い→-1
    public void ValueForLabel_unknown_returns_minus_one(string? label)
        => Assert.Equal(-1, HiddenFilesHelper.ValueForLabel(label));

    // ─── ラウンドトリップ：value(1/2)→index→value ───
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Value_index_value_roundtrip(int value)
    {
        int idx = HiddenFilesHelper.IndexForValue(value);
        Assert.InRange(idx, 0, 1);
        Assert.Equal(value, HiddenFilesHelper.ValueForIndex(idx));
    }

    // ─── ラウンドトリップ：label→value→index→label（同一参照） ───
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Label_value_index_label_roundtrip(int index)
    {
        var label = HiddenFilesHelper.Choices[index];
        int value = HiddenFilesHelper.ValueForLabel(label);
        int idx = HiddenFilesHelper.IndexForValue(value);
        Assert.Same(label, HiddenFilesHelper.Choices[idx]);
    }

    // ─── CoerceInt：REG_SZ "1"／int 2／不正は fallback ───
    [Fact]
    public void CoerceInt_parses_string_and_int()
    {
        Assert.Equal(1, HiddenFilesHelper.CoerceInt("1", -1));
        Assert.Equal(2, HiddenFilesHelper.CoerceInt(2, -1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void CoerceInt_invalid_returns_fallback(object? v)
        => Assert.Equal(-1, HiddenFilesHelper.CoerceInt(v, -1));

    // ─── TryGetManagedIndex：管理対象（1/2）判定の一元化 ───

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void TryGetManagedIndex_int_1_or_2_is_managed(int value, int expectedIndex)
    {
        Assert.True(HiddenFilesHelper.TryGetManagedIndex(value, out var idx));
        Assert.Equal(expectedIndex, idx);
    }

    [Fact]
    public void TryGetManagedIndex_reg_sz_string_is_managed()
    {
        Assert.True(HiddenFilesHelper.TryGetManagedIndex("1", out var idx));   // REG_SZ "1" 混在
        Assert.Equal(0, idx);
    }

    [Fact]
    public void TryGetManagedIndex_null_is_unmanaged()
        => Assert.False(HiddenFilesHelper.TryGetManagedIndex(null, out _));

    [Theory]
    [InlineData(0)]     // 0 は Windows 標準でない＝管理外
    [InlineData(3)]
    [InlineData(-1)]
    [InlineData(99)]
    public void TryGetManagedIndex_out_of_range_is_unmanaged(int value)
        => Assert.False(HiddenFilesHelper.TryGetManagedIndex(value, out _));

    [Theory]
    [InlineData("abc")]          // 非数値 REG_SZ
    [InlineData("00 00 00 00")]  // REG_BINARY 16進文字列風（coerce 不可）
    public void TryGetManagedIndex_non_numeric_is_unmanaged(object raw)
        => Assert.False(HiddenFilesHelper.TryGetManagedIndex(raw, out _));

    [Fact]
    public void TryGetManagedIndex_unmanaged_yields_zero_index_but_false()
    {
        // 非管理時は index=0 だが戻り値 false＝呼び出し側は ValueExisted=false とし Restore で書込まず削除する
        bool managed = HiddenFilesHelper.TryGetManagedIndex("abc", out var idx);
        Assert.False(managed);
        Assert.Equal(0, idx);
    }
}
