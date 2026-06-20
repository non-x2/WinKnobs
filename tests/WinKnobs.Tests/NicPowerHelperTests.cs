using System.Collections.Generic;
using System.Linq;
using WinKnobs.Services;
using Xunit;

namespace WinKnobs.Tests;

/// <summary>
/// v0.2.2 NicPowerHelper（純粋関数）のテスト。
/// 🔧 plan-validator 反映の核心を検証：
///   反映1＝個別集計（ok=N fail=M）／反映2＝ValidValues ガード（一律 0 書込禁止）／
///   反映3＝文字列保持＋数値正規化／反映4＝GUID 識別／エッジ1＝GUID 再解決。
/// </summary>
public class NicPowerHelperTests
{
    // ---------- NormalizeNumeric（🔧反映3：00000000 / 0x0 / 0 を同一視） ----------

    [Theory]
    [InlineData("0", "0")]
    [InlineData("00000000", "0")]
    [InlineData(" 0 ", "0")]
    [InlineData("0x0", "0")]
    [InlineData("1", "1")]
    [InlineData("0x1", "1")]
    [InlineData("256", "256")]
    [InlineData("0x100", "256")]
    [InlineData("", "")]
    [InlineData("auto", "auto")]   // 数値でない値は trim のみ
    public void NormalizeNumeric_数値正規化(string input, string expected)
        => Assert.Equal(expected, NicPowerHelper.NormalizeNumeric(input));

    // ---------- SelectKeywordsToApply（🔧反映2：ValidValues ガード） ----------

    [Fact]
    public void Select_0が有効値かつ現在値が0でない項目だけ選ぶ()
    {
        var snap = Snapshot(
            Prop("*EEE", "1", "0", "1"),                 // ✅ 0 有効・現在1 → 対象
            Prop("ULPMode", "0", "0", "1"),              // ✗ 既に0（no-op）
            Prop("PowerSavingMode", "2", "1", "2"),      // ✗ 0 が有効値でない（意味反転/enum）
            Prop("GreenEthernet", "1"));                 // ✗ ValidValues 空（値域不明・安全側スキップ）

        var selected = NicPowerHelper.SelectKeywordsToApply(snap);

        Assert.Equal(new[] { "*EEE" }, selected.ToArray());
    }

    [Fact]
    public void Select_00000000を0扱いしてno_op判定()
    {
        var snap = Snapshot(Prop("*EEE", "00000000", "0", "1"));
        Assert.Empty(NicPowerHelper.SelectKeywordsToApply(snap));   // ゼロ埋めも0 → 既にOFF
    }

    // ---------- BuildApplyCommand（🔧反映1：個別 try/catch・集計／エッジ1：GUID 再解決） ----------

