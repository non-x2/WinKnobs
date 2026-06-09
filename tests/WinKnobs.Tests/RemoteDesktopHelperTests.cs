using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// RemoteDesktopHelper（v0.1.18 リモートデスクトップ無効化・Phase 4）の純粋関数テスト。
/// 実行系（PowerShell/レジストリ/FW）は VM 実機検証の領域。ここは文字列生成・分類・判定のみ。
/// </summary>
public class RemoteDesktopHelperTests
{
    // ---- BuildDisableCommand（OFF コマンド組み立て）----

    [Fact]
    public void BuildDisableCommand_disables_both_firewall_rules_then_sets_fdeny_1()
    {
        var cmd = RemoteDesktopHelper.BuildDisableCommand();

        // FW 規則名（安定 Name・言語非依存）が両方入る
        Assert.Contains("RemoteDesktop-UserMode-In-TCP", cmd);
        Assert.Contains("RemoteDesktop-UserMode-In-UDP", cmd);
        Assert.Contains("Disable-NetFirewallRule", cmd);
        // fDeny を 1（拒否）に
        Assert.Contains("fDenyTSConnections", cmd);
        Assert.Contains("-Value 1", cmd);
        Assert.Contains(@"HKLM:\System\CurrentControlSet\Control\Terminal Server", cmd);
        // 非終了エラーも terminating 化（ExitCode!=0 保証）
        Assert.Contains("-ErrorAction Stop", cmd);
    }

    [Fact]
    public void BuildDisableCommand_orders_firewall_before_fdeny_external_to_internal()
    {
        var cmd = RemoteDesktopHelper.BuildDisableCommand();
        var fwIndex = cmd.IndexOf("Disable-NetFirewallRule", System.StringComparison.Ordinal);
        var fdenyIndex = cmd.IndexOf("fDenyTSConnections", System.StringComparison.Ordinal);

        // 外→内：FW を先に閉じる → fDeny。fail-closed の前提。
        Assert.True(fwIndex >= 0 && fdenyIndex >= 0);
        Assert.True(fwIndex < fdenyIndex, "FW 無効化は fDeny 設定より前に来るべき（外→内・締め出し回避）");
    }

    // ---- Classify（exitCode + 出力 → Outcome）----

    [Fact]
    public void Classify_exit0_is_success()
        => Assert.Equal(RemoteDesktopOutcome.Success, RemoteDesktopHelper.Classify(0, "なんでも"));

    [Theory]
    [InlineData("Access is denied.")]
    [InlineData("要求された操作には管理者特権が必要です")]      // netsh/PS 昇格不足の日本語
    [InlineData("UnauthorizedAccessException")]
    [InlineData("This command requires elevation.")]
    [InlineData("PrivilegeNotHeldException")]
    public void Classify_exitNonZero_with_permission_keyword_is_AccessDenied(string output)
        => Assert.Equal(RemoteDesktopOutcome.AccessDenied, RemoteDesktopHelper.Classify(1, output));

    [Theory]
    [InlineData("RemoteDesktop-UserMode-In-TCP rule not found")]
    [InlineData("some generic failure")]
    [InlineData("")]
    [InlineData(null)]
    public void Classify_exitNonZero_without_permission_keyword_is_Failed(string? output)
        => Assert.Equal(RemoteDesktopOutcome.Failed, RemoteDesktopHelper.Classify(1, output));

    // ---- IsRdpHostCapableEdition（Home 判定・ホワイトリスト）----

    [Theory]
    [InlineData("Professional")]
    [InlineData("ProfessionalN")]
    [InlineData("ProfessionalWorkstation")]
    [InlineData("ProfessionalEducation")]
    [InlineData("Enterprise")]
    [InlineData("EnterpriseN")]
    [InlineData("EnterpriseS")]
    [InlineData("Education")]
    [InlineData("EducationN")]
    [InlineData("professional")]   // 大文字小文字非依存
    public void IsRdpHostCapableEdition_true_for_pro_enterprise_education(string editionId)
        => Assert.True(RemoteDesktopHelper.IsRdpHostCapableEdition(editionId));

