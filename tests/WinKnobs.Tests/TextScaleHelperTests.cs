using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// TextScaleHelper（v0.1.9 アクセシビリティ「テキストのサイズ」）の純粋関数テスト。
/// レジストリ I/O（Read/Apply/Backup/Restore）は VM 実機検証の領域でここでは扱わない。
/// </summary>
public class TextScaleHelperTests
{
    // ─── プリセットとラベルの対応 ───

    [Fact]
    public void Presets_and_choices_are_same_length()
    {
        Assert.Equal(TextScaleHelper.PresetValues.Count, TextScaleHelper.Choices.Count);
        Assert.Equal(7, TextScaleHelper.Choices.Count);
    }

    // cross-reviewer 保守性：const(MinScale/MaxScale) と PresetValues / Choices の端値整合を将来も担保。
    // MaxScale だけ変えて Choices 配列を直し忘れる事故を検出する。
    [Fact]
    public void Presets_and_choices_endpoints_match_min_and_max()
    {
        Assert.Equal(TextScaleHelper.MinScale, TextScaleHelper.PresetValues[0]);
        Assert.Equal(TextScaleHelper.MaxScale, TextScaleHelper.PresetValues[TextScaleHelper.PresetValues.Count - 1]);

        Assert.True(TextScaleHelper.TryParseLabel(TextScaleHelper.Choices[0], out var firstLabel));
        Assert.Equal(TextScaleHelper.MinScale, firstLabel);
        Assert.True(TextScaleHelper.TryParseLabel(TextScaleHelper.Choices[TextScaleHelper.Choices.Count - 1], out var lastLabel));
        Assert.Equal(TextScaleHelper.MaxScale, lastLabel);
    }

    // ─── ClampToRange：[100, 225] に丸める ───

    [Theory]
    [InlineData(99, 100)]
    [InlineData(100, 100)]
    [InlineData(150, 150)]
    [InlineData(225, 225)]
    [InlineData(226, 225)]
    [InlineData(0, 100)]
    [InlineData(-50, 100)]
    [InlineData(int.MaxValue, 225)]
    [InlineData(int.MinValue, 100)]
    public void ClampToRange_clamps_to_100_225(int input, int expected)
        => Assert.Equal(expected, TextScaleHelper.ClampToRange(input));

    // ─── TryParseLabel：先頭の整数（パーセント）を取り出す ───

    [Theory]
    [InlineData("135%", true, 135)]
    [InlineData("100%（既定）", true, 100)]
    [InlineData("225%", true, 225)]
    [InlineData("150", true, 150)]        // % なしも寛容に
    [InlineData("  175% ", true, 175)]    // 先頭空白許容
    [InlineData("abc", false, 0)]
    [InlineData("", false, 0)]
    [InlineData("   ", false, 0)]
    [InlineData("%135", false, 0)]        // 先頭に数字なし
    public void TryParseLabel_extracts_leading_int(string label, bool ok, int expected)
    {
        var result = TextScaleHelper.TryParseLabel(label, out var value);
        Assert.Equal(ok, result);
        if (ok) Assert.Equal(expected, value);
    }

    [Fact]
    public void TryParseLabel_null_returns_false()
        => Assert.False(TextScaleHelper.TryParseLabel(null, out _));

    // ─── NearestPresetLabel：最も近いプリセットのラベル ───

    [Theory]
    [InlineData(100, "100%（既定）")]
    [InlineData(135, "135%")]
    [InlineData(140, "135%")]   // 135(5) < 150(10)
    [InlineData(160, "150%")]   // 150(10) < 175(15)
    [InlineData(188, "200%")]   // 200(12) < 175(13)
    [InlineData(213, "225%")]   // 225(12) < 200(13)
    [InlineData(50, "100%（既定）")]
    [InlineData(300, "225%")]
    [InlineData(175, "175%")]
    public void NearestPresetLabel_maps_to_closest(int value, string expected)
        => Assert.Equal(expected, TextScaleHelper.NearestPresetLabel(value));

    // ─── plan-validator 致命対応：戻り値は必ず Choices に含まれる（ComboBox 整合 invariant） ───

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(137)]
    [InlineData(188)]
    [InlineData(225)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void NearestPresetLabel_always_returns_a_valid_choice(int value)
        => Assert.Contains(TextScaleHelper.NearestPresetLabel(value), TextScaleHelper.Choices);

    // ─── ラウンドトリップ：各プリセット値→ラベル→パースで実値に戻る ───

    [Fact]
    public void EachPreset_roundtrips_label_to_value()
    {
        for (int idx = 0; idx < TextScaleHelper.PresetValues.Count; idx++)
        {
            var label = TextScaleHelper.Choices[idx];
            Assert.True(TextScaleHelper.TryParseLabel(label, out var parsed));
            Assert.Equal(TextScaleHelper.PresetValues[idx], parsed);
        }
    }
}
