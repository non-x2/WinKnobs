using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// ScrollbarHelper（v0.1.11 アクセシビリティ「スクロールバーを常に表示」）の純粋関数テスト。
/// レジストリ I/O（Read/Apply/Backup/Restore）は VM 実機検証の領域でここでは扱わない。
/// </summary>
public class ScrollbarHelperTests
{
    [Fact]
    public void Choices_has_two_entries()
        => Assert.Equal(2, ScrollbarHelper.Choices.Count);

    // ─── IsAlwaysShow：0=true / それ以外・null=false ───

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(-1, false)]
    public void IsAlwaysShow_only_zero_is_true(int value, bool expected)
        => Assert.Equal(expected, ScrollbarHelper.IsAlwaysShow(value));

    [Fact]
    public void IsAlwaysShow_null_is_false()
        => Assert.False(ScrollbarHelper.IsAlwaysShow(null));

    // ─── ValueForLabel：「常に表示する」→0 / それ以外→1 ───

    [Fact]
    public void ValueForLabel_always_show_returns_zero()
        => Assert.Equal(ScrollbarHelper.ValueAlwaysShow, ScrollbarHelper.ValueForLabel(ScrollbarHelper.AlwaysShowLabel));

    [Fact]
    public void ValueForLabel_auto_hide_returns_one()
        => Assert.Equal(ScrollbarHelper.ValueAutoHide, ScrollbarHelper.ValueForLabel(ScrollbarHelper.AutoHideLabel));

    [Theory]
    [InlineData("foo")]
    [InlineData("")]
    [InlineData(null)]
    public void ValueForLabel_unknown_returns_auto_hide(string? label)
        => Assert.Equal(ScrollbarHelper.ValueAutoHide, ScrollbarHelper.ValueForLabel(label));

    // ─── LabelForValue：0→常に表示 / 1・null・他→既定。plan-validator 致命：必ず Choices 内の同一参照 ───

    [Fact]
    public void LabelForValue_zero_is_always_show_same_reference()
    {
        var label = ScrollbarHelper.LabelForValue(0);
        Assert.Equal(ScrollbarHelper.AlwaysShowLabel, label);
        Assert.Same(ScrollbarHelper.Choices[1], label);   // 同一参照（リテラル直書きでない）
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-5)]
    public void LabelForValue_nonzero_is_auto_hide(int value)
    {
        var label = ScrollbarHelper.LabelForValue(value);
        Assert.Same(ScrollbarHelper.Choices[0], label);
    }

    [Fact]
    public void LabelForValue_null_is_auto_hide()
        => Assert.Same(ScrollbarHelper.Choices[0], ScrollbarHelper.LabelForValue(null));

    // plan-validator 致命対応：戻り値は必ず Choices 内（ComboBox 整合 invariant）
    [Theory]
    [InlineData(null)]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void LabelForValue_always_returns_a_valid_choice(int? value)
        => Assert.Contains(ScrollbarHelper.LabelForValue(value), ScrollbarHelper.Choices);

    // ─── ラベル⇔値ラウンドトリップ ───

    [Fact]
    public void Label_value_roundtrips()
    {
        // 常に表示
        Assert.Equal(ScrollbarHelper.AlwaysShowLabel,
            ScrollbarHelper.LabelForValue(ScrollbarHelper.ValueForLabel(ScrollbarHelper.AlwaysShowLabel)));
        // 既定
        Assert.Equal(ScrollbarHelper.AutoHideLabel,
            ScrollbarHelper.LabelForValue(ScrollbarHelper.ValueForLabel(ScrollbarHelper.AutoHideLabel)));
    }

    // ─── CoerceInt：REG_DWORD(int) / REG_SZ "0"・"1"(string) / null・異常を数値化（型混在対策） ───

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public void CoerceInt_int_passthrough(int boxed, int expected)
        => Assert.Equal(expected, ScrollbarHelper.CoerceInt(boxed, -1));

    [Theory]
    [InlineData("0", 0)]   // REG_SZ "0"＝常に表示 を取りこぼさない
    [InlineData("1", 1)]
    public void CoerceInt_string_parses(string s, int expected)
        => Assert.Equal(expected, ScrollbarHelper.CoerceInt(s, -1));

    [Fact]
    public void CoerceInt_null_returns_fallback()
        => Assert.Equal(1, ScrollbarHelper.CoerceInt(null, 1));

    [Fact]
    public void CoerceInt_unparsable_returns_fallback()
        => Assert.Equal(1, ScrollbarHelper.CoerceInt("abc", 1));

    // cross-reviewer EC-1：string 経路の境界（負・桁あふれ・小数）。fallback=7 で判別。
    // いずれも実害なし＝Coerce(REG)値は LabelForValue が必ず Choices 内に丸める。
    [Theory]
    [InlineData("-5", -5)]            // 負の文字列も int.TryParse 成功
    [InlineData("99999999999", 7)]    // int 範囲外 → fallback
    [InlineData("3.5", 7)]            // 小数 → int.TryParse 失敗 → fallback
    public void CoerceInt_string_boundaries(string s, int expected)
        => Assert.Equal(expected, ScrollbarHelper.CoerceInt(s, 7));
}