    [Fact]
    public void Apply_GUID再解決と個別集計と電源管理ロックを含む()
    {
        var cmd = NicPowerHelper.BuildApplyCommand(
            "{ABC-123}", new[] { "*EEE", "ULPMode" }, lockPowerManagement: true);

        // GUID で再解決（Name でなく InterfaceGuid・🔧反映4＋エッジ1）
        Assert.Contains("InterfaceGuid -eq '{ABC-123}'", cmd);
        Assert.Contains(NicPowerHelper.AdapterNotFoundMarker, cmd);
        // 各 Set- が個別 try/catch で集計される（🔧反映1）
        Assert.Contains("-RegistryKeyword '*EEE' -RegistryValue '0' -NoRestart", cmd);
        Assert.Contains("-RegistryKeyword 'ULPMode' -RegistryValue '0' -NoRestart", cmd);
        // 各 Set- ＋電源管理＝計3つの個別 try/catch（$fail++ 集計）
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(cmd, @"catch \{ \$fail\+\+").Count);
        // 電源管理ロック＝広範な Disable- でなく Get→プロパティ書換→パイプ Set（2026-06-20 修正：直接パラメーターは無い）
        Assert.Contains("Get-NetAdapterPowerManagement -Name $a.Name", cmd);
        Assert.Contains("$pm.AllowComputerToTurnOffDevice='Disabled'", cmd);
        Assert.Contains("$pm | Set-NetAdapterPowerManagement", cmd);
        Assert.DoesNotContain("Disable-NetAdapterPowerManagement", cmd);
        // 構造化結果＋件数で exit を分岐
        Assert.Contains(NicPowerHelper.ResultMarker, cmd);
    }

    [Fact]
    public void Apply_電源管理ロック不要なら電源管理ステップを出さない()
    {
        var cmd = NicPowerHelper.BuildApplyCommand("{G}", new[] { "*EEE" }, lockPowerManagement: false);
        Assert.DoesNotContain("Set-NetAdapterPowerManagement", cmd);
    }

    // ---------- BuildRestoreCommand（🔧反映3：保存した文字列値を書き戻す） ----------

    [Fact]
    public void Restore_保存値の書き戻しと電源管理の原状復帰()
    {
        var snap = Snapshot(
            Prop("*EEE", "1", "0", "1"),
            Prop("ULPMode", "1", "0", "1"));
        var withAllow = snap with { AllowComputerToTurnOffDevice = "Enabled" };

        var cmd = NicPowerHelper.BuildRestoreCommand(withAllow);

        Assert.Contains("-RegistryKeyword '*EEE' -RegistryValue '1' -NoRestart", cmd);
        Assert.Contains("-RegistryKeyword 'ULPMode' -RegistryValue '1' -NoRestart", cmd);
        // 電源管理は保存した Enabled へ戻す（Get→プロパティ書換→パイプ Set）
        Assert.Contains("$pm.AllowComputerToTurnOffDevice='Enabled'", cmd);
        Assert.Contains("$pm | Set-NetAdapterPowerManagement", cmd);
    }

    [Fact]
    public void Restore_値域に無い原状値は書き戻さない()
    {
        // 他ツール改変等で現在値が値域外 → 復元で書かない（破損防衛）
        var snap = Snapshot(Prop("*EEE", "9", "0", "1"));
        var cmd = NicPowerHelper.BuildRestoreCommand(snap);
        Assert.DoesNotContain("-RegistryKeyword '*EEE'", cmd);
    }

    // ---------- ParseApplyCounts / Classify ----------

    [Fact]
    public void ParseApplyCounts_結果行から件数を読む()
    {
        var counts = NicPowerHelper.ParseApplyCounts("WINKNOBS_RESULT ok=3 fail=1\nsome error");
        Assert.Equal((3, 1), counts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("no marker here")]
    public void ParseApplyCounts_該当なしはnull(string? output)
        => Assert.Null(NicPowerHelper.ParseApplyCounts(output));

    [Fact]
    public void Classify_全成功はSuccess()
        => Assert.Equal(NicPowerOutcome.Success,
            NicPowerHelper.Classify(0, "WINKNOBS_RESULT ok=2 fail=0"));

    [Fact]
    public void Classify_アダプタ消失はAdapterNotFound()
        => Assert.Equal(NicPowerOutcome.AdapterNotFound,
            NicPowerHelper.Classify(NicPowerHelper.ExitAdapterNotFound, "WINKNOBS_ADAPTER_NOT_FOUND"));

    [Fact]
    public void Classify_一部成功一部失敗はPartiallyApplied()
        => Assert.Equal(NicPowerOutcome.PartiallyApplied,
            NicPowerHelper.Classify(NicPowerHelper.ExitPartialOrFail, "WINKNOBS_RESULT ok=2 fail=1\nfoo"));

    [Fact]
    public void Classify_権限不足はAccessDenied()
        => Assert.Equal(NicPowerOutcome.AccessDenied,
            NicPowerHelper.Classify(NicPowerHelper.ExitPartialOrFail,
                "WINKNOBS_RESULT ok=0 fail=1\nAccess is denied"));

    [Fact]
    public void Classify_全失敗はFailed()
        => Assert.Equal(NicPowerOutcome.Failed,
            NicPowerHelper.Classify(NicPowerHelper.ExitPartialOrFail, "WINKNOBS_RESULT ok=0 fail=2\nsome error"));

    // 重要②（クロスレビュー 2026-06-14）：一部成功時はエラー文に権限語（管理者/administrator/elevation 等）が
    // 偶発混入しても AccessDenied でなく PartiallyApplied を維持する（「全部ダメ・要管理者」の誤誘導を防ぐ）。
    // 修正前はこのケースが AccessDenied に誤分類された（分岐順序：AccessDenied → PartiallyApplied）。
    [Fact]
    public void Classify_一部成功はエラー文に権限語を含んでもPartiallyApplied()
        => Assert.Equal(NicPowerOutcome.PartiallyApplied,
            NicPowerHelper.Classify(NicPowerHelper.ExitPartialOrFail,
                "WINKNOBS_RESULT ok=2 fail=1\nThe requested operation requires elevation. Access is denied. 管理者"));

    // ---------- TryParseSnapshot（厳密・PS 5.1 単一要素展開の吸収） ----------

    [Fact]
    public void Parse_省電力キーワードのみ抽出し非該当は捨てる()
    {
        // Advanced 配列・RegistryValue は配列、ValidRegistryValues も配列の通常ケース。
        var json = """
        {"Name":"イーサネット","Guid":"{G1}","AllowComputerToTurnOffDevice":"Enabled","Advanced":[
          {"RegistryKeyword":"*EEE","RegistryValue":["1"],"ValidRegistryValues":["0","1"],"DisplayName":"EEE"},
          {"RegistryKeyword":"*SpeedDuplex","RegistryValue":["0"],"ValidRegistryValues":["0","1"],"DisplayName":"Speed"}
        ]}
        """;
        var snap = NicPowerHelper.TryParseSnapshot(json);

        Assert.NotNull(snap);
        Assert.Equal("イーサネット", snap!.AdapterName);
        Assert.Equal("{G1}", snap.Guid);
        Assert.Equal("Enabled", snap.AllowComputerToTurnOffDevice);
        // *SpeedDuplex は省電力キーワードでない → 除外
        var p = Assert.Single(snap.PowerSavingProps);
        Assert.Equal("*EEE", p.Keyword);
        Assert.Equal("1", p.CurrentValue);
        Assert.Equal(new[] { "0", "1" }, p.ValidValues.ToArray());
    }

    [Fact]
    public void Parse_PS51の単一要素展開_スカラーでも配列でも読める()
    {
        // Advanced がオブジェクト単体（配列でない）＋ RegistryValue がスカラー文字列（PS 5.1 展開後）。
        var json = """
        {"Name":"Eth","Guid":"{G2}","AllowComputerToTurnOffDevice":"Disabled","Advanced":
          {"RegistryKeyword":"ULPMode","RegistryValue":"1","ValidRegistryValues":["0","1"],"DisplayName":"ULP"}
        }
        """;
        var snap = NicPowerHelper.TryParseSnapshot(json);

        Assert.NotNull(snap);
        var p = Assert.Single(snap!.PowerSavingProps);
        Assert.Equal("ULPMode", p.Keyword);
        Assert.Equal("1", p.CurrentValue);
    }

    [Fact]
    public void Parse_数値のAllowを正規化()
    {
        var json = """{"Name":"Eth","Guid":"{G}","AllowComputerToTurnOffDevice":"1","Advanced":[]}""";
        var snap = NicPowerHelper.TryParseSnapshot(json);
        Assert.Equal("Enabled", snap!.AllowComputerToTurnOffDevice);
    }

    [Fact]
    public void Parse_後ろにCLIXML進捗が連結されても本物JSONだけ抽出して読める()
    {
        // 2026-06-20 実機バグ：昇格セッションでは Get-NetAdapter が CLIXML 形式の進捗(中括弧を含む)を stderr に出す。
        // combined=stdout+stderr で連結されるため、ExtractJsonObject はブレース・バランスで本物JSONだけ取る必要がある。
        var json =
            """{"Name":"Eth","Guid":"{G4}","AllowComputerToTurnOffDevice":"Enabled","Advanced":[]}""" +
            "\n#< CLIXML\n<Objs><Obj><MS><AV>Where-Object { $_.InterfaceGuid -eq '...' }</AV></MS></Obj></Objs>";
        var snap = NicPowerHelper.TryParseSnapshot(json);
        Assert.NotNull(snap);
        Assert.Equal("Eth", snap!.AdapterName);
        Assert.Equal("{G4}", snap.Guid);
    }

    [Fact]
    public void Parse_PS51のICollection特殊形式_valueプロパティを配列として読める()
    {
        // 2026-06-20 実機バグ：昇格セッションの ConvertTo-Json で ValidRegistryValues が
        // {"value":[...],"Count":N} のオブジェクトで出るケースを吸収する。
        var json = """
        {"Name":"Eth","Guid":"{G3}","AllowComputerToTurnOffDevice":"Enabled","Advanced":[
          {"RegistryKeyword":"EEELinkAdvertisement","RegistryValue":["0"],
           "ValidRegistryValues":{"value":["0","1"],"Count":2},"DisplayName":"EEE"}
        ]}
        """;
        var snap = NicPowerHelper.TryParseSnapshot(json);
        Assert.NotNull(snap);
        var p = Assert.Single(snap!.PowerSavingProps);
        Assert.Equal("EEELinkAdvertisement", p.Keyword);
        Assert.Equal(new[] { "0", "1" }, p.ValidValues.ToArray());
    }

    [Fact]
    public void Parse_stderr警告が前後に混じってもJSON抽出して読める()
    {
        // RunPowerShell は stdout+stderr 結合を渡す＝警告等が前後に付いても本体 JSON を拾えること（2026-06-20 実機バグ堅牢化）。
        var json =
            "WARNING: cmdlet emitted a warning\r\n" +
            """{"Name":"イーサネット","Guid":"{C5094D7A}","AllowComputerToTurnOffDevice":"Enabled","Advanced":[{"RegistryKeyword":"ULPMode","RegistryValue":["1"],"ValidRegistryValues":["0","1"],"DisplayName":"ULP"}]}""" +
            "\r\nVERBOSE: done";
        var snap = NicPowerHelper.TryParseSnapshot(json);
        Assert.NotNull(snap);
        Assert.Equal("イーサネット", snap!.AdapterName);
        Assert.Equal("{C5094D7A}", snap.Guid);
        Assert.Equal("Enabled", snap.AllowComputerToTurnOffDevice);
        Assert.Single(snap.PowerSavingProps);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("WINKNOBS_ADAPTER_NOT_FOUND")]
    [InlineData("{}")]                                   // Name/Guid 欠落
    [InlineData("""{"Name":"x"}""")]                     // Guid 欠落
    public void Parse_想定外はnull(string? json)
        => Assert.Null(NicPowerHelper.TryParseSnapshot(json));

    // ---------- 退避ラウンドトリップ ----------

    [Fact]
    public void Backup_シリアライズ往復で同値()
    {
        var snap = Snapshot(Prop("*EEE", "1", "0", "1"), Prop("ULPMode", "0", "0", "1"))
            with { AllowComputerToTurnOffDevice = "Enabled" };

        var json = NicPowerHelper.SerializeBackup(snap);
        var back = NicPowerHelper.TryParseBackup(json);

        Assert.NotNull(back);
        Assert.Equal(snap.AdapterName, back!.AdapterName);
        Assert.Equal(snap.Guid, back.Guid);
        Assert.Equal("Enabled", back.AllowComputerToTurnOffDevice);
        Assert.Equal(2, back.PowerSavingProps.Count);
        Assert.Equal("*EEE", back.PowerSavingProps[0].Keyword);
        Assert.Equal("1", back.PowerSavingProps[0].CurrentValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("broken")]
    public void Backup_破損はnull(string? json)
        => Assert.Null(NicPowerHelper.TryParseBackup(json));

    // ---------- EscapePsSingleQuote（注入防止） ----------

    [Fact]
    public void Escape_シングルクォートを二重化()
        => Assert.Equal("a''b", NicPowerHelper.EscapePsSingleQuote("a'b"));

    // ---------- DescribeStatus（2 軸独立・🔧反映エッジ2） ----------

    [Fact]
    public void Status_省電力件数と電源オフ許可を別々に表示()
    {
        var snap = Snapshot(Prop("*EEE", "1", "0", "1"), Prop("ULPMode", "0", "0", "1"))
            with { AllowComputerToTurnOffDevice = "Enabled" };
        var label = NicPowerHelper.DescribeStatus(snap);
        Assert.Contains("2 件中 1 件", label);
        Assert.Contains("電源オフ許可：ON", label);
    }

    [Fact]
    public void Status_省電力項目ゼロでも電源オフ許可は表示()
    {
        var snap = Snapshot() with { AllowComputerToTurnOffDevice = "Disabled" };
        var label = NicPowerHelper.DescribeStatus(snap);
        Assert.Contains("該当設定なし", label);
        Assert.Contains("電源オフ許可：OFF", label);
    }

    // ---------- ヘルパー ----------

    private static NicPowerHelper.NicPowerSnapshot Snapshot(params NicPowerHelper.NicPowerProperty[] props)
        => new("イーサネット", "{GUID}", props.ToList(), null);

    private static NicPowerHelper.NicPowerProperty Prop(string kw, string current, params string[] valid)
        => new(kw, current, valid.ToList());
}
