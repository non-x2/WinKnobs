using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// CaretWidthHelper（v0.1.12 アクセシビリティ「テキストカーソルの太さ」）の純粋関数テスト。
/// レジストリ I/O（Read/Apply/Backup/Restore）は VM 実機検証の領域でここでは扱わない。
/// CoerceInt は委譲先 <see cref="RegistryValueCoercion"/> 側で別途テスト（RegistryValueCoercionTests）。
/// </summary>
public class CaretWidthHelperTests
{
    // ─── プリセットとラベルの対応 ───

    [Fact]
    public void Presets_and_choices_are_same_length()
    {
        Assert.Equal(CaretWidthHelper.PresetValues.Count, CaretWidthHelper.Choices.Count);
        Assert.Equal(7, CaretWidthHelper.Choices.Count);
    }

    // plan-validator 指摘4：const(MinWidth/MaxWidth) と PresetValues / Choices の端値整合を将来も担保。
    [Fact]
    public void Presets_and_choices_endpoints_match_min_and_max()
    {
        Assert.Equal(CaretWidthHelper.MinWidth, CaretWidthHelper.PresetValues[0]);
        Assert.Equal(CaretWidthHelper.MaxWidth, CaretWidthHelper.PresetValues[CaretWidthHelper.PresetValues.Count - 1]);

        Assert.True(CaretWidthHelper.TryParseLabel(CaretWidthHelper.Choices[0], out var firstLabel));
        Assert.Equal(CaretWidthHelper.MinWidth, firstLabel);
        Assert.True(CaretWidthHelper.TryParseLabel(CaretWidthHelper.Choices[CaretWidthHelper.Choices.Count - 1], out var lastLabel));
        Assert.Equal(CaretWidthHelper.MaxWidth, lastLabel);
    }

    // ─── ClampToRange：[1, 20] に丸める ───

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(20, 20)]
    [InlineData(21, 20)]
    [InlineData(-50, 1)]
    [InlineData(int.MaxValue, 20)]
    [InlineData(int.MinValue, 1)]
    public void ClampToRange_clamps_to_1_20(int input, int expected)
        => Assert.Equal(expected, CaretWidthHelper.ClampToRange(input));

    // ─── TryParseLabel：先頭の整数（px）を取り出す ───

    [Theory]
    [InlineData("3px", true, 3)]
    [InlineData("1px（既定）", true, 1)]
    [InlineData("20px", true, 20)]
    [InlineData("10", true, 10)]        // px なしも寛容に
    [InlineData("  5px ", true, 5)]     // 先頭空白許容
    [InlineData("abc", false, 0)]
    [InlineData("", false, 0)]
    [InlineData("   ", false, 0)]
    [InlineData("px3", false, 0)]       // 先頭に数字なし
    public void TryParseLabel_extracts_leading_int(string label, bool ok, int expected)
    {
        var result = CaretWidthHelper.TryParseLabel(label, out var value);
        Assert.Equal(ok, result);
        if (ok) Assert.Equal(expected, value);
    }

    [Fact]
    public void TryParseLabel_null_returns_false()
        => Assert.False(CaretWidthHelper.TryParseLabel(null, out _));

    // ─── NearestPresetLabel：最も近いプリセットのラベル ───
    // プリセット {1,2,3,4,5,10,20}。同距離は小さい方（先に出現）を採用。

    [Theory]
    [InlineData(1, "1px（既定）")]
    [InlineData(3, "3px")]
    [InlineData(6, "5px")]    // |6-5|=1 < |6-10|=4
    [InlineData(7, "5px")]    // |7-5|=2 < |7-10|=3
    [InlineData(8, "10px")]   // |8-5|=3 > |8-10|=2
    [InlineData(12, "10px")]  // |12-10|=2 < |12-20|=8
    [InlineData(15, "10px")]  // |15-10|=5 = |15-20|=5 → 同距離は小さい方(10px)
    [InlineData(16, "20px")]  // |16-10|=6 > |16-20|=4
    [InlineData(21, "20px")]  // 範囲外（上）→最大プリセット
    [InlineData(0, "1px（既定）")]
    [InlineData(-5, "1px（既定）")]
    public void NearestPresetLabel_maps_to_closest(int value, string expected)
        => Assert.Equal(expected, CaretWidthHelper.NearestPresetLabel(value));

    // ─── plan-validator 致命対応：戻り値は必ず Choices に含まれる（ComboBox 整合 invariant） ───

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(12)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void NearestPresetLabel_always_returns_a_valid_choice(int value)
        => Assert.Contains(CaretWidthHelper.NearestPresetLabel(value), CaretWidthHelper.Choices);

    // ─── ラウンドトリップ：各プリセット値→ラベル→パースで実値に戻る ───

    [Fact]
    public void EachPreset_roundtrips_label_to_value()
    {
        for (int idx = 0; idx < CaretWidthHelper.PresetValues.Count; idx++)
        {
            var label = CaretWidthHelper.Choices[idx];
            Assert.True(CaretWidthHelper.TryParseLabel(label, out var parsed));
            Assert.Equal(CaretWidthHelper.PresetValues[idx], parsed);
        }
    }

    // ─── CoerceInt は委譲先と同一挙動（前方互換ラッパの担保） ───

    [Fact]
    public void CoerceInt_delegates_to_shared_coercion()
    {
        Assert.Equal(5, CaretWidthHelper.CoerceInt(5, -1));        // int passthrough
        Assert.Equal(3, CaretWidthHelper.CoerceInt("3", -1));      // REG_SZ
        Assert.Equal(1, CaretWidthHelper.CoerceInt(null, 1));      // 未設定→fallback
        Assert.Equal(CaretWidthHelper.MinWidth, CaretWidthHelper.CoerceInt(new byte[] { 1, 2 }, CaretWidthHelper.MinWidth)); // 異常型(REG_BINARY)→fallback
    }
}
