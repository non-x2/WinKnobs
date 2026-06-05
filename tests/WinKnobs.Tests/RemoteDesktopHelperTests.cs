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
    public void IsRdpHostCapableEdition_false_for_home_unknown_empty(string? editionId)
        => Assert.False(RemoteDesktopHelper.IsRdpHostCapableEdition(editionId));
}
