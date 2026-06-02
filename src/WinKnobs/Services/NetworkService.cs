using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using Microsoft.Win32;

namespace WinKnobs.Services;

/// <summary>
/// ネットワーク機能のサービス層（v0.1.7・設計ドラフト 4-2）。
/// 読取・判定は .NET（System.Net.NetworkInformation／レジストリ＝ロケール非依存・安全）、
/// 設定変更は netsh に集約（PowerShell 基盤を新設しない＝plan-validator 重要3）。
/// コマンド生成・値判定など純粋部分は static にして xUnit 可能に保つ。
///
/// 実装フェーズ：
///   Phase 3（実装済）：配信最適化（DODownloadMode）。
///   Phase 4（実装済）：DNS プロファイル切替（netsh・物理アダプタ判定・退避/復元）。
///   Phase 6：軽いリセット（flushdns）。
/// </summary>
public class NetworkService
{
    private readonly RegistryService _registry;
    private readonly BackupService _backup;
    private readonly ExternalLauncherService _launcher;

    // ── 配信最適化（Delivery Optimization）──
    private const string DoConfigPath = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config";
    private const string DoPolicyPath = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";
    private const string DoValueName = "DODownloadMode";

    public const string FeatureDeliveryOptimization = "deliveryOptimization";
    public const string FeatureDns = "dns";

    public const int DoModeOff = 0;      // HTTP のみ（P2P 完全オフ・軽い）
    public const int DoModeDefault = 3;  // インターネット含む（Windows 既定）

    // ── DNS プロファイル（IPv4 / IPv6）──
    public static readonly string[] CloudflareV4 = { "1.1.1.1", "1.0.0.1" };
    public static readonly string[] CloudflareV6 = { "2606:4700:4700::1111", "2606:4700:4700::1001" };
    public static readonly string[] GoogleV4 = { "8.8.8.8", "8.8.4.4" };
    public static readonly string[] GoogleV6 = { "2001:4860:4860::8888", "2001:4860:4860::8844" };

    public NetworkService(RegistryService registry, BackupService backup, ExternalLauncherService launcher)
    {
        _registry = registry;
        _backup = backup;
        _launcher = launcher;
    }

    // =================== Phase 3：配信最適化（帯域抑制） ===================

    /// <summary>現在の DODownloadMode。未設定は null（＝Windows 既定 3 とみなす）。</summary>
    public int? GetDeliveryOptimizationMode()
    {
        var v = _registry.ReadValue<object>(DoConfigPath, DoValueName);
        if (v is int i) return i;
        if (v != null && int.TryParse(v.ToString(), out var p)) return p;
        return null;
    }

    /// <summary>配信最適化がグループポリシーで管理されているか（Policies 側に値があるか）。</summary>
    public bool IsDeliveryOptimizationPolicyManaged()
        => _registry.ReadValue<object>(DoPolicyPath, DoValueName) != null;

    /// <summary>DODownloadMode 値を初心者向けの日本語ラベルに（純粋関数・xUnit 対象）。</summary>
    public static string DescribeDeliveryOptimizationMode(int mode) => mode switch
    {
        0 => "オフ（P2P なし・軽い）",
        1 => "LAN 内のみ",
        2 => "LAN 内のみ（同じグループ）",
        3 => "オン（インターネット含む・既定）",
        99 => "ダウンロードを簡素化",
        _ => $"モード {mode}",
    };

    /// <summary>「今の状態」表示用サマリ（ポリシー管理なら注記付き）。</summary>
    public string GetDeliveryOptimizationSummary()
    {
        var mode = GetDeliveryOptimizationMode() ?? DoModeDefault;
        var label = DescribeDeliveryOptimizationMode(mode);
        return IsDeliveryOptimizationPolicyManaged() ? label + "・ポリシー管理" : label;
    }

    /// <summary>
    /// 配信最適化を適用（light=true で P2P オフ）。適用前に原状を退避（first-touch-wins）。
    /// HKLM 書込のため非管理者では UnauthorizedAccessException 等（呼び出し側で昇格誘導）。
    /// </summary>
    public NetworkApplyResult ApplyDeliveryOptimization(bool light)
    {
        var before = GetDeliveryOptimizationMode();
        var payload = JsonSerializer.Serialize(new DeliveryOptimizationBackup(before, before.HasValue));
        _backup.SaveNetworkOriginalIfAbsent(FeatureDeliveryOptimization, payload);

        var target = light ? DoModeOff : DoModeDefault;
        _registry.WriteValue(DoConfigPath, DoValueName, target, RegistryValueKind.DWord);

        var after = GetDeliveryOptimizationMode();
        return new NetworkApplyResult(after == target, after ?? DoModeDefault, IsDeliveryOptimizationPolicyManaged());
    }

