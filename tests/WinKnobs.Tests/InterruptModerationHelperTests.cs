using System.Collections.Generic;
using WinKnobs.Services;
using Xunit;

namespace WinKnobs.Tests;

/// <summary>
/// v0.4.6 InterruptModerationHelper（純粋関数）のテスト。
/// 対象キーワードは NDIS 標準の単一プロパティ・固定値域（0/1）のため、NicPowerHelper と異なり
/// ValidValues 動的ガードは持たない点・FlexibleListConverter 再利用による PS 5.1 単一要素展開吸収を検証。
/// </summary>
public class InterruptModerationHelperTests
{
    // ---------- BuildReadCommand ----------

    [Fact]
    public void BuildReadCommand_対象キーワードと物理アダプタ限定を含む()
    {
        var cmd = InterruptModerationHelper.BuildReadCommand();
        Assert.Contains("Get-NetAdapter -Physical", cmd);
        Assert.Contains("*InterruptModeration", cmd);
        Assert.Contains("ConvertTo-Json", cmd);
    }

    // ---------- BuildApplyCommand ----------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void BuildApplyCommand_指定値とNoRestartと個別集計を含む(int value)
    {
        var cmd = InterruptModerationHelper.BuildApplyCommand(value);
        Assert.Contains($"-RegistryValue '{value}' -NoRestart", cmd);
        Assert.Contains("$ok++", cmd);
        Assert.Contains("$fail++", cmd);
        Assert.Contains(InterruptModerationHelper.ResultMarker, cmd);
    }

    // ---------- BuildRestoreCommand ----------

    [Fact]
    public void BuildRestoreCommand_GUIDごとにName再解決して個別集計()
    {
        var cmd = InterruptModerationHelper.BuildRestoreCommand(new[]
        {
            new InterruptModerationHelper.AdapterValue("{ABC-123}", "1"),
            new InterruptModerationHelper.AdapterValue("{DEF-456}", "0")
        });

        Assert.Contains("InterfaceGuid -eq '{ABC-123}'", cmd);
        Assert.Contains("InterfaceGuid -eq '{DEF-456}'", cmd);
        Assert.Contains("-RegistryValue '1' -NoRestart", cmd);
        Assert.Contains("-RegistryValue '0' -NoRestart", cmd);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(cmd, @"catch \{ \$fail\+\+").Count);
    }

    [Fact]
    public void BuildRestoreCommand_シングルクォートをエスケープする()
    {
        var cmd = InterruptModerationHelper.BuildRestoreCommand(new[]
        {
            new InterruptModerationHelper.AdapterValue("{O'Brien}", "1")
        });
        Assert.Contains("{O''Brien}", cmd);
    }

    // ---------- TryParseRead（FlexibleListConverter 再利用＝PS 5.1 単一要素展開吸収） ----------

    [Fact]
    public void TryParseRead_複数要素の配列を正しく読む()
    {
        var json = "{\"Adapters\":[{\"Guid\":\"{AAA}\",\"Value\":\"1\"},{\"Guid\":\"{BBB}\",\"Value\":\"0\"}]}";
        var result = InterruptModerationHelper.TryParseRead(json);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Equal("{AAA}", result[0].Guid);
        Assert.Equal("1", result[0].Value);
    }

    [Fact]
    public void TryParseRead_PS51単一要素展開スカラーを配列として読む()
    {
        // PS 5.1 の ConvertTo-Json は要素数1の配列をスカラーオブジェクトへ展開する（NicPowerHelper と同じ罠）。
        var json = "{\"Adapters\":{\"Guid\":\"{ONLY}\",\"Value\":\"1\"}}";
        var result = InterruptModerationHelper.TryParseRead(json);

        Assert.NotNull(result);
        Assert.Single(result!);
        Assert.Equal("{ONLY}", result![0].Guid);
    }

    [Fact]
    public void TryParseRead_対象アダプタ0件は空リスト()
    {
        var json = "{\"Adapters\":[]}";
        var result = InterruptModerationHelper.TryParseRead(json);
        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public void TryParseRead_破損JSONはnull()
    {
        Assert.Null(InterruptModerationHelper.TryParseRead("not json at all"));
        Assert.Null(InterruptModerationHelper.TryParseRead(""));
        Assert.Null(InterruptModerationHelper.TryParseRead(null));
    }

    [Fact]
    public void TryParseRead_CLIXML等の前後ノイズがあってもJSON境界を抽出する()
    {
        var json = "some progress noise {\"Adapters\":[{\"Guid\":\"{X}\",\"Value\":\"0\"}]} trailing noise";
        var result = InterruptModerationHelper.TryParseRead(json);
        Assert.NotNull(result);
        Assert.Single(result!);
    }

    // ---------- AllDisabled ----------

    [Fact]
    public void AllDisabled_全アダプタが0ならtrue()
    {
        var adapters = new List<InterruptModerationHelper.AdapterValue>
        {
            new("{A}", "0"), new("{B}", "00000000")
        };
        Assert.True(InterruptModerationHelper.AllDisabled(adapters));
    }

    [Fact]
    public void AllDisabled_一部が1ならfalse()
    {
        var adapters = new List<InterruptModerationHelper.AdapterValue>
        {
            new("{A}", "0"), new("{B}", "1")
        };
        Assert.False(InterruptModerationHelper.AllDisabled(adapters));
    }

    [Fact]
    public void AllDisabled_対象0件は既定扱いでfalse()
    {
        Assert.False(InterruptModerationHelper.AllDisabled(new List<InterruptModerationHelper.AdapterValue>()));
    }

    // ---------- ParseCounts ----------

    [Fact]
    public void ParseCounts_正常な結果行を読む()
    {
        var counts = InterruptModerationHelper.ParseCounts("WINKNOBS_IM_RESULT ok=2 fail=1");
        Assert.Equal((2, 1), counts);
    }

    [Fact]
    public void ParseCounts_マーカーが無ければnull()
    {
        Assert.Null(InterruptModerationHelper.ParseCounts("何かの出力"));
        Assert.Null(InterruptModerationHelper.ParseCounts(null));
    }
}