    [Theory]
    [InlineData("Core")]                 // Home
    [InlineData("CoreSingleLanguage")]   // Home Single Language
    [InlineData("CoreCountrySpecific")]
    [InlineData("CoreN")]
    [InlineData("Starter")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("SomethingUnknown")]     // 未知は安全側 false
    // plan-validator（2026-06-06）：サーバ/IoT/Cloud は本来 RDP 可だが接頭辞不一致で false＝過剰グレー（安全側・実害小）
    [InlineData("ServerStandard")]
    [InlineData("ServerDatacenter")]
    [InlineData("IoTEnterprise")]
    [InlineData("CloudEdition")]         // Windows 11 SE 等
    public void IsRdpHostCapableEdition_false_for_home_unknown_empty(string? editionId)
        => Assert.False(RemoteDesktopHelper.IsRdpHostCapableEdition(editionId));

    // ============================================================
    // v0.1.19 ON（有効化）用 純粋関数テスト
    // ============================================================

    // ---- BuildSetDwordCommand（fDeny / NLA 共用の単一ステップ）----

    [Fact]
    public void BuildSetDwordCommand_sets_fdeny_to_0_for_enable()
    {
        var cmd = RemoteDesktopHelper.BuildSetDwordCommand(
            RemoteDesktopHelper.TerminalServerKeyPath, RemoteDesktopHelper.FDenyValueName, 0);
        Assert.Contains("Set-ItemProperty", cmd);
        Assert.Contains(@"HKLM:\System\CurrentControlSet\Control\Terminal Server", cmd);
        Assert.Contains("fDenyTSConnections", cmd);
        Assert.Contains("-Value 0", cmd);
        Assert.Contains("-Type DWord", cmd);
        Assert.Contains("-ErrorAction Stop", cmd);
    }

    [Fact]
    public void BuildSetDwordCommand_sets_nla_to_1()
    {
        var cmd = RemoteDesktopHelper.BuildSetDwordCommand(
            RemoteDesktopHelper.RdpTcpKeyPath, RemoteDesktopHelper.NlaValueName, 1);
        Assert.Contains("UserAuthentication", cmd);
        Assert.Contains(@"WinStations\RDP-Tcp", cmd);
        Assert.Contains("-Value 1", cmd);
        Assert.Contains("-ErrorAction Stop", cmd);
    }

    // ---- BuildFirewallCommand（Enable/Disable・規則名指定）----

    [Fact]
    public void BuildFirewallCommand_enable_uses_both_rules()
    {
        var cmd = RemoteDesktopHelper.BuildFirewallCommand(enable: true);
        Assert.Contains("Enable-NetFirewallRule", cmd);
        Assert.Contains("RemoteDesktop-UserMode-In-TCP", cmd);
        Assert.Contains("RemoteDesktop-UserMode-In-UDP", cmd);
        Assert.Contains("-ErrorAction Stop", cmd);
    }

    [Fact]
    public void BuildFirewallCommand_disable_single_rule_for_rollback()
    {
        // ロールバックでは「開けた規則だけ」を個別に閉じる
        var cmd = RemoteDesktopHelper.BuildFirewallCommand(
            enable: false, new[] { "RemoteDesktop-UserMode-In-TCP" });
        Assert.Contains("Disable-NetFirewallRule", cmd);
        Assert.Contains("RemoteDesktop-UserMode-In-TCP", cmd);
        Assert.DoesNotContain("In-UDP", cmd);
    }

    // ---- BuildReadFirewallCommand ----

    [Fact]
    public void BuildReadFirewallCommand_reads_enabled_property()
    {
        var cmd = RemoteDesktopHelper.BuildReadFirewallCommand("RemoteDesktop-UserMode-In-TCP");
        Assert.Contains("Get-NetFirewallRule", cmd);
        Assert.Contains("RemoteDesktop-UserMode-In-TCP", cmd);
        Assert.Contains(".Enabled", cmd);
    }

    // ---- ParseFirewallEnabled ----

    [Theory]
    [InlineData("True")]
    [InlineData("true")]
    [InlineData("Enabled")]
    [InlineData("Yes")]
    [InlineData("1")]
    [InlineData("はい")]
    public void ParseFirewallEnabled_true_cases(string output)
        => Assert.True(RemoteDesktopHelper.ParseFirewallEnabled(output));

    [Theory]
    [InlineData("False")]
    [InlineData("false")]
    [InlineData("Disabled")]
    [InlineData("No")]
    [InlineData("2")]
    [InlineData("いいえ")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("garbage")]   // 未知は安全側 false
    public void ParseFirewallEnabled_false_cases(string? output)
        => Assert.False(RemoteDesktopHelper.ParseFirewallEnabled(output));

    // ---- IsRdpEnabled（合成判定）----