    /// <summary>配信最適化を退避ペイロードから復元（Phase 5「ぜんぶ戻す」が利用）。</summary>
    public void RestoreDeliveryOptimization(string jsonPayload)
    {
        var b = JsonSerializer.Deserialize<DeliveryOptimizationBackup>(jsonPayload);
        if (b == null) return;
        if (b.Existed && b.Mode.HasValue)
            _registry.WriteValue(DoConfigPath, DoValueName, b.Mode.Value, RegistryValueKind.DWord);
        else
            _registry.DeleteValue(DoConfigPath, DoValueName);  // 元々無かった → 削除して Windows 既定へ
    }

    // =================== Phase 4：DNS プロファイル切替 ===================

    /// <summary>
    /// 物理アクティブアダプタ判定（純粋関数・xUnit 対象）。
    /// Up かつ Ethernet/Wireless80211 で、説明に仮想・VPN・トンネル・Bluetooth 等のマーカーが無いもの。
    /// plan-validator エッジ1：誤って VPN/WSL/Hyper-V を含めないよう Description で厳格除外。
    /// </summary>
    public static bool IsPhysicalAdapter(NetworkInterfaceType type, OperationalStatus status, string? description)
    {
        if (status != OperationalStatus.Up) return false;
        if (type != NetworkInterfaceType.Ethernet && type != NetworkInterfaceType.Wireless80211) return false;

        var desc = description ?? string.Empty;
        string[] virtualMarkers =
        {
            "Hyper-V", "vEthernet", "Virtual", "VPN", "TAP", "WAN Miniport",
            "VMware", "VirtualBox", "Loopback", "Pseudo", "Tunnel", "WSL",
            "Bluetooth", "Wi-Fi Direct", "Npcap", "Container",
        };
        return !virtualMarkers.Any(m => desc.Contains(m, System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>IPv4/IPv6 アドレス文字列の検証（純粋関数・xUnit 対象・Phase 7 手入力で使用）。</summary>
    public static bool IsValidIpAddress(string? s)
        => !string.IsNullOrWhiteSpace(s) && IPAddress.TryParse(s.Trim(), out _);

    /// <summary>アクティブな物理アダプタを列挙（読取のみ・安全）。</summary>
    public List<AdapterInfo> GetActivePhysicalAdapters()
    {
        var list = new List<AdapterInfo>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!IsPhysicalAdapter(ni.NetworkInterfaceType, ni.OperationalStatus, ni.Description)) continue;
            List<string> dns;
            try { dns = ni.GetIPProperties().DnsAddresses.Select(a => a.ToString()).ToList(); }
            catch { dns = new(); }
            list.Add(new AdapterInfo(ni.Name, ni.Id, ni.Description ?? string.Empty, dns));
        }
        return list;
    }

    /// <summary>DNS サーバー一覧から既知プロファイル名を判定（純粋関数・xUnit 対象）。</summary>
    public static string DescribeDnsProfile(IEnumerable<string> servers)
    {
        var set = servers.ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        if (set.Contains("1.1.1.1") || set.Contains("1.0.0.1")) return "Cloudflare";
        if (set.Contains("8.8.8.8") || set.Contains("8.8.4.4")) return "Google";
        if (set.Count == 0) return "自動 (DHCP)";
        return "自動 / その他";
    }

    /// <summary>「今の状態」表示用 DNS サマリ（先頭物理アダプタ基準）。</summary>
    public string GetDnsSummary()
    {
        var adapters = GetActivePhysicalAdapters();
        if (adapters.Count == 0) return "（有効なアダプタなし）";
        var first = adapters[0];
        var profile = DescribeDnsProfile(first.DnsServers);
        var v4 = first.DnsServers.FirstOrDefault(a => !a.Contains(':'));
        return v4 == null ? profile : $"{profile}（{v4}）";
    }

    /// <summary>全アクティブ物理アダプタに DNS プロファイルを適用（netsh）。詳細層でアダプタを絞る場合は overload。</summary>
    public DnsApplyResult ApplyDnsProfile(DnsProfile profile)
        => ApplyDnsProfile(profile, null);

    /// <summary>
    /// DNS プロファイルを対象アダプタ（null/空＝全アクティブ物理アダプタ）に適用（netsh）。
    /// 適用前に対象アダプタの原状を退避（アダプタ単位 first-touch-wins）。部分失敗はロールバックして例外送出。
    /// 非管理者では netsh が失敗 → 例外（呼び出し側で昇格誘導）。
    /// </summary>
    public DnsApplyResult ApplyDnsProfile(DnsProfile profile, IReadOnlyList<string>? targetAdapterIds)
        => ApplyDnsCore(ResolveTargets(targetAdapterIds), name => ApplyDnsToAdapter(name, profile));

    /// <summary>
    /// 手入力 DNS（任意 IPv4/IPv6 配列）を対象アダプタ（null/空＝全件）に適用（netsh・Phase 7a 詳細層）。
    /// v4/v6 とも空配列のファミリは netsh 呼び出しをスキップ（エッジ1）。退避/ロールバックは profile 版と共通。
    /// </summary>
    public DnsApplyResult ApplyCustomDns(string[] v4, string[] v6, IReadOnlyList<string>? targetAdapterIds = null)
        => ApplyDnsCore(ResolveTargets(targetAdapterIds), name => ApplyCustomDnsToAdapter(name, v4, v6));

    /// <summary>適用対象アダプタを解決（null/空＝全アクティブ物理アダプタ／指定時は ID で絞る・存在しない ID は無視）。</summary>
    private List<AdapterInfo> ResolveTargets(IReadOnlyList<string>? targetAdapterIds)
    {
        var all = GetActivePhysicalAdapters();
        if (targetAdapterIds == null || targetAdapterIds.Count == 0) return all;
        var idSet = targetAdapterIds.ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        return all.Where(a => idSet.Contains(a.Id)).ToList();
    }

    /// <summary>
    /// DNS 適用の共通コア（profile 版 / custom 版 / 個別アダプタ版で共有・plan-validator 重要②③）。
    /// 退避：対象アダプタの「適用直前の原状」を読み、既存退避に未収録のアダプタだけ追記して上書き保存
    ///       （アダプタ単位 first-touch-wins。機能キー単位の IfAbsent では個別適用で後発アダプタが退避できないため）。
    /// ロールバック：適用直前の値（preApply）を done に蓄積し、途中失敗時に適用済みを巻き戻して例外送出。
    /// </summary>
    private DnsApplyResult ApplyDnsCore(List<AdapterInfo> targets, System.Action<string> applyToAdapter)
    {
        if (targets.Count == 0) return new DnsApplyResult(0, 0, 0, NoAdapters: true);

        // 適用直前の原状（ロールバック用＋退避マージ素材）。
        var preApply = targets.Select(a => new DnsAdapterBackup(
            a.Name, a.Id,
            ReadStaticDns(a.Id, ipv6: false),
            ReadStaticDns(a.Id, ipv6: true))).ToList();

        // 退避ストア：read-modify-write（未収録アダプタだけ追記＝アダプタ単位 first-touch-wins）。
        var merged = MergeDnsBackups(LoadDnsBackup(), preApply);
        _backup.SaveNetworkOriginal(FeatureDns, JsonSerializer.Serialize(new DnsBackup(merged)));

        var done = new List<DnsAdapterBackup>();
        int ok = 0;
        foreach (var a in targets)
        {
            try
            {
                applyToAdapter(a.Name);
                ok++;
                done.Add(preApply.First(b => b.Id == a.Id));
            }
            catch
            {
                // 部分適用ロールバック（適用済みを適用直前の値へ戻す）。
                foreach (var d in done)
                {
                    try { RestoreAdapterDns(d); } catch { /* ロールバック失敗は握り */ }
                }
                throw;  // 上位（VM）で昇格/エラー処理。ロールバック済。
            }
        }
        return new DnsApplyResult(targets.Count, ok, 0, NoAdapters: false);
    }

    /// <summary>退避ストアから DNS 原状（全アダプタ分）を読む（無ければ空）。</summary>
    private List<DnsAdapterBackup> LoadDnsBackup()
    {
        var originals = _backup.LoadNetworkOriginals();
        if (!originals.TryGetValue(FeatureDns, out var json)) return new();
        try { return JsonSerializer.Deserialize<DnsBackup>(json)?.Adapters ?? new(); }
        catch { return new(); }
    }

    /// <summary>
    /// 既存退避に、新規対象のうち未収録（ID 未登録）のアダプタだけ追記したリストを返す（純粋関数・xUnit 対象）。
    /// アダプタ単位 first-touch-wins：既に退避済みのアダプタの原状は保持（上書きしない）。
    /// </summary>
    public static List<DnsAdapterBackup> MergeDnsBackups(
        List<DnsAdapterBackup> existing, List<DnsAdapterBackup> newTargets)
    {
        var knownIds = existing.Select(b => b.Id).ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var merged = new List<DnsAdapterBackup>(existing);
        foreach (var nt in newTargets)
            if (knownIds.Add(nt.Id)) merged.Add(nt);  // Add が true＝未収録のみ追記
        return merged;
    }

    /// <summary>
    /// 手入力欄（プライマリ/セカンダリ等）から有効な IP だけを順序保持で配列化（純粋関数・xUnit 対象）。
    /// 空欄・空白・無効 IP（IsValidIpAddress=false）は除外。前後空白は trim して返す。
    /// </summary>
    public static string[] BuildDnsServers(params string?[] inputs)
        => inputs.Where(IsValidIpAddress).Select(s => s!.Trim()).ToArray();

    /// <summary>DNS を退避ペイロードから全アダプタ復元（Phase 5「ぜんぶ戻す」が利用）。</summary>
    public void RestoreDns(string jsonPayload)
    {
        var b = JsonSerializer.Deserialize<DnsBackup>(jsonPayload);
        if (b == null) return;
        System.Exception? firstError = null;
        foreach (var ad in b.Adapters)
        {
            try { RestoreAdapterDns(ad); }
            catch (System.Exception ex) { firstError ??= ex; }
        }
        if (firstError != null) throw firstError;  // 1 枚でも失敗 → 退避を残すためエラー伝播
    }

    // =================== Phase 5：共通「ぜんぶ戻す」 ===================

    /// <summary>退避済みの機能があるか（「ぜんぶ戻す」ボタンの活性判定）。</summary>
    public bool HasBackup() => _backup.HasNetworkOriginals();

    /// <summary>
    /// 退避済みの全機能を原状へ復元。成功した機能だけ退避ストアから消す（失敗は残して再試行可能に）。
    /// 各機能の復元は HKLM/netsh ＝管理者要（呼び出し側で事前昇格）。
    /// </summary>
    public NetworkRevertResult RevertAll()
    {
        var originals = _backup.LoadNetworkOriginals();
        if (originals.Count == 0) return new NetworkRevertResult(0, 0);

        int restored = 0, failed = 0;
        foreach (var kv in originals)
        {
            try
            {
                switch (kv.Key)
                {
                    case FeatureDeliveryOptimization: RestoreDeliveryOptimization(kv.Value); break;
                    case FeatureDns: RestoreDns(kv.Value); break;
                    default: continue;  // 未知の機能キーは触らない
                }
                _backup.RemoveNetworkOriginal(kv.Key);  // 成功したものだけ退避から消す
                restored++;
            }
            catch
            {
                failed++;  // 失敗は退避を残す（管理者で再試行可能）
            }
        }
        return new NetworkRevertResult(restored, failed);
    }

    // =================== Phase 6：軽いリセット（flushdns のみ） ===================

    /// <summary>DNS 解決キャッシュをクリア（ipconfig /flushdns）。実質可逆・管理者不要・退避不要。</summary>
    public void FlushDnsCache() => _launcher.RunIpconfig("/flushdns");

    // ── DNS 内部ヘルパー ──

    /// <summary>レジストリから静的 DNS（NameServer・空=DHCP）をロケール非依存で読む。</summary>
    private string ReadStaticDns(string guid, bool ipv6)
    {
        var svc = ipv6 ? "Tcpip6" : "Tcpip";
        var path = $@"HKLM\SYSTEM\CurrentControlSet\Services\{svc}\Parameters\Interfaces\{guid}";
        return _registry.ReadValue<string>(path, "NameServer") ?? string.Empty;
    }

    private void ApplyDnsToAdapter(string name, DnsProfile profile)
    {
        if (profile == DnsProfile.Auto)
        {
            _launcher.RunNetsh("interface", "ipv4", "set", "dnsservers", $"name={name}", "source=dhcp");
            try { _launcher.RunNetsh("interface", "ipv6", "set", "dnsservers", $"name={name}", "source=dhcp"); } catch { }
            return;
        }
        var (v4, v6) = profile == DnsProfile.Cloudflare ? (CloudflareV4, CloudflareV6) : (GoogleV4, GoogleV6);
        SetStaticDns(name, ipv6: false, v4);
        try { SetStaticDns(name, ipv6: true, v6); } catch { /* IPv6 失敗は致命でない（IPv4 が効けば実用上 OK） */ }
    }

    /// <summary>手入力 DNS を 1 アダプタに適用（空配列のファミリはスキップ＝エッジ1）。Phase 7a。</summary>
    private void ApplyCustomDnsToAdapter(string name, string[] v4, string[] v6)
    {
        if (v4.Length > 0) SetStaticDns(name, ipv6: false, v4);
        if (v6.Length > 0) { try { SetStaticDns(name, ipv6: true, v6); } catch { /* IPv6 失敗は致命でない */ } }
    }

    private void SetStaticDns(string name, bool ipv6, string[] servers)
    {
        if (servers.Length == 0) return;  // エッジ1（plan-validator）：空配列ガード（servers[0] の IndexOutOfRange 予防）。
        var fam = ipv6 ? "ipv6" : "ipv4";
        _launcher.RunNetsh("interface", fam, "set", "dnsservers", $"name={name}", "static", servers[0], "primary");
        for (int i = 1; i < servers.Length; i++)
            _launcher.RunNetsh("interface", fam, "add", "dnsservers", $"name={name}", servers[i], $"index={i + 1}");
    }

    private void RestoreAdapterDns(DnsAdapterBackup b)
    {
        RestoreFamilyDns(b.Name, ipv6: false, b.Ipv4NameServer);
        RestoreFamilyDns(b.Name, ipv6: true, b.Ipv6NameServer);
    }

    private void RestoreFamilyDns(string name, bool ipv6, string nameServerRaw)
    {
        var fam = ipv6 ? "ipv6" : "ipv4";
        var servers = ParseNameServer(nameServerRaw);
        if (servers.Length == 0)
        {
            // 元は DHCP（NameServer 空）→ 自動へ。
            _launcher.RunNetsh("interface", fam, "set", "dnsservers", $"name={name}", "source=dhcp");
            return;
        }
        _launcher.RunNetsh("interface", fam, "set", "dnsservers", $"name={name}", "static", servers[0], "primary");
        for (int i = 1; i < servers.Length; i++)
            _launcher.RunNetsh("interface", fam, "add", "dnsservers", $"name={name}", servers[i], $"index={i + 1}");
    }

    /// <summary>レジストリ NameServer（"1.1.1.1,1.0.0.1" 等）を配列へ（純粋関数・xUnit 対象）。</summary>
    public static string[] ParseNameServer(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? System.Array.Empty<string>()
            : raw.Split(new[] { ',', ' ', ';' }, System.StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>ネットワーク設定適用の結果（配信最適化）。</summary>
public record NetworkApplyResult(bool Applied, int EffectiveValue, bool PolicyManaged);

/// <summary>配信最適化の原状退避ペイロード（JSON 直列化用）。</summary>
public record DeliveryOptimizationBackup(int? Mode, bool Existed);

/// <summary>DNS プロファイル。</summary>
public enum DnsProfile { Auto, Cloudflare, Google }

/// <summary>アクティブ物理アダプタ情報。</summary>
public record AdapterInfo(string Name, string Id, string Description, List<string> DnsServers);

/// <summary>DNS 適用結果。</summary>
public record DnsApplyResult(int Total, int Succeeded, int Failed, bool NoAdapters);

/// <summary>アダプタ 1 枚分の DNS 原状退避（NameServer 生文字列・空=DHCP）。</summary>
public record DnsAdapterBackup(string Name, string Id, string Ipv4NameServer, string Ipv6NameServer);

/// <summary>DNS 原状退避ペイロード（全対象アダプタ）。</summary>
public record DnsBackup(List<DnsAdapterBackup> Adapters);

/// <summary>「ぜんぶ戻す」の結果。</summary>
public record NetworkRevertResult(int Restored, int Failed);
