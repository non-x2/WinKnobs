using System.Collections.Generic;
using System.Net.NetworkInformation;
using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// NetworkService（v0.1.7 ネットワーク機能）の純粋関数の単体テスト。
/// 物理アダプタ判定 / DNS プロファイル判定 / NameServer パース / IP 検証 / 配信最適化ラベル。
/// netsh・レジストリ I/O を伴う部分は VM 実機検証の領域（plan-validator 重要5）でここでは扱わない。
/// </summary>
public class NetworkServiceTests
{
    // ─── 配信最適化（DODownloadMode）ラベル ───

    [Theory]
    [InlineData(0, "オフ（P2P なし・軽い）")]
    [InlineData(1, "LAN 内のみ")]
    [InlineData(3, "オン（インターネット含む・既定）")]
    [InlineData(99, "ダウンロードを簡素化")]
    [InlineData(7, "モード 7")]
    public void DescribeDeliveryOptimizationMode_maps_known_and_unknown(int mode, string expected)
        => Assert.Equal(expected, NetworkService.DescribeDeliveryOptimizationMode(mode));

    // ─── 物理アダプタ判定（テーブル駆動・plan-validator エッジ1）───

    [Theory]
    // 物理：有線 Up / Wi-Fi Up
    [InlineData(NetworkInterfaceType.Ethernet, OperationalStatus.Up, "Intel(R) Ethernet Connection", true)]
    [InlineData(NetworkInterfaceType.Wireless80211, OperationalStatus.Up, "Intel(R) Wi-Fi 6 AX201", true)]
    // 非物理：Down は除外
    [InlineData(NetworkInterfaceType.Ethernet, OperationalStatus.Down, "Intel(R) Ethernet Connection", false)]
    // 非物理：型違い（Loopback / Tunnel）
    [InlineData(NetworkInterfaceType.Loopback, OperationalStatus.Up, "Software Loopback Interface 1", false)]
    [InlineData(NetworkInterfaceType.Tunnel, OperationalStatus.Up, "Teredo Tunneling Pseudo-Interface", false)]
    // 非物理：説明に仮想/VPN/Hyper-V/WSL マーカー（型は Ethernet でも除外）
    [InlineData(NetworkInterfaceType.Ethernet, OperationalStatus.Up, "Hyper-V Virtual Ethernet Adapter", false)]
    [InlineData(NetworkInterfaceType.Ethernet, OperationalStatus.Up, "vEthernet (WSL)", false)]
    [InlineData(NetworkInterfaceType.Ethernet, OperationalStatus.Up, "TAP-Windows Adapter V9 (VPN)", false)]
    [InlineData(NetworkInterfaceType.Ethernet, OperationalStatus.Up, "VMware Virtual Ethernet Adapter", false)]
    public void IsPhysicalAdapter_table(NetworkInterfaceType type, OperationalStatus status, string desc, bool expected)
        => Assert.Equal(expected, NetworkService.IsPhysicalAdapter(type, status, desc));

    [Fact]
    public void IsPhysicalAdapter_null_description_is_physical_when_type_and_status_ok()
        => Assert.True(NetworkService.IsPhysicalAdapter(NetworkInterfaceType.Ethernet, OperationalStatus.Up, null));

    // ─── DNS プロファイル判定 ───

    [Fact]
    public void DescribeDnsProfile_cloudflare()
        => Assert.Equal("Cloudflare", NetworkService.DescribeDnsProfile(new[] { "1.1.1.1", "1.0.0.1" }));

    [Fact]
    public void DescribeDnsProfile_google()
        => Assert.Equal("Google", NetworkService.DescribeDnsProfile(new[] { "8.8.8.8" }));

    [Fact]
    public void DescribeDnsProfile_empty_is_dhcp()
        => Assert.Equal("自動 (DHCP)", NetworkService.DescribeDnsProfile(System.Array.Empty<string>()));

    [Fact]
    public void DescribeDnsProfile_other_static()
        => Assert.Equal("自動 / その他", NetworkService.DescribeDnsProfile(new[] { "192.168.1.1" }));

    // ─── NameServer パース（退避→復元のラウンドトリップに使う）───