    [Theory]
    [InlineData(0, true, true)]    // fDeny=許可 ＋ FW 開 → 有効
    [InlineData(1, true, false)]   // fDeny=拒否 → 無効
    [InlineData(0, false, false)]  // FW 閉 → 到達不可（片肺）
    [InlineData(1, false, false)]
    public void IsRdpEnabled_requires_fdeny0_and_fw_open(int fDeny, bool fwTcp, bool expected)
        => Assert.Equal(expected, RemoteDesktopHelper.IsRdpEnabled(fDeny, fwTcp));

    // ---- DescribeRdpState（ラベル＋NLA）----

    [Fact]
    public void DescribeRdpState_enabled_with_nla_required()
    {
        var s = RemoteDesktopHelper.DescribeRdpState(0, true, 1);
        Assert.Contains("有効", s);
        Assert.Contains("NLA：必須", s);
    }

    [Fact]
    public void DescribeRdpState_disabled()
    {
        var s = RemoteDesktopHelper.DescribeRdpState(1, false, 0);
        Assert.Contains("無効", s);
        Assert.Contains("NLA：不要", s);
    }

    [Fact]
    public void DescribeRdpState_partial_is_flagged()
    {
        // fDeny=許可 なのに FW 閉＝片肺
        var s = RemoteDesktopHelper.DescribeRdpState(0, false, 1);
        Assert.Contains("一部だけ有効", s);
    }

    [Fact]
    public void DescribeRdpState_nla_unknown_when_null()
    {
        var s = RemoteDesktopHelper.DescribeRdpState(1, false, null);
        Assert.Contains("NLA：不明", s);
    }

    // ---- BuildRemoveValueCommand（S-1：ロールバックの削除を PowerShell 経路で失敗検知）----

    [Fact]
    public void BuildRemoveValueCommand_removes_then_throws_if_still_present()
    {
        var cmd = RemoteDesktopHelper.BuildRemoveValueCommand(
            RemoteDesktopHelper.TerminalServerKeyPath, RemoteDesktopHelper.FDenyValueName);

        // 削除（SilentlyContinue で冪等）→ 残っていれば throw（ExitCode!=0 で失敗を確実に観測）
        Assert.Contains("Remove-ItemProperty", cmd);
        Assert.Contains($"-LiteralPath '{RemoteDesktopHelper.TerminalServerKeyPath}'", cmd);
        Assert.Contains($"-Name {RemoteDesktopHelper.FDenyValueName}", cmd);
        Assert.Contains("SilentlyContinue", cmd);
        Assert.Contains("Get-ItemProperty", cmd);
        Assert.Contains("throw", cmd);
    }

    // ---- TryParseBackup（E-1：退避 JSON 厳密パース・破損は null）----

    [Fact]
    public void TryParseBackup_roundtrips_a_valid_backup()
    {
        var original = new RemoteDesktopBackup(
            FDenyExisted: true, FDenyValue: 0,
            FwTcpWasEnabled: false, FwUdpWasEnabled: true,
            NlaExisted: true, NlaValue: 1);
        var json = System.Text.Json.JsonSerializer.Serialize(original);

        var parsed = RemoteDesktopHelper.TryParseBackup(json);

        Assert.NotNull(parsed);
        Assert.Equal(original, parsed); // record の値等価
    }

    [Theory]
    [InlineData("{}")]                              // 空オブジェクト＝全フィールド欠落
    [InlineData("{\"FDenyExisted\":true}")]         // 一部だけ＝残り欠落
    [InlineData("not json at all")]                 // malformed
    [InlineData("{ broken")]                        // malformed
    [InlineData("")]                                // 空
    [InlineData("   ")]                             // 空白のみ
    [InlineData(null)]                              // null
    public void TryParseBackup_returns_null_for_corrupt_or_partial(string? json)
        => Assert.Null(RemoteDesktopHelper.TryParseBackup(json));

    [Fact]
    public void TryParseBackup_null_when_a_single_field_is_missing()
    {
        // FDenyValue だけ欠落（他は揃っている）→ 破損扱いで null（全 default の record にして誤認しない）
        var json =
            "{\"FDenyExisted\":true,\"FwTcpWasEnabled\":false,\"FwUdpWasEnabled\":true," +
            "\"NlaExisted\":true,\"NlaValue\":1}";
        Assert.Null(RemoteDesktopHelper.TryParseBackup(json));
    }
}
