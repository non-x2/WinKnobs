using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// CursorSizeHelper（v0.1.10 アクセシビリティ「マウスポインタのサイズ」）の純粋関数テスト。
/// レジストリ I/O（Read/Apply/Backup/Restore）・SystemParametersInfo は VM 実機検証の領域でここでは扱わない。
/// </summary>
public class CursorSizeHelperTests
{
    // ─── プリセットとラベルの対応 ───

    [Fact]
    public void Presets_and_choices_are_same_length()
    {
        Assert.Equal(CursorSizeHelper.PresetSizes.Count, CursorSizeHelper.Choices.Count);
        Assert.Equal(8, CursorSizeHelper.Choices.Count);
    }

    [Fact]
    public void Presets_and_choices_endpoints_match_min_and_max()
    {
        Assert.Equal(CursorSizeHelper.MinSize, CursorSizeHelper.PresetSizes[0]);
        Assert.Equal(CursorSizeHelper.MaxSize, CursorSizeHelper.PresetSizes[CursorSizeHelper.PresetSizes.Count - 1]);

        Assert.True(CursorSizeHelper.TryParseLabel(CursorSizeHelper.Choices[0], out var first));
        Assert.Equal(CursorSizeHelper.MinSize, first);
        Assert.True(CursorSizeHelper.TryParseLabel(CursorSizeHelper.Choices[CursorSizeHelper.Choices.Count - 1], out var last));
        Assert.Equal(CursorSizeHelper.MaxSize, last);
    }

    // ─── ClampSize：[1, 15] に丸める ───

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(8, 8)]
    [InlineData(15, 15)]
    [InlineData(16, 15)]
    [InlineData(-5, 1)]
    [InlineData(int.MaxValue, 15)]
    [InlineData(int.MinValue, 1)]
    public void ClampSize_clamps_to_1_15(int input, int expected)
        => Assert.Equal(expected, CursorSizeHelper.ClampSize(input));

    // ─── ToBaseSize：CursorSize → CursorBaseSize（32 + (n-1)*16・範囲外は Clamp 後） ───

    [Theory]
    [InlineData(1, 32)]
    [InlineData(2, 48)]
    [InlineData(3, 64)]
    [InlineData(5, 96)]
    [InlineData(8, 144)]
    [InlineData(11, 192)]
    [InlineData(15, 256)]
    [InlineData(0, 32)]     // clamp → 1 → 32
    [InlineData(20, 256)]   // clamp → 15 → 256
    [InlineData(int.MinValue, 32)]
    [InlineData(int.MaxValue, 256)]
    public void ToBaseSize_maps_with_formula(int size, int expected)
        => Assert.Equal(expected, CursorSizeHelper.ToBaseSize(size));

    [Fact]
    public void ToBaseSize_endpoints()
    {
        Assert.Equal(CursorSizeHelper.BaseSizeForMin, CursorSizeHelper.ToBaseSize(CursorSizeHelper.MinSize));
        Assert.Equal(256, CursorSizeHelper.ToBaseSize(CursorSizeHelper.MaxSize));
    }

    // ─── TryParseLabel：先頭の整数を取り出す ───

    [Theory]
    [InlineData("1（標準）", true, 1)]
    [InlineData("11", true, 11)]
    [InlineData("15", true, 15)]
    [InlineData("8", true, 8)]
    [InlineData("  3 ", true, 3)]
    [InlineData("abc", false, 0)]
    [InlineData("", false, 0)]
    [InlineData("   ", false, 0)]
    [InlineData("（1）", false, 0)]   // 先頭に数字なし
    public void TryParseLabel_extracts_leading_int(string label, bool ok, int expected)
    {
        var result = CursorSizeHelper.TryParseLabel(label, out var value);
        Assert.Equal(ok, result);
        if (ok) Assert.Equal(expected, value);
    }

    [Fact]
    public void TryParseLabel_null_returns_false()
        => Assert.False(CursorSizeHelper.TryParseLabel(null, out _));

    // ─── NearestPresetLabel：最も近いプリセット（PresetSizes={1,2,3,4,5,8,11,15}） ───

    [Theory]
    [InlineData(1, "1（標準）")]
    [InlineData(6, "5")]    // 5(1) < 8(2)
    [InlineData(7, "8")]    // 8(1) < 5(2)
    [InlineData(10, "11")]  // 11(1) < 8(2)
    [InlineData(14, "15")]  // 15(1) < 11(3)
    [InlineData(15, "15")]
    [InlineData(0, "1（標準）")]
    [InlineData(100, "15")]
    public void NearestPresetLabel_maps_to_closest(int cursorSize, string expected)
        => Assert.Equal(expected, CursorSizeHelper.NearestPresetLabel(cursorSize));

    // ─── plan-validator 致命対応：戻り値は必ず Choices 内（ComboBox 整合 invariant・int.Min/Max 含む） ───

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(9)]
    [InlineData(15)]
    [InlineData(500)]
    [InlineData(int.MaxValue)]
    public void NearestPresetLabel_always_returns_a_valid_choice(int cursorSize)
        => Assert.Contains(CursorSizeHelper.NearestPresetLabel(cursorSize), CursorSizeHelper.Choices);

    // ─── CoerceInt：REG_DWORD(int) / REG_SZ(string) / null を数値化（型混在環境対策） ───

    [Theory]
    [InlineData(128, 128)]      // int（REG_DWORD）
    [InlineData(32, 32)]
    public void CoerceInt_int_passthrough(int boxed, int expected)
        => Assert.Equal(expected, CursorSizeHelper.CoerceInt(boxed, -1));

    [Fact]
    public void CoerceInt_string_parses()      // REG_SZ "128"
        => Assert.Equal(128, CursorSizeHelper.CoerceInt("128", -1));

    [Fact]
    public void CoerceInt_null_returns_fallback()
        => Assert.Equal(99, CursorSizeHelper.CoerceInt(null, 99));

    [Fact]
    public void CoerceInt_unparsable_returns_fallback()   // REG_SZ "abc" など
        => Assert.Equal(32, CursorSizeHelper.CoerceInt("abc", 32));

    // ─── ラウンドトリップ：各プリセット→ラベル→パースで実値に戻る ───

    [Fact]
    public void EachPreset_roundtrips_label_to_value()
    {
        for (int idx = 0; idx < CursorSizeHelper.PresetSizes.Count; idx++)
        {
            var label = CursorSizeHelper.Choices[idx];
            Assert.True(CursorSizeHelper.TryParseLabel(label, out var parsed));
            Assert.Equal(CursorSizeHelper.PresetSizes[idx], parsed);
        }
    }
}