    [Theory]
    [InlineData("1.1.1.1,1.0.0.1", 2)]
    [InlineData("8.8.8.8 8.8.4.4", 2)]
    [InlineData("1.1.1.1", 1)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    [InlineData("   ", 0)]
    public void ParseNameServer_counts(string? raw, int expectedCount)
        => Assert.Equal(expectedCount, NetworkService.ParseNameServer(raw).Length);

    [Fact]
    public void ParseNameServer_values_trimmed()
        => Assert.Equal(new[] { "1.1.1.1", "1.0.0.1" }, NetworkService.ParseNameServer("1.1.1.1, 1.0.0.1"));

    // ─── IP 検証（Phase 7 手入力・plan-validator エッジ2）───

    [Theory]
    [InlineData("1.1.1.1", true)]
    [InlineData("8.8.4.4", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("999.1.1.1", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("1.1.1.1 ", true)]   // 前後空白は trim して許容
    public void IsValidIpAddress_table(string? input, bool expected)
        => Assert.Equal(expected, NetworkService.IsValidIpAddress(input));

    // ─── BuildDnsServers（手入力欄→有効 IP 配列・Phase 7a 詳細層・純粋関数）───

    [Fact]
    public void BuildDnsServers_keeps_valid_and_trims()
        => Assert.Equal(new[] { "1.1.1.1", "1.0.0.1" },
            NetworkService.BuildDnsServers(" 1.1.1.1 ", "1.0.0.1"));

    [Fact]
    public void BuildDnsServers_drops_empty_and_invalid()
        => Assert.Equal(new[] { "8.8.8.8" },
            NetworkService.BuildDnsServers("8.8.8.8", "", "  ", "abc", null));

    [Fact]
    public void BuildDnsServers_all_empty_returns_empty()
        => Assert.Empty(NetworkService.BuildDnsServers("", null, "   "));

    [Fact]
    public void BuildDnsServers_keeps_ipv6()
        => Assert.Equal(new[] { "2606:4700:4700::1111" },
            NetworkService.BuildDnsServers("2606:4700:4700::1111"));

    [Fact]
    public void BuildDnsServers_preserves_order()
        => Assert.Equal(new[] { "8.8.8.8", "1.1.1.1" },
            NetworkService.BuildDnsServers("8.8.8.8", "1.1.1.1"));

    // ─── MergeDnsBackups（アダプタ単位 first-touch-wins・Phase 7c 詳細層・純粋関数）───

    [Fact]
    public void MergeDnsBackups_appends_only_unrecorded()
    {
        var existing = new List<DnsAdapterBackup>
        {
            new("Wi-Fi", "id-A", "1.1.1.1", ""),
        };
        var newTargets = new List<DnsAdapterBackup>
        {
            new("Wi-Fi", "id-A", "8.8.8.8", ""),   // 既収録 → 原状を保持（上書きしない）
            new("Ethernet", "id-B", "", ""),       // 未収録 → 追記
        };

        var merged = NetworkService.MergeDnsBackups(existing, newTargets);

        Assert.Equal(2, merged.Count);
        Assert.Equal("1.1.1.1", merged.Single(b => b.Id == "id-A").Ipv4NameServer);  // first-touch 保持
        Assert.Contains(merged, b => b.Id == "id-B");
    }

    [Fact]
    public void MergeDnsBackups_empty_existing_takes_all()
    {
        var merged = NetworkService.MergeDnsBackups(
            new List<DnsAdapterBackup>(),
            new List<DnsAdapterBackup> { new("Wi-Fi", "id-A", "", ""), new("Eth", "id-B", "", "") });

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void MergeDnsBackups_id_case_insensitive()
    {
        var existing = new List<DnsAdapterBackup> { new("Wi-Fi", "ID-A", "1.1.1.1", "") };

        var merged = NetworkService.MergeDnsBackups(
            existing,
            new List<DnsAdapterBackup> { new("Wi-Fi", "id-a", "8.8.8.8", "") });  // 大小違いは同一扱い

        Assert.Single(merged);
        Assert.Equal("1.1.1.1", merged[0].Ipv4NameServer);  // 既存の原状を保持
    }
}
