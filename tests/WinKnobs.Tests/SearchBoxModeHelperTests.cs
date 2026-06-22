using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// SearchBoxModeHelper（v0.3.2 タスクバー検索ボックス表示・SearchboxTaskbarMode）の純粋関数テスト。
/// レジストリ I/O は VM/実機検証の領域でここでは扱わない（TaskManagerHelperTests と同方針）。
/// </summary>
public class SearchBoxModeHelperTests
{
    [Fact]
    public void Choices_has_four_entries()
        => Assert.Equal(4, SearchBoxModeHelper.Choices.Count);

    [Fact]
    public void Mode_range_is_zero_to_three()
    {
        Assert.Equal(0, SearchBoxModeHelper.MinMode);
        Assert.Equal(3, SearchBoxModeHelper.MaxMode);
        Assert.Equal(SearchBoxModeHelper.MaxMode + 1, SearchBoxModeHelper.Choices.Count);
    }

    // ─── IsValidMode：0..3 のみ true ───
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(-1, false)]
    [InlineData(4, false)]
    [InlineData(100, false)]
    public void IsValidMode_true_only_for_0_to_3(int value, bool expected)
        => Assert.Equal(expected, SearchBoxModeHelper.IsValidMode(value));

    // ─── LabelForValue：必ず Choices の同一参照／範囲外・null は null ───
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LabelForValue_returns_same_reference_as_choices(int value)
        => Assert.Same(SearchBoxModeHelper.Choices[value], SearchBoxModeHelper.LabelForValue(value));

    [Fact]
    public void LabelForValue_null_is_null()
        => Assert.Null(SearchBoxModeHelper.LabelForValue(null));

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(99)]
    public void LabelForValue_out_of_range_is_null(int value)
        => Assert.Null(SearchBoxModeHelper.LabelForValue(value));

    // ─── ValueForLabel：ラベル→0..3／不正・センチネル・null は -1 ───
    [Fact]
    public void ValueForLabel_maps_each_choice_to_its_index()
    {
        for (int i = 0; i < SearchBoxModeHelper.Choices.Count; i++)
            Assert.Equal(i, SearchBoxModeHelper.ValueForLabel(SearchBoxModeHelper.Choices[i]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("でたらめ")]
    [InlineData("（変更しない・現在のまま）")]   // ViewModel 側のセンチネルは Helper の Choices に無い→-1
    public void ValueForLabel_unknown_returns_minus_one(string? label)
        => Assert.Equal(-1, SearchBoxModeHelper.ValueForLabel(label));

    // ─── ラウンドトリップ：value→label→value ───
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Value_label_value_roundtrip(int value)
    {
        var label = SearchBoxModeHelper.LabelForValue(value);
        Assert.NotNull(label);
        Assert.Equal(value, SearchBoxModeHelper.ValueForLabel(label));
    }

    // ─── CoerceInt：REG_SZ "2"／int 2／不正は fallback ───
    [Fact]
    public void CoerceInt_parses_string_and_int()
    {
        Assert.Equal(2, SearchBoxModeHelper.CoerceInt("2", -1));
        Assert.Equal(2, SearchBoxModeHelper.CoerceInt(2, -1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void CoerceInt_invalid_returns_fallback(object? v)
        => Assert.Equal(-1, SearchBoxModeHelper.CoerceInt(v, -1));

    // ─── TryGetManagedMode：管理対象（0..3）判定の一元化（cross-reviewer 致命⑥/重要⑦/⑧） ───

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TryGetManagedMode_int_in_range_is_managed(int value)
    {
        Assert.True(SearchBoxModeHelper.TryGetManagedMode(value, out var m));
        Assert.Equal(value, m);
    }

    [Fact]
    public void TryGetManagedMode_reg_sz_string_is_managed()
    {
        Assert.True(SearchBoxModeHelper.TryGetManagedMode("2", out var m));   // REG_SZ "2" 混在
        Assert.Equal(2, m);
    }

    [Fact]
    public void TryGetManagedMode_null_is_unmanaged()
        => Assert.False(SearchBoxModeHelper.TryGetManagedMode(null, out _));

    [Theory]
    [InlineData(4)]     // 範囲外（既知モード外）
    [InlineData(5)]
    [InlineData(-1)]
    [InlineData(99)]
    public void TryGetManagedMode_out_of_range_is_unmanaged(int value)
        => Assert.False(SearchBoxModeHelper.TryGetManagedMode(value, out _));

    [Theory]
    [InlineData("abc")]          // 非数値 REG_SZ
    [InlineData("00 00 00 00")]  // REG_BINARY 16進文字列風（coerce 不可）
    public void TryGetManagedMode_non_numeric_is_unmanaged(object raw)
        => Assert.False(SearchBoxModeHelper.TryGetManagedMode(raw, out _));

    [Fact]
    public void TryGetManagedMode_unmanaged_yields_zero_mode_but_false()
    {
        // 非管理時は mode=0 だが戻り値 false＝呼び出し側は ValueExisted=false とし Restore で書込まず削除する
        bool managed = SearchBoxModeHelper.TryGetManagedMode("abc", out var m);
        Assert.False(managed);
        Assert.Equal(0, m);
    }
}
