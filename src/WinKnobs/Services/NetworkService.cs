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
    public const string FeatureDoh = "doh";  // v0.1.8：暗号化DNS（DNS over HTTPS）

    // ── DoH（DNS over HTTPS・v0.1.8）──
    // 有効化の本命はインターフェース別レジストリ DohFlags（REG_QWORD）。netsh テンプレ登録は補助（best-effort）。
    private const string DnscacheIfParams = @"HKLM\SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters";
    public const long DohFlagsAuto = 1;  // 自動（暗号化優先・失敗時は平文フォールバック＝ネットが切れない安全側）

    /// <summary>既知 DoH プロバイダの IP→テンプレ（v0.1.8 はこの3社限定。カスタムIPは v0.2）。</summary>
    private static readonly Dictionary<string, string> KnownDohTemplates =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["1.1.1.1"] = "https://cloudflare-dns.com/dns-query",
            ["1.0.0.1"] = "https://cloudflare-dns.com/dns-query",
            ["2606:4700:4700::1111"] = "https://cloudflare-dns.com/dns-query",
            ["2606:4700:4700::1001"] = "https://cloudflare-dns.com/dns-query",
            ["8.8.8.8"] = "https://dns.google/dns-query",
            ["8.8.4.4"] = "https://dns.google/dns-query",
            ["2001:4860:4860::8888"] = "https://dns.google/dns-query",
            ["2001:4860:4860::8844"] = "https://dns.google/dns-query",
            ["9.9.9.9"] = "https://dns.quad9.net/dns-query",
            ["149.112.112.112"] = "https://dns.quad9.net/dns-query",
            ["2620:fe::fe"] = "https://dns.quad9.net/dns-query",
            ["2620:fe::9"] = "https://dns.quad9.net/dns-query",
        };

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

    // NCF_PHYSICAL 特性ビット（netcfgx.h）。Get-NetAdapter -Physical と同じ「物理ハードウェア」判定。
    private const int NcfPhysical = 0x4;
    private const string NetClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    /// <summary>アクティブな物理アダプタを列挙（読取のみ・安全）。
    /// .NET の Description ヒューリスティック（<see cref="IsPhysicalAdapter"/>）に加え、レジストリ NCF_PHYSICAL の
    /// 真の物理 GUID 集合と突き合わせて擬似/エミュレータ NIC（LDPlayer 等）を構造的に排除する。
    /// 2026-06-20 実機検証で発覚：read/apply は Get-NetAdapter -Physical 基準のため、列挙が不一致だと幻アダプタで
    /// 「現在設定を読み取れない」→ 適用失敗になる。レジストリ取得不能時は集合 null＝従来ヒューリスティックのみ（可用性優先）。</summary>
    public List<AdapterInfo> GetActivePhysicalAdapters()
    {
        var physicalGuids = TryGetPhysicalGuidSet();
        var list = new List<AdapterInfo>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!IsPhysicalAdapter(ni.NetworkInterfaceType, ni.OperationalStatus, ni.Description)) continue;
            // 真の物理集合が得られたら、そこに無い GUID（擬似 NIC）は除外＝列挙ソースを read/apply と一致させる。
            if (physicalGuids != null && !physicalGuids.Contains(ni.Id)) continue;
            List<string> dns;
            try { dns = ni.GetIPProperties().DnsAddresses.Select(a => a.ToString()).ToList(); }
            catch { dns = new(); }
            list.Add(new AdapterInfo(ni.Name, ni.Id, ni.Description ?? string.Empty, dns));
        }
        return list;
    }

    /// <summary>レジストリのネットワーククラスキーから NCF_PHYSICAL を持つ InterfaceGuid 集合を得る
    /// （PowerShell 不使用・plan-validator 重要3 準拠）。取得不能/0 件は null（呼び出し側はヒューリスティックにフォールバック）。</summary>
    private static HashSet<string>? TryGetPhysicalGuidSet()
    {
        try
        {
            using var cls = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(NetClassKey);
            if (cls == null) return null;
            var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var sub in cls.GetSubKeyNames())
            {
                if (sub.Length != 4 || !sub.All(char.IsDigit)) continue;   // 0000..NNNN の数値キーのみ
                using var k = cls.OpenSubKey(sub);
                var guid = k?.GetValue("NetCfgInstanceId") as string;
                if (string.IsNullOrWhiteSpace(guid)) continue;
                if (k!.GetValue("Characteristics") is int ch && (ch & NcfPhysical) != 0)
                    set.Add(guid);
            }
            return set.Count > 0 ? set : null;
        }
        catch { return null; }
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
    ///
    /// 復元順序（plan-validator 致命2）：DoH → DNS → 配信最適化 の明示順。
    /// DNS を先に戻すと、DoH 退避が指す「適用時の DNS IP」と現状がズレる事故を避ける。
    /// plan-validator 致命1：originals を最初に1回ロード（スナップショット）して固定順で回し、
    /// 退避ストアからの削除は列挙後にまとめて行う（列挙中変更の InvalidOperationException 回避）。
    /// </summary>
    public NetworkRevertResult RevertAll()
    {
        var originals = _backup.LoadNetworkOriginals();
        if (originals.Count == 0) return new NetworkRevertResult(0, 0);

        int restored = 0, failed = 0;
        var succeededKeys = new List<string>();

        // 明示順（DoH→DNS→配信最適化）。未知の機能キーは触らない（退避も残す）。
        foreach (var key in new[] { FeatureDoh, FeatureDns, FeatureDeliveryOptimization })
        {
            if (!originals.TryGetValue(key, out var payload)) continue;
            try
            {
                switch (key)
                {
                    case FeatureDoh: RestoreDoh(payload); break;
                    case FeatureDns: RestoreDns(payload); break;
                    case FeatureDeliveryOptimization: RestoreDeliveryOptimization(payload); break;
                }
                succeededKeys.Add(key);  // 削除はループ後（列挙中変更を避ける）
                restored++;
            }
            catch
            {
                failed++;  // 失敗は退避を残す（管理者で再試行可能）
            }
        }

        foreach (var k in succeededKeys) _backup.RemoveNetworkOriginal(k);  // 成功分だけ後始末
        return new NetworkRevertResult(restored, failed);
    }

    // =================== v0.1.8：DoH（暗号化DNS・DNS over HTTPS） ===================
    // 本命＝インターフェース別レジストリ DohFlags(REG_QWORD)。netsh テンプレ登録は補助(best-effort)。
    // 対象は「選択アダプタの現行 DNS のうち既知 DoH プロバイダの IP」。退避/復元は DNS と同流儀(first-touch-wins)。

    /// <summary>OS が DoH に対応しているか（このマシンのビルドで判定）。</summary>
    public bool IsDohSupportedHere() => IsDohSupported(GetOsBuildNumber());

    /// <summary>Windows のビルド番号を読む（CurrentBuildNumber=REG_SZ）。読めなければ 0（純粋関数 IsDohSupported で安全に無効化）。</summary>
    public int GetOsBuildNumber()
    {
        var s = _registry.ReadValue<string>(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber");
        return int.TryParse(s, out var n) ? n : 0;  // REG_SZ → int 化失敗（欠損/非数値）は 0
    }

    /// <summary>ビルド番号から DoH 対応を判定（純粋関数・xUnit 対象）。Windows 11=22000+。0（読めない）は false＝安全側。</summary>
    public static bool IsDohSupported(int build) => build >= 22000;

    /// <summary>IP が既知 DoH プロバイダか（純粋関数・xUnit 対象）。</summary>
    public static bool IsKnownDohProvider(string? ip)
        => !string.IsNullOrWhiteSpace(ip) && KnownDohTemplates.ContainsKey(ip.Trim());

    /// <summary>既知 DoH プロバイダの DoH テンプレ URL（純粋関数・xUnit 対象・未知は null）。</summary>
    public static string? GetDohTemplate(string? ip)
        => ip != null && KnownDohTemplates.TryGetValue(ip.Trim(), out var t) ? t : null;

    /// <summary>
    /// インターフェース別 DoH 設定のレジストリパスを生成（純粋関数・xUnit 対象）。
    /// HKLM\...\Dnscache\InterfaceSpecificParameters\{GUID}\DohInterfaceSettings\{Doh|Doh6}\{IP}
    /// </summary>
    public static string BuildDohRegistryPath(string guid, bool ipv6, string ip)
        => $@"{DnscacheIfParams}\{guid}\DohInterfaceSettings\{(ipv6 ? "Doh6" : "Doh")}\{ip}";

    /// <summary>「今の状態」表示用 DoH サマリ（アクティブ物理アダプタの集約）。</summary>
    public string GetDohSummary()
    {
        if (!IsDohSupportedHere()) return "この Windows では非対応";
        int on = 0, eligible = 0;
        foreach (var a in GetActivePhysicalAdapters())
        {
            var targets = ResolveDohTargetsForAdapter(a.Id);
            if (targets.Count == 0) continue;  // 現行 DNS が既知 DoH 先でない＝対象外
            eligible++;
            // cross-reviewer 重要2：キー存在でなく「値が有効(>=1)」で判定。
            // DohFlags=0（外部ツール/GPO で明示無効化）を ON と誤表示しないため、ReadDohState の値を見る。
            var allOn = targets.All(t =>
            {
                var st = ReadDohState(t.Guid, t.Ipv6, t.Ip);
                return st.DohExisted && st.Flags >= 1;
            });
            if (allOn) on++;
        }
        if (eligible == 0) return "対象外（DNS が Cloudflare/Google/Quad9 のときに使えます）";
        return on == eligible ? $"ON（{on}/{eligible} アダプタ）"
             : on == 0 ? "OFF"
             : $"一部ON（{on}/{eligible} アダプタ）";
    }

    /// <summary>選択アダプタに既知 DoH プロバイダの現行 DNS があるか（UI の事前判定用）。</summary>
    public bool HasKnownDohDns(IReadOnlyList<string>? targetAdapterIds)
        => ResolveTargets(targetAdapterIds).Any(a => ResolveDohTargetsForAdapter(a.Id).Count > 0);

    /// <summary>
    /// DoH を対象アダプタ（null/空＝全アクティブ物理）に適用。enable=true で ON（DohFlags=1）/ false で解除（キー削除）。
    /// 対象＝各アダプタの現行 DNS のうち既知 DoH プロバイダの IP。0 件なら NoTargets（呼び出し側で案内）。
    /// 退避：適用直前の (GUID×family×IP) 原状を first-touch でマージ保存。部分失敗はロールバックして例外送出。
    /// </summary>
    public DohApplyResult ApplyDoh(IReadOnlyList<string>? targetAdapterIds, bool enable)
    {
        var adapters = ResolveTargets(targetAdapterIds);
        var targets = adapters.SelectMany(a => ResolveDohTargetsForAdapter(a.Id)).ToList();
        if (targets.Count == 0) return new DohApplyResult(0, 0, NoTargets: true);

        // 適用直前の原状（ロールバック用＋退避マージ素材）。読めたままを保存（自前正規化しない＝表記ズレ回避）。
        var preApply = targets.Select(t => ReadDohState(t.Guid, t.Ipv6, t.Ip)).ToList();
        var merged = MergeDohBackups(LoadDohBackup(), preApply);
        _backup.SaveNetworkOriginal(FeatureDoh, JsonSerializer.Serialize(new DohBackup(merged)));

        // 本命＝レジストリ DohFlags 書込/削除（速い・成否判定の対象）。途中失敗は適用済みをロールバックして例外送出。
        var done = new List<DohAdapterBackup>();
        int ok = 0;
        foreach (var t in targets)
        {
            try
            {
                var path = BuildDohRegistryPath(t.Guid, t.Ipv6, t.Ip);
                if (enable) _registry.WriteValue(path, "DohFlags", DohFlagsAuto, RegistryValueKind.QWord);
                else _registry.DeleteKeyTree(path);  // 解除＝当該 IP の Doh サブキーを消す
                ok++;
                done.Add(preApply.First(b => b.Guid == t.Guid && b.Ipv6 == t.Ipv6 && b.Ip == t.Ip));
            }
            catch
            {
                foreach (var d in done) { try { RestoreDohEntry(d); } catch { /* ロールバック失敗は握り */ } }
                throw;
            }
        }

        // 補助＝netsh テンプレ登録（enable 時のみ・IP 重複は1回・best-effort・失敗は致命でない＝plan-validator 重要3）。
        // cross-reviewer 重要1：per-target でなく全件成功後に一意 IP だけ登録（netsh プロセス起動回数を最小化）。
        if (enable)
            foreach (var ip in targets.Select(t => t.Ip).Distinct(System.StringComparer.OrdinalIgnoreCase))
                TryRegisterDohTemplate(ip);

        return new DohApplyResult(targets.Count, ok, NoTargets: false);
    }

    /// <summary>DoH を退避ペイロードから復元（RevertAll が利用）。1枚でも失敗したら退避を残すため例外伝播。</summary>
    public void RestoreDoh(string jsonPayload)
    {
        var b = JsonSerializer.Deserialize<DohBackup>(jsonPayload);
        if (b == null) return;
        System.Exception? firstError = null;
        foreach (var e in b.Entries)
        {
            try { RestoreDohEntry(e); }
            catch (System.Exception ex) { firstError ??= ex; }
        }
        if (firstError != null) throw firstError;
    }

    /// <summary>
    /// 既存退避に、未収録（GUID×family×IP）の新規対象だけ追記（純粋関数・xUnit 対象）。
    /// アダプタ・IP 単位 first-touch-wins：既に退避済みの原状は保持（上書きしない）。
    /// </summary>
    public static List<DohAdapterBackup> MergeDohBackups(
        List<DohAdapterBackup> existing, List<DohAdapterBackup> newTargets)
    {
        static string Key(DohAdapterBackup b) => $"{b.Guid}|{(b.Ipv6 ? "6" : "4")}|{b.Ip}".ToLowerInvariant();
        var known = existing.Select(Key).ToHashSet();
        var merged = new List<DohAdapterBackup>(existing);
        foreach (var nt in newTargets)
            if (known.Add(Key(nt))) merged.Add(nt);
        return merged;
    }

    // ── DoH 内部ヘルパー ──

    /// <summary>1 アダプタの現行 DNS のうち既知 DoH プロバイダの (GUID, family, IP) を列挙（v4/v6 両方）。</summary>
    private List<(string Guid, bool Ipv6, string Ip)> ResolveDohTargetsForAdapter(string guid)
    {
        var list = new List<(string, bool, string)>();
        foreach (var ipv6 in new[] { false, true })
            foreach (var s in ParseNameServer(ReadStaticDns(guid, ipv6)))
                if (IPAddress.TryParse(s.Trim(), out var ip) && IsKnownDohProvider(ip.ToString()))
                    list.Add((guid, ipv6, ip.ToString()));  // 自分が一貫して使う正規表記
        return list;
    }

    /// <summary>レジストリから現在の DohFlags 原状を読む（キー/値が無ければ DohExisted=false）。</summary>
    private DohAdapterBackup ReadDohState(string guid, bool ipv6, string ip)
    {
        var raw = _registry.ReadValue<object>(BuildDohRegistryPath(guid, ipv6, ip), "DohFlags");
        long flags = 0;
        if (raw != null) { try { flags = System.Convert.ToInt64(raw); } catch { flags = 0; } }  // QWORD/DWORD 両対応
        return new DohAdapterBackup(guid, ipv6, ip, raw != null, flags);
    }

    /// <summary>退避レコード1件を原状へ（元々あれば値書戻し／無ければ Doh サブキー削除）。</summary>
    private void RestoreDohEntry(DohAdapterBackup e)
    {
        var path = BuildDohRegistryPath(e.Guid, e.Ipv6, e.Ip);
        if (e.DohExisted) _registry.WriteValue(path, "DohFlags", e.Flags, RegistryValueKind.QWord);
        else _registry.DeleteKeyTree(path);
    }

    /// <summary>退避ストアから DoH 原状（全エントリ）を読む（無ければ空）。</summary>
    private List<DohAdapterBackup> LoadDohBackup()
    {
        var originals = _backup.LoadNetworkOriginals();
        if (!originals.TryGetValue(FeatureDoh, out var json)) return new();
        try { return JsonSerializer.Deserialize<DohBackup>(json)?.Entries ?? new(); }
        catch { return new(); }
    }

    /// <summary>既知 DoH テンプレを netsh で登録（補助・best-effort・失敗は致命でない）。dnsclient/dns 両コンテキスト試行。</summary>
    private void TryRegisterDohTemplate(string ip)
    {
        var template = GetDohTemplate(ip);
        if (string.IsNullOrEmpty(template)) return;
        try { _launcher.RunNetsh("dnsclient", "add", "encryption", $"server={ip}", $"dohtemplate={template}", "autoupgrade=yes", "udpfallback=yes"); return; }
        catch { /* 旧コンテキスト or 既存登録で失敗し得る → フォールバック */ }
        try { _launcher.RunNetsh("dns", "add", "encryption", $"server={ip}", $"dohtemplate={template}", "autoupgrade=yes"); }
        catch { /* 既に登録済み等＝無害。本命は DohFlags レジストリ */ }
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

/// <summary>DoH 原状退避（アダプタ×family×IP 単位・DohExisted=適用前にキーがあったか）。</summary>
public record DohAdapterBackup(string Guid, bool Ipv6, string Ip, bool DohExisted, long Flags);

/// <summary>DoH 原状退避ペイロード（全対象エントリ）。</summary>
public record DohBackup(List<DohAdapterBackup> Entries);

/// <summary>DoH 適用結果（NoTargets＝既知 DoH プロバイダの現行 DNS が無い）。</summary>
public record DohApplyResult(int Total, int Succeeded, bool NoTargets);
