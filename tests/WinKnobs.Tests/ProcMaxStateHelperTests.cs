using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// ProcMaxStateHelper（v0.3.11 最大のプロセッサの状態・PROCTHROTTLEMAX %）の純粋関数テスト。
/// powercfg I/O は実機/VM 検証の領域でここでは扱わない（ProcessorBoost と同方針）。
/// </summary>
public class ProcMaxStateHelperTests
{
    [Fact]
    public void Percents_and_choices_align_by_index()
        => Assert.Equal(ProcMaxStateHelper.Percents.Count, ProcMaxStateHelper.Choices.Count);

    [Fact]
    public void Percents_are_expected_presets()
        => Assert.Equal(new[] { 100, 90, 80, 70, 60, 50 }, ProcMaxStateHelper.Percents);

    [Theory]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(101, false)]
    public void IsValidPercent_range_1_to_100(int p, bool expected)
        => Assert.Equal(expected, ProcMaxStateHelper.IsValidPercent(p));

    // ─── LabelForPercent：必ず Choices の同一参照／プリセット外は null ───
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void LabelForPercent_returns_same_reference(int idx)
    {
        var p = ProcMaxStateHelper.Percents[idx];
        Assert.Same(ProcMaxStateHelper.Choices[idx], ProcMaxStateHelper.LabelForPercent(p));
    }

    [Theory]
    [InlineData(95)]   // プリセット外（誤って丸めない）
    [InlineData(75)]
    [InlineData(0)]
    [InlineData(100000)]
    public void LabelForPercent_non_preset_is_null(int p)
        => Assert.Null(ProcMaxStateHelper.LabelForPercent(p));

    // ─── PercentForLabel：ラベル→percent／不正・センチネル・null は -1 ───
    [Fact]
    public void PercentForLabel_maps_each_choice_to_its_percent()
    {
        for (int i = 0; i < ProcMaxStateHelper.Choices.Count; i++)
            Assert.Equal(ProcMaxStateHelper.Percents[i], ProcMaxStateHelper.PercentForLabel(ProcMaxStateHelper.Choices[i]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("でたらめ")]
    [InlineData("（変更しない・現在のまま）")]   // ViewModel 側センチネルは Helper の Choices に無い→-1
    public void PercentForLabel_unknown_returns_minus_one(string? label)
        => Assert.Equal(-1, ProcMaxStateHelper.PercentForLabel(label));

    // ─── ラウンドトリップ：percent→label→percent ───
    [Theory]
    [InlineData(100)]
    [InlineData(90)]
    [InlineData(70)]
    [InlineData(50)]
    public void Percent_label_percent_roundtrip(int p)
    {
        var label = ProcMaxStateHelper.LabelForPercent(p);
        Assert.NotNull(label);
        Assert.Equal(p, ProcMaxStateHelper.PercentForLabel(label));
    }
}
