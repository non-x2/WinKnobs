using System.Collections.Generic;
using System.Text.Json;
using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// v0.1.19 Step2 で追加した record（<see cref="RdpStatus"/> / <see cref="RemoteDesktopBackup"/>）の
/// I/O を伴わない振る舞いテスト。実行系（PowerShell/レジストリ/FW）の検証は VM 実機の領域。
///
/// ここで固めるのは：
///   - 退避 record の JSON ラウンドトリップ（保存→読込で値が壊れない＝復元の前提）
///   - RdpStatus の算出プロパティが Helper 純粋関数へ正しく委譲されること（表示の一貫性）
/// </summary>
public class RemoteDesktopServiceModelsTests
{
    // ---- RemoteDesktopBackup：退避 JSON ラウンドトリップ ----

    [Fact]
    public void RemoteDesktopBackup_round_trips_through_json()
    {
        var original = new RemoteDesktopBackup(
            FDenyExisted: true, FDenyValue: 1,
            FwTcpWasEnabled: false, FwUdpWasEnabled: true,
            NlaExisted: true, NlaValue: 0);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<RemoteDesktopBackup>(json);

        Assert.Equal(original, restored); // record の値等価
    }

    [Fact]
    public void RemoteDesktopBackup_preserves_absent_keys()
    {
        // 「元々キーが無かった」（Existed=false）が保存・復元で保たれること
        // （復元時に「削除して既定へ」を選ぶ分岐の前提）。
        var original = new RemoteDesktopBackup(
            FDenyExisted: false, FDenyValue: 0,
            FwTcpWasEnabled: false, FwUdpWasEnabled: false,
            NlaExisted: false, NlaValue: 0);

        var restored = JsonSerializer.Deserialize<RemoteDesktopBackup>(
            JsonSerializer.Serialize(original));

        Assert.NotNull(restored);
        Assert.False(restored!.FDenyExisted);
        Assert.False(restored.NlaExisted);
    }

    // ---- RdpStatus：算出プロパティの委譲 ----

    private static RdpStatus MakeStatus(int fDeny, bool fwTcp, int? nla) =>
        new(
            FDeny: fDeny, FDenyExisted: true,
            FwTcpEnabled: fwTcp, FwUdpEnabled: false,
            Nla: nla, Port: 3389,
            Edition: "Professional", IsHostCapable: true,
            IsPolicyManaged: false, IsCurrentSessionRemote: false,
            LanIPv4: new List<string>(), UserName: "tester");

    [Theory]
    [InlineData(0, true, true)]    // fDeny=許可 ＋ FW 開 → 有効
    [InlineData(1, true, false)]   // fDeny=拒否 → 無効
    [InlineData(0, false, false)]  // FW 閉 → 片肺で到達不可
    public void IsEnabled_delegates_to_helper(int fDeny, bool fwTcp, bool expected)
        => Assert.Equal(expected, MakeStatus(fDeny, fwTcp, 1).IsEnabled);

    [Fact]
    public void StateLabel_reflects_enabled_with_nla_required()
    {
        var label = MakeStatus(0, true, 1).StateLabel;
        Assert.Contains("有効", label);
        Assert.Contains("NLA：必須", label);
    }

    [Fact]
    public void StateLabel_marks_nla_unknown_when_null()
    {
        var label = MakeStatus(1, false, null).StateLabel;
        Assert.Contains("NLA：不明", label);
    }
}
