using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// LaunchToHelper（v0.3.14 エクスプローラの起動先・LaunchTo）の純粋関数テスト。
/// レジストリ I/O は VM/実機検証の領域でここでは扱わない（HiddenFilesHelperTests と同方針）。
/// 検証点＝「レジストリ値（1/2/3・0も）≠ 選択肢 index（0/1/2）」の対応と、0/2 がともにホームになること。
/// </summary>
public class LaunchToHelperTests
{
    [Fact]
    public void Choices_has_three_entries()
        => Assert.Equal(3, LaunchToHelper.Choices.Count);

    [Fact]
    public void Registry_values_are_canonical()
    {
        Assert.Equal(1, LaunchToHelper.ValueThisPc);
        Assert.Equal(2, LaunchToHelper.ValueHome);
        Assert.Equal(3, LaunchToHelper.ValueDownloads);
    }

    // ─── IndexForValue：0/2→ホーム(0) / 1→このPC(1) / 3→DL(2) / 4等→-1 ───
    [Theory]
    [InlineData(0, 0)]    // 0 もホーム
    [InlineData(2, 0)]    // 2 もホーム
    [InlineData(1, 1)]
    [InlineData(3, 2)]
    [InlineData(4, -1)]   // OneDrive＝管理外
    [InlineData(5, -1)]
    [InlineData(-1, -1)]
    public void IndexForValue_maps_with_zero_as_home(int value, int expectedIndex)
        => Assert.Equal(expectedIndex, LaunchToHelper.IndexForValue(value));

    // ─── ValueForIndex：0→2(ホームは2を書く) / 1→1 / 2→3 / 範囲外→-1 ───
    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(-1, -1)]
    [InlineData(3, -1)]
    public void ValueForIndex_home_writes_two(int index, int expectedValue)
        => Assert.Equal(expectedValue, LaunchToHelper.ValueForIndex(index));

    // ─── ValueForLabel：ラベル→書込値（2/1/3）／不正・センチネル・null は -1 ───
    [Fact]
    public void ValueForLabel_maps_each_choice_to_registry_value()
    {
        Assert.Equal(2, LaunchToHelper.ValueForLabel(LaunchToHelper.Choices[0]));  // ホーム→2
        Assert.Equal(1, LaunchToHelper.ValueForLabel(LaunchToHelper.Choices[1]));  // このPC→1
        Assert.Equal(3, LaunchToHelper.ValueForLabel(LaunchToHelper.Choices[2]));  // ダウンロード→3
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("でたらめ")]
    [InlineData("（変更しない・現在のまま）")]
    public void ValueForLabel_unknown_returns_minus_one(string? label)
        => Assert.Equal(-1, LaunchToHelper.ValueForLabel(label));

    // ─── ラウンドトリップ：書込値(2/1/3)→index→書込値（ホームは2へ正規化） ───
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Canonical_value_index_value_roundtrip(int value)
    {
        int idx = LaunchToHelper.IndexForValue(value);
        Assert.InRange(idx, 0, 2);
        Assert.Equal(value, LaunchToHelper.ValueForIndex(idx));
    }

    [Fact]
    public void Zero_maps_to_home_index_but_normalizes_to_two_on_write()
    {
        int idx = LaunchToHelper.IndexForValue(0);
        Assert.Equal(0, idx);                                   // 0 はホーム
        Assert.Equal(2, LaunchToHelper.ValueForIndex(idx));     // 書き戻しは 2（GUI と同じ）
    }

    // ─── label→value→index→label（同一参照） ───
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Label_value_index_label_roundtrip(int index)
    {
        var label = LaunchToHelper.Choices[index];
        int value = LaunchToHelper.ValueForLabel(label);
        int idx = LaunchToHelper.IndexForValue(value);
        Assert.Same(label, LaunchToHelper.Choices[idx]);
    }

    // ─── CoerceInt ───
    [Fact]
    public void CoerceInt_parses_string_and_int()
    {
        Assert.Equal(1, LaunchToHelper.CoerceInt("1", -1));
        Assert.Equal(3, LaunchToHelper.CoerceInt(3, -1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void CoerceInt_invalid_returns_fallback(object? v)
        => Assert.Equal(-1, LaunchToHelper.CoerceInt(v, -1));

    // ─── TryGetManagedIndex ───
    [Theory]
    [InlineData(0, 0)]    // 0=ホーム
    [InlineData(2, 0)]    // 2=ホーム
    [InlineData(1, 1)]
    [InlineData(3, 2)]
    public void TryGetManagedIndex_in_range_is_managed(int value, int expectedIndex)
    {
        Assert.True(LaunchToHelper.TryGetManagedIndex(value, out var idx));
        Assert.Equal(expectedIndex, idx);
    }

    [Fact]
    public void TryGetManagedIndex_reg_sz_string_is_managed()
    {
        Assert.True(LaunchToHelper.TryGetManagedIndex("3", out var idx));
        Assert.Equal(2, idx);
    }

    [Fact]
    public void TryGetManagedIndex_null_is_unmanaged()
        => Assert.False(LaunchToHelper.TryGetManagedIndex(null, out _));

    [Theory]
    [InlineData(4)]      // OneDrive＝3択外
    [InlineData(5)]
    [InlineData(-1)]
    public void TryGetManagedIndex_out_of_range_is_unmanaged(int value)
        => Assert.False(LaunchToHelper.TryGetManagedIndex(value, out _));

    [Theory]
    [InlineData("abc")]
    [InlineData("00 00 00 00")]
    public void TryGetManagedIndex_non_numeric_is_unmanaged(object raw)
        => Assert.False(LaunchToHelper.TryGetManagedIndex(raw, out _));

    [Fact]
    public void TryGetManagedIndex_unmanaged_yields_zero_index_but_false()
    {
        bool managed = LaunchToHelper.TryGetManagedIndex("abc", out var idx);
        Assert.False(managed);
        Assert.Equal(0, idx);
    }
}
