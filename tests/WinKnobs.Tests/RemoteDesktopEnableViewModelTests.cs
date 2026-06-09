using System.Collections.Generic;
using WinKnobs.Services;
using WinKnobs.ViewModels;

namespace WinKnobs.Tests;

/// <summary>
/// v0.1.19 Step3：Red 専用 強確認ダイアログ VM（<see cref="RemoteDesktopEnableViewModel"/>）の振る舞いテスト。
/// VM は I/O を持たず（Init に <see cref="RdpStatus"/> を渡すだけ）、同意可否・文言生成を純粋に検証できる。
/// </summary>
public class RemoteDesktopEnableViewModelTests
{
    private static RdpStatus Status(
        int fDeny = 1, bool fwTcp = false, int? nla = 1, int port = 3389,
        bool remote = false, bool policy = false,
        IReadOnlyList<string>? lan = null, string user = "tester") =>
        new(
            FDeny: fDeny, FDenyExisted: true,
            FwTcpEnabled: fwTcp, FwUdpEnabled: false,
            Nla: nla, Port: port,
            Edition: "Professional", IsHostCapable: true,
            IsPolicyManaged: policy, IsCurrentSessionRemote: remote,
            LanIPv4: lan ?? new List<string> { "192.168.1.10" }, UserName: user);

    private static RemoteDesktopEnableViewModel Vm(RdpStatus s)
    {
        var vm = new RemoteDesktopEnableViewModel();
        vm.Init(s);
        return vm;
    }

    // ---- 明示同意ジェスチャ（問2-A）----

    [Fact]
    public void CanEnable_is_false_until_risk_understood()
    {
        var vm = Vm(Status());
        Assert.False(vm.CanEnable);

        vm.IsRiskUnderstood = true;
        Assert.True(vm.CanEnable);
    }

    [Fact]
    public void CanEnable_raises_property_changed_when_check_toggles()
    {
        var vm = Vm(Status());
        var raised = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.CanEnable)) raised = true; };

        vm.IsRiskUnderstood = true;

        Assert.True(raised); // チェック変更で [有効化] 活性が連動更新される
    }

    [Fact]
    public void EnableCommand_sets_dialog_result_true()
    {
        var vm = Vm(Status());
        vm.EnableCommand.Execute(null);
        Assert.True(vm.DialogResult);
    }

    [Fact]
    public void CancelCommand_sets_dialog_result_false()
    {
        var vm = Vm(Status());
        vm.CancelCommand.Execute(null);
        Assert.False(vm.DialogResult);
    }

    // ---- 文言生成（コンテキスト提示・§4-3）----

    [Fact]
    public void CurrentStateText_includes_state_and_nla()
    {
        var vm = Vm(Status(fDeny: 0, fwTcp: true, nla: 1)); // 有効・NLA 必須
        Assert.Contains("有効", vm.CurrentStateText);
        Assert.Contains("NLA：必須", vm.CurrentStateText);
    }

    [Fact]
    public void LanIpText_joins_multiple_addresses()
    {
        var vm = Vm(Status(lan: new List<string> { "192.168.1.10", "10.0.0.5" }));
        Assert.Contains("192.168.1.10", vm.LanIpText);
        Assert.Contains("10.0.0.5", vm.LanIpText);
    }

    [Fact]
    public void PortText_default_3389_not_flagged()
    {
        var vm = Vm(Status(port: 3389));
        Assert.False(vm.IsPortChanged);
        Assert.DoesNotContain("変更されています", vm.PortText);
    }

    [Fact]
    public void PortText_non_default_is_flagged()
    {
        var vm = Vm(Status(port: 13389));
        Assert.True(vm.IsPortChanged);
        Assert.Contains("13389", vm.PortText);
        Assert.Contains("変更されています", vm.PortText);
    }

    [Fact]
    public void RemoteSession_warning_visibility_follows_status()
    {
        Assert.True(Vm(Status(remote: true)).IsCurrentSessionRemote);
        Assert.False(Vm(Status(remote: false)).IsCurrentSessionRemote);
    }

    [Fact]
    public void PolicyNote_visibility_follows_status()
    {
        Assert.True(Vm(Status(policy: true)).IsPolicyManaged);
        Assert.False(Vm(Status(policy: false)).IsPolicyManaged);
    }

    [Fact]
    public void RestoreAssurance_warns_revert_all_does_not_restore()
    {
        // ON は『ぜんぶ戻す』では戻らない旨を明示（plan-validator #2c）
        var vm = Vm(Status());
        Assert.Contains("ぜんぶ戻す", vm.RestoreAssuranceText);
        Assert.Contains("元に戻す", vm.RestoreAssuranceText);
    }
}
