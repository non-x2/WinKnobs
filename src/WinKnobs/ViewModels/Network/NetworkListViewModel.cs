using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinKnobs.Services;

namespace WinKnobs.ViewModels.Network;

/// <summary>
/// ネットワーク・カテゴリ専用ペインの ViewModel（v0.1.7・設計ドラフト 4-3）。
/// 三層構造（設計原則1）：A 初心者（かんたん設定＋純正ランチャー）／B 中級（▸ くわしい設定）／C 上級（v0.2.2）。
///
/// 実装フェーズ：
///   Phase 1（骨格）／Phase 2（機能0 純正ランチャー）／Phase 3（機能3 帯域抑制）実装済。
///   Phase 4-7：DNS 切替／ぜんぶ戻す／軽いリセット／詳細層を順次実装。
///
/// 昇格ヘルパーは StartupListViewModel から複製（将来 M-1：ElevationAwareViewModel へ共通化）。
/// </summary>
public partial class NetworkListViewModel : ObservableObject
{
    private readonly ExternalLauncherService _launcher;
    private readonly DialogService _dialogs;
    private readonly NetworkService _network;
    private readonly PrivilegeService _privilege;
    private readonly NicPowerService _nic;            // v0.2.2：NIC省電力プリセット
    private readonly RemoteDesktopService _rdp;       // v0.2.2：リモートセッション判定（締め出し警告）

    /// <summary>NIC省電力の状態リフレッシュ世代（fire-and-forget の古い値上書きレース対策・見落とし事例 2026-06-09）。</summary>
    private int _nicStatusGeneration;

    [ObservableProperty]
    private string _statusMessage =
        "ネットワーク設定。「かんたん設定」または「Windowsの画面を開く」から操作できます。";

    // ── 今の状態 ──
    [ObservableProperty] private string _dnsSummary = "（Phase 4 で表示）";
    [ObservableProperty] private string _connectionSummary = "—";
    [ObservableProperty] private string _deliveryOptimizationSummary = "（読み込み中）";

    // ── DoH（暗号化DNS・v0.1.8）。非対応 OS ではボタン無効化。──
    [ObservableProperty] private string _dohSummary = "（読み込み中）";
    [ObservableProperty] private bool _isDohSupported = true;

    /// <summary>▸ くわしい設定 Expander の開閉（既定：閉じる＝初心者の視界から隠す・設計原則1）。</summary>
    [ObservableProperty]
    private bool _isAdvancedExpanded;

    // ── 詳細層：DNS手入力（Phase 7a）。有効 IP が1つでもあれば適用ボタン活性。──
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCustomDnsCommand))]
    private string _customDnsV4Primary = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCustomDnsCommand))]
    private string _customDnsV4Secondary = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCustomDnsCommand))]
    private string _customDnsV6 = string.Empty;

    /// <summary>詳細層：DNS 適用の対象アダプタ選択（既定＝全チェック＝現行の全件挙動・Phase 7c）。</summary>
    public ObservableCollection<AdapterSelect> Adapters { get; } = new();

    // ── NIC省電力プリセット（v0.2.2）──
    /// <summary>NIC省電力の操作中フラグ（PowerShell 120秒・UI フリーズ防止＋ペイン内直列化・🔧反映エッジ3）。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyNicPowerSavingCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreNicPowerCommand))]
    private bool _isNicPowerBusy;

    /// <summary>NIC省電力の「今の状態」（省電力件数＋電源オフ許可の2軸・🔧反映エッジ2）。</summary>
    [ObservableProperty] private string _nicPowerStatus = "（読み込み中）";

    public NetworkListViewModel(
        ExternalLauncherService launcher,
        DialogService dialogs,
        NetworkService network,
        PrivilegeService privilege,
        NicPowerService nic,
        RemoteDesktopService rdp)
    {
        _launcher = launcher;
        _dialogs = dialogs;
        _network = network;
        _privilege = privilege;
        _nic = nic;
        _rdp = rdp;
    }

    /// <summary>
    /// カテゴリ選択時に MainViewModel から呼ばれる。「今の状態」を読み直す。
    /// </summary>
    public void Load()
    {
        RefreshStatus();
        RefreshAdapters();
        RefreshNicPowerStatus();
        StatusMessage = "ネットワーク設定を開きました。「Windowsの画面を開く」はすぐに使えます。";
    }

    /// <summary>
    /// NIC省電力の「今の状態」を非同期で読み直す（読取のみ・安全）。
    /// 世代カウンタで古い結果の上書きを防ぐ（見落とし事例 2026-06-09）。対象＝選択アダプタの先頭。
    /// </summary>
    private async void RefreshNicPowerStatus()
    {
        var gen = ++_nicStatusGeneration;
        var active = _network.GetActivePhysicalAdapters();
        var target = Adapters.FirstOrDefault(a => a.IsSelected)?.Id
                     ?? active.FirstOrDefault()?.Id;
        if (target == null)
        {
            NicPowerStatus = "（有効な物理アダプタがありません）";
            return;
        }

        NicPowerHelper.NicPowerSnapshot? snap = null;
        try { snap = await System.Threading.Tasks.Task.Run(() => _nic.ReadSnapshot(target)); }
        catch { /* 読取失敗は下の DescribeStatus(null) でフォールバック */ }

        if (gen != _nicStatusGeneration) return;   // より新しいリフレッシュが走った → 破棄
        NicPowerStatus = NicPowerHelper.DescribeStatus(snap);
    }

    /// <summary>「今の状態」（配信最適化・DNS）を読み直してプロパティへ反映。</summary>
    private void RefreshStatus()
    {
        try { DeliveryOptimizationSummary = _network.GetDeliveryOptimizationSummary(); }
        catch { DeliveryOptimizationSummary = "（取得できません）"; }
        try { DnsSummary = _network.GetDnsSummary(); }
        catch { DnsSummary = "（取得できません）"; }
        try { DohSummary = _network.GetDohSummary(); IsDohSupported = _network.IsDohSupportedHere(); }
        catch { DohSummary = "（取得できません）"; }
    }

    /// <summary>
    /// 対象アダプタ選択リストを現在のアクティブ物理アダプタで再構築（詳細層・Phase 7c）。
    /// 既定は全選択。再ロード時は既存のチェック状態を維持し、新規アダプタは選択状態で追加する。
    /// </summary>
    private void RefreshAdapters()
    {
        var prev = Adapters.ToDictionary(a => a.Id, a => a.IsSelected, System.StringComparer.OrdinalIgnoreCase);
        Adapters.Clear();
        foreach (var info in _network.GetActivePhysicalAdapters())
        {
            var selected = !prev.TryGetValue(info.Id, out var was) || was;  // 既知は以前の状態・新規は選択
            Adapters.Add(new AdapterSelect(info) { IsSelected = selected });
        }
    }

    // ============ Phase 2：機能0 Windows 純正画面ランチャー（実装済） ============

    /// <summary>
    /// Windows 純正のネットワーク画面を開く（開くだけ・設定値は変更しない・管理者不要・危険度🟢）。
    /// CommandParameter：status / wifi / adapters / sharing / proxy / vpn。設計原則7「純正へ逃がす」安全弁。
    /// </summary>
    [RelayCommand]
    private void OpenWindowsNetwork(string? key)
    {
        var (fileName, args, label) = key switch
        {
            "status"   => ("ms-settings:network-status", (string?)null, "ネットワークの状態"),
            "wifi"     => ("ms-settings:network-wifi",    (string?)null, "Wi-Fi"),
            "adapters" => ("control.exe", "ncpa.cpl", "ネットワーク接続（アダプター）"),
            "sharing"  => ("control.exe", "/name Microsoft.NetworkAndSharingCenter", "ネットワークと共有センター"),
            "proxy"    => ("ms-settings:network-proxy",   (string?)null, "プロキシ"),
            "vpn"      => ("ms-settings:network-vpn",      (string?)null, "VPN"),
            _          => ("ms-settings:network",          (string?)null, "ネットワーク設定"),
        };

        if (_launcher.OpenNetworkSettings(fileName, args))
        {
            StatusMessage = $"🪟 「{label}」の画面を開きました。";
            return;
        }

        // エッジ（設計 3-0）：ms-settings: 非対応のまれな環境 → クラシックなネットワーク接続一覧へフォールバック。
        if (_launcher.OpenNetworkSettings("control.exe", "ncpa.cpl"))
        {
            StatusMessage = $"🪟 「{label}」を直接開けなかったため、ネットワーク接続一覧を開きました。";
        }
        else
        {
            StatusMessage = $"⚠️ 「{label}」の画面を開けませんでした。";
        }
    }

    // ============ Phase 3：機能3 バックグラウンド帯域抑制（配信最適化 P2P オフ・実装済） ============

    /// <summary>
    /// 「通信を軽くする」＝配信最適化（DODownloadMode）を P2P オフ（0）に。戻せる🟡・HKLM 書込で管理者要。
    /// </summary>
    [RelayCommand]
    private void SetBandwidth()
    {
        if (_privilege.IsShutdownPending || GuardNicBusy()) return;

        var ok = _dialogs.Confirm(
            "「通信を軽くする」を実行します。\n\n" +
            "・他の PC との更新の配り合い（配信最適化の P2P）をオフにします。\n" +
            "・ご家庭で PC が 1 台なら、ふだんの使い心地に影響はありません。\n" +
            "・Windows Update 自体は止まりません。\n" +
            "・この変更は「↩ ぜんぶ元の状態に戻す」で元に戻せます。\n\n" +
            "実行しますか？",
            "通信を軽くする");
        if (!ok) return;

        // HKLM 書込 → 非管理者なら事前 3 択（再起動／続行／キャンセル）。
        if (!_privilege.IsRunningAsAdministrator())
        {
            var choice = _dialogs.ConfirmElevation("通信を軽くする（配信最適化）", isFromError: false);
            switch (choice)
            {
                case ElevationChoice.RestartAsAdmin: RequestRestartAsAdministrator(); return;
                case ElevationChoice.ContinueAnyway: break;
                case ElevationChoice.Cancel:
                default: return;
            }
        }

        try
        {
            var result = _network.ApplyDeliveryOptimization(light: true);
            RefreshStatus();

            if (result.PolicyManaged && !result.Applied)
                StatusMessage = "⚠️ 配信最適化はグループポリシーで管理されているため、このツールからは変更できませんでした。";
            else if (!result.Applied)
                StatusMessage = "⚠️ 設定を書き込みましたが、反映を確認できませんでした（再起動後に反映される場合があります）。";
            else
                StatusMessage = "✅ 通信を軽くしました（配信最適化の P2P をオフ）。元に戻すには「↩ ぜんぶ元の状態に戻す」。";
        }
        catch (UnauthorizedAccessException) when (HandleElevationNeeded("通信を軽くする")) { }
        catch (SecurityException)            when (HandleElevationNeeded("通信を軽くする")) { }
        catch (InvalidOperationException ex) when (LooksLikeAccessDenied(ex.Message) && HandleElevationNeeded("通信を軽くする")) { }
        catch (Exception ex)
        {
            _dialogs.Error($"設定の変更に失敗しました：\n{ex.Message}", "失敗");
            StatusMessage = $"❌ 失敗: {ex.Message}";
        }
    }

    // ============ Phase 4-6：かんたん設定（各フェーズで実装）============

    /// <summary>
    /// DNS プロファイルを「対象アダプタ」（くわしい設定で選択・既定全選択）に切替（netsh）。戻せる🟡・管理者要。
    /// CommandParameter：cloudflare（既定・かんたん設定）/ google / auto（DHCPに戻す）。詳細層から google/auto も選べる。
    /// </summary>
    [RelayCommand]
    private void ApplyDns(string? profileKey)
    {
        if (_privilege.IsShutdownPending || GuardNicBusy()) return;

        var (profile, label) = profileKey switch
        {
            "auto"   => (DnsProfile.Auto, "自動 (DHCP)"),
            "google" => (DnsProfile.Google, "Google (8.8.8.8)"),
            _        => (DnsProfile.Cloudflare, "Cloudflare (1.1.1.1)"),
        };

        var targets = ResolveSelectedAdapters();
        if (targets == null) return;  // 対象なし／未選択（中で Info 表示済み）

        var detail = profile == DnsProfile.Auto
            ? "・各アダプタの DNS を自動取得（DHCP）に戻します。\n"
            : "・Web サイト名の解決が速く・プライバシー重視になります。\n";
        var ok = _dialogs.Confirm(
            $"DNS を「{label}」に変更します。\n\n" +
            $"対象アダプタ：{string.Join("・", targets.Select(a => a.Name))}\n\n" +
            detail +
            "・「↩ ぜんぶ元の状態に戻す」で元の状態に戻せます（戻す操作はインターネット不要）。\n\n" +
            "実行しますか？",
            $"DNSを変更（{label}）");
        if (!ok) return;

        if (!_privilege.IsRunningAsAdministrator())
        {
            var choice = _dialogs.ConfirmElevation($"DNS変更（{label}）", isFromError: false);
            switch (choice)
            {
                case ElevationChoice.RestartAsAdmin: RequestRestartAsAdministrator(); return;
                case ElevationChoice.ContinueAnyway: break;
                case ElevationChoice.Cancel:
                default: return;
            }
        }

        try
        {
            var result = _network.ApplyDnsProfile(profile, targets.Select(a => a.Id).ToList());
            RefreshStatus();
            StatusMessage = result.Succeeded == result.Total
                ? $"✅ DNS を「{label}」に変更しました（{result.Succeeded}/{result.Total} アダプタ）。戻すには「↩ ぜんぶ元の状態に戻す」。"
                : $"⚠️ 一部のアダプタで変更できませんでした（{result.Succeeded}/{result.Total} 成功）。";
        }
        catch (UnauthorizedAccessException) when (HandleElevationNeeded("DNS変更")) { }
        catch (SecurityException)            when (HandleElevationNeeded("DNS変更")) { }
        catch (InvalidOperationException ex) when (LooksLikeAccessDenied(ex.Message) && HandleElevationNeeded("DNS変更")) { }
        catch (Exception ex)
        {
            _dialogs.Error($"DNS の変更に失敗しました：\n{ex.Message}", "失敗");
            StatusMessage = $"❌ DNS 変更失敗: {ex.Message}";
        }
    }

    /// <summary>
    /// 手入力した DNS（v4プライマリ/セカンダリ・v6）を「対象アダプタ」に適用（詳細層・Phase 7a）。戻せる🟡・管理者要。
    /// 有効 IP が1つも無ければボタン非活性（CanApplyCustomDns）。空ファミリは適用スキップ（NetworkService 側エッジ1）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApplyCustomDns))]
    private void ApplyCustomDns()
    {
        if (_privilege.IsShutdownPending || GuardNicBusy()) return;

        var v4 = NetworkService.BuildDnsServers(CustomDnsV4Primary, CustomDnsV4Secondary);
        var v6 = NetworkService.BuildDnsServers(CustomDnsV6);
        if (v4.Length == 0 && v6.Length == 0)
        {
            _dialogs.Info("有効な DNS サーバの IP アドレスを入力してください（例：1.1.1.1）。", "入力エラー");
            return;
        }

        var targets = ResolveSelectedAdapters();
        if (targets == null) return;  // 対象なし／未選択（中で Info 表示済み）

        var shown = string.Join("・", v4.Concat(v6));
        // cross-reviewer 重要UX①：入力しなかったファミリは現状維持＝プロファイル版（v4/v6 両方セット）と非対称。
        // 「片方だけ入力したのに、もう片方が前の DNS のまま残る」混在状態をユーザーに見えるように明示する。
        var keepNote =
            v4.Length == 0 ? "・IPv4 の DNS は今のまま変わりません（IPv6 だけ設定します）。\n" :
            v6.Length == 0 ? "・IPv6 の DNS は今のまま変わりません（IPv4 だけ設定します）。\n" :
            "";
        var ok = _dialogs.Confirm(
            "手入力した DNS を適用します。\n\n" +
            $"DNS：{shown}\n" +
            $"対象アダプタ：{string.Join("・", targets.Select(a => a.Name))}\n\n" +
            keepNote +
            "・「↩ ぜんぶ元の状態に戻す」で元の状態に戻せます（戻す操作はインターネット不要）。\n\n" +
            "実行しますか？",
            "DNSを手入力で適用");
        if (!ok) return;

        if (!_privilege.IsRunningAsAdministrator())
        {
            var choice = _dialogs.ConfirmElevation("DNS手入力", isFromError: false);
            switch (choice)
            {
                case ElevationChoice.RestartAsAdmin: RequestRestartAsAdministrator(); return;
                case ElevationChoice.ContinueAnyway: break;
                case ElevationChoice.Cancel:
                default: return;
            }
        }

        try
        {
            var result = _network.ApplyCustomDns(v4, v6, targets.Select(a => a.Id).ToList());
            RefreshStatus();
            StatusMessage = result.Succeeded == result.Total
                ? $"✅ 手入力 DNS を適用しました（{result.Succeeded}/{result.Total} アダプタ）。戻すには「↩ ぜんぶ元の状態に戻す」。"
                : $"⚠️ 一部のアダプタで変更できませんでした（{result.Succeeded}/{result.Total} 成功）。";
        }
        catch (UnauthorizedAccessException) when (HandleElevationNeeded("DNS手入力")) { }
        catch (SecurityException)            when (HandleElevationNeeded("DNS手入力")) { }
        catch (InvalidOperationException ex) when (LooksLikeAccessDenied(ex.Message) && HandleElevationNeeded("DNS手入力")) { }
        catch (Exception ex)
        {
            _dialogs.Error($"DNS の手入力適用に失敗しました：\n{ex.Message}", "失敗");
            StatusMessage = $"❌ DNS手入力失敗: {ex.Message}";
        }
    }

    /// <summary>手入力欄に有効な IP が1つでもあれば適用可（ボタン活性・3 欄いずれか）。</summary>
    private bool CanApplyCustomDns()
        => NetworkService.IsValidIpAddress(CustomDnsV4Primary)
        || NetworkService.IsValidIpAddress(CustomDnsV4Secondary)
        || NetworkService.IsValidIpAddress(CustomDnsV6);

    /// <summary>
    /// DNS 適用の対象アダプタを解決。アクティブ物理アダプタが無い／1枚も選択されていない場合は
    /// Info を出して null（呼び出し側は中止）。Adapters 未ロード時は全件にフォールバック。
    /// </summary>
    private List<AdapterInfo>? ResolveSelectedAdapters()
    {
        var active = _network.GetActivePhysicalAdapters();
        if (active.Count == 0)
        {
            _dialogs.Info(
                "DNS を変更できる有効なネットワークアダプタが見つかりません。\n" +
                "（Wi-Fi または有線がつながっているか確認してください）",
                "対象なし");
            StatusMessage = "ℹ️ DNS 変更対象のアダプタがありません。";
            return null;
        }

        if (Adapters.Count == 0) return active;  // Load 前フォールバック＝全件（従来挙動）

        var selectedIds = Adapters.Where(a => a.IsSelected).Select(a => a.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chosen = active.Where(a => selectedIds.Contains(a.Id)).ToList();
        if (chosen.Count == 0)
        {
            _dialogs.Info(
                "「対象アダプタ」が1つも選ばれていません。\n" +
                "くわしい設定の「対象アダプタ」で、変更したいアダプタにチェックを入れてください。",
                "アダプタ未選択");
            StatusMessage = "ℹ️ 対象アダプタが選ばれていません。";
            return null;
        }
        return chosen;
    }

    /// <summary>
    /// DoH（暗号化DNS）を ON/OFF（詳細層・v0.1.8）。対象＝選択アダプタの現行 DNS のうち既知 DoH プロバイダの IP。
    /// CommandParameter："on"＝有効化（DohFlags=1）／"off"＝解除。戻せる🟡・HKLM 書込で管理者要。
    /// </summary>
    [RelayCommand]
    private void ApplyDoh(string? mode)
    {
        if (_privilege.IsShutdownPending || GuardNicBusy()) return;
        var enable = mode != "off";

        if (!_network.IsDohSupportedHere())
        {
            _dialogs.Info(
                "お使いの Windows は暗号化DNS（DoH）に対応していません。\n（Windows 11 以降が必要です）",
                "非対応");
            StatusMessage = "ℹ️ この Windows は DoH 非対応です。";
            return;
        }

        var targets = ResolveSelectedAdapters();
        if (targets == null) return;  // 対象なし／未選択（中で Info 表示済み）
        var ids = targets.Select(a => a.Id).ToList();

        // 事前判定：既知 DoH プロバイダの現行 DNS が無ければ案内（エッジ1：DHCP/空で無風成功を防ぐ）。
        if (!_network.HasKnownDohDns(ids))
        {
            _dialogs.Info(
                enable
                    ? "暗号化（DoH）の対象になる DNS が見つかりません。\n\n" +
                      "先に「🌐 DNSを速くする（Cloudflare）」や詳細層で Cloudflare / Google / Quad9 に設定してください。\n" +
                      "その DNS のときだけ暗号化を ON にできます。"
                    : "解除できる暗号化DNS（DoH）の対象が見つかりません。\n\n" +
                      "これまでの変更をまとめて元に戻すには「↩ ぜんぶ元の状態に戻す」をお使いください。",
                "対象なし");
            StatusMessage = enable
                ? "ℹ️ 暗号化の対象 DNS がありません（先に Cloudflare/Google へ）。"
                : "ℹ️ 解除対象がありません（ぜんぶ戻すで復元可）。";
            return;
        }

        var ok = _dialogs.Confirm(
            enable
                ? "DNS の問い合わせを暗号化（DoH）します。\n\n" +
                  $"対象アダプタ：{string.Join("・", targets.Select(a => a.Name))}\n\n" +
                  "・通信内容を盗み見されにくくします（Cloudflare/Google/Quad9 のときだけ有効）。\n" +
                  "・うまくいかない時は自動でふつうの通信に戻ります（ネットは切れません）。\n" +
                  "・「↩ ぜんぶ元の状態に戻す」で元に戻せます。\n\n実行しますか？"
                : "DNS の暗号化（DoH）を解除して、ふつうの状態に戻します。\n\n" +
                  $"対象アダプタ：{string.Join("・", targets.Select(a => a.Name))}\n\n" +
                  "・「↩ ぜんぶ元の状態に戻す」で元に戻せます。\n\n実行しますか？",
            enable ? "暗号化DNS（DoH）をONにする" : "暗号化DNS（DoH）を解除");
        if (!ok) return;

        if (!_privilege.IsRunningAsAdministrator())
        {
            var choice = _dialogs.ConfirmElevation(enable ? "DoHをON" : "DoHを解除", isFromError: false);
            switch (choice)
            {
                case ElevationChoice.RestartAsAdmin: RequestRestartAsAdministrator(); return;
                case ElevationChoice.ContinueAnyway: break;
                case ElevationChoice.Cancel:
                default: return;
            }
        }

        try
        {
            var result = _network.ApplyDoh(ids, enable);
            RefreshStatus();
            if (result.NoTargets)
                StatusMessage = "ℹ️ 暗号化の対象 DNS がありませんでした（先に Cloudflare/Google へ）。";
            else
                StatusMessage = result.Succeeded == result.Total
                    ? (enable
                        ? $"🔒 暗号化DNS（DoH）を ON にしました（{result.Succeeded}/{result.Total}）。戻すには「↩ ぜんぶ元の状態に戻す」。"
                        : $"🔓 暗号化DNS（DoH）を解除しました（{result.Succeeded}/{result.Total}）。")
                    : $"⚠️ 一部の対象で変更できませんでした（{result.Succeeded}/{result.Total} 成功）。";
        }
        catch (UnauthorizedAccessException) when (HandleElevationNeeded("DoH 設定")) { }
        catch (SecurityException)            when (HandleElevationNeeded("DoH 設定")) { }
        catch (InvalidOperationException ex) when (LooksLikeAccessDenied(ex.Message) && HandleElevationNeeded("DoH 設定")) { }
        catch (Exception ex)
        {
            _dialogs.Error($"暗号化DNS（DoH）の変更に失敗しました：\n{ex.Message}", "失敗");
            StatusMessage = $"❌ DoH 変更失敗: {ex.Message}";
        }
    }

    /// <summary>
    /// 「不調を直す」＝DNS 解決キャッシュをクリア（ipconfig /flushdns）。実質可逆・管理者不要・退避不要。
    /// 完全リセット（winsock/int ip reset・不可逆）は v0.2.x へ後ろ倒し → 純正画面へ誘導。
    /// </summary>
    [RelayCommand]
    private void ResetNetwork()
    {
        if (_privilege.IsShutdownPending) return;

        var ok = _dialogs.Confirm(
            "DNS の名前解決キャッシュをクリアします（軽いリセット）。\n\n" +
            "・「さっきまで見られたサイトに繋がらない」等の不調が直ることがあります。\n" +
            "・設定は変わりません・再起動も不要です（すぐ作り直される安全な操作です）。\n\n" +
            "※ もっと深刻な不調には、上の「🪟 ネットの状態」から Windows 標準の『ネットワークのリセット』をお使いください。\n\n" +
            "実行しますか？",
            "不調を直す（DNSキャッシュのクリア）");
        if (!ok) return;

        try
        {
            _network.FlushDnsCache();
            StatusMessage = "🔧 DNS の名前解決キャッシュをクリアしました。";
        }
        catch (Exception ex)
        {
            _dialogs.Error($"キャッシュのクリアに失敗しました：\n{ex.Message}", "失敗");
            StatusMessage = $"❌ 失敗: {ex.Message}";
        }
    }

    /// <summary>
    /// 共通「↩ ぜんぶ元の状態に戻す」。退避済みの DNS・帯域抑制を原状へ。管理者要（HKLM/netsh）。
    /// </summary>
    [RelayCommand]
    private void RevertAll()
    {
        if (_privilege.IsShutdownPending || GuardNicBusy()) return;

        if (!_network.HasBackup())
        {
            _dialogs.Info(
                "元に戻す変更がありません。\n" +
                "（このツールで DNS・通信設定を変更すると、ここから戻せるようになります）",
                "戻す対象なし");
            StatusMessage = "ℹ️ 戻す対象の変更がありません。";
            return;
        }

        var ok = _dialogs.Confirm(
            "このツールで変更したネットワーク設定を、すべて元の状態に戻します。\n" +
            "（対象：DNS の変更・暗号化DNS(DoH)・「通信を軽くする」。ネットワークのリセットは元に戻せません）\n\n" +
            "実行しますか？",
            "ぜんぶ元の状態に戻す");
        if (!ok) return;

        if (!_privilege.IsRunningAsAdministrator())
        {
            var choice = _dialogs.ConfirmElevation("ぜんぶ元の状態に戻す", isFromError: false);
            switch (choice)
            {
                case ElevationChoice.RestartAsAdmin: RequestRestartAsAdministrator(); return;
                case ElevationChoice.ContinueAnyway: break;
                case ElevationChoice.Cancel:
                default: return;
            }
        }

        try
        {
            var result = _network.RevertAll();
            RefreshStatus();
            StatusMessage = result.Failed == 0
                ? $"↩ {result.Restored} 件の変更を元に戻しました。"
                : $"⚠️ {result.Restored} 件を戻し、{result.Failed} 件は戻せませんでした（管理者権限が必要かもしれません）。";
        }
        catch (UnauthorizedAccessException) when (HandleElevationNeeded("ぜんぶ元に戻す")) { }
        catch (SecurityException)            when (HandleElevationNeeded("ぜんぶ元に戻す")) { }
        catch (InvalidOperationException ex) when (LooksLikeAccessDenied(ex.Message) && HandleElevationNeeded("ぜんぶ元に戻す")) { }
        catch (Exception ex)
        {
            _dialogs.Error($"元に戻す処理に失敗しました：\n{ex.Message}", "失敗");
            StatusMessage = $"❌ 失敗: {ex.Message}";
        }
    }

    // ============ NIC省電力プリセット（v0.2.2・くわしい設定 内） ============

    /// <summary>NIC省電力操作が可能か（操作中でない）。DNS 等の同期操作との直列化は各コマンド先頭で相互チェック。</summary>
    private bool CanOperateNicPower() => !IsNicPowerBusy;

    /// <summary>
    /// NIC省電力操作中に他のネットワーク変更を弾く（🔧反映エッジ3：同一アダプタへの再初期化競合防止）。
    /// 操作中なら案内を出して true（呼び出し側は中止）。
    /// </summary>
    private bool GuardNicBusy()
    {
        if (!IsNicPowerBusy) return false;
        _dialogs.Info(
            "NICの省電力設定を変更中です。完了してから操作してください（数秒〜十数秒）。",
            "処理中");
        StatusMessage = "⏳ NIC省電力の処理中です。少しお待ちください。";
        return true;
    }

    /// <summary>
    /// 選択アダプタに「省電力OFFプリセット」を適用（EEE/ULPM 等を 0＋電源オフ許可を Disabled）。
    /// 🟡・要管理者・即時反映＝適用時に数秒の通信断。適用前に GUID 単位 first-touch 退避。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOperateNicPower))]
    private async System.Threading.Tasks.Task ApplyNicPowerSaving()
    {
        if (_privilege.IsShutdownPending || IsNicPowerBusy) return;

        var targets = ResolveSelectedAdapters();
        if (targets == null) return;   // 対象なし／未選択（中で Info 表示済み）

        // 🔴 締め出しガード（🔧反映ダメ出し2）：リモート（RDP）経由で NIC を触ると自分の接続が切れる。
        var remoteWarn = _rdp.IsCurrentSessionRemote()
            ? "\n\n⚠️ 今このPCに【リモートデスクトップ経由】で接続中のようです。\n" +
              "ネットワーク設定の変更でこの接続が切れる可能性が高いです。本当に続けますか？"
            : "";
        var confirmTitle = "NICの省電力をOFFにする（安定重視）";
        var body =
            "選んだアダプタの省電力機能をOFFにします。\n\n" +
            $"対象アダプタ：{string.Join("・", targets.Select(a => a.Name))}\n\n" +
            "・省電力イーサネット(EEE)等をOFFにし、「電力節約のためにオフにできる」のチェックも外します。\n" +
            "・ラグ・突然の切断対策に効きます（待機時の消費電力はわずかに増えます）。\n" +
            "・⚡ 適用の瞬間、アダプタが再初期化され【数秒だけ通信が途切れる】ことがあります。\n" +
            "・適用前の状態は自動で退避し、「↩ 省電力を元に戻す」でいつでも戻せます。\n\n" +
            "実行しますか？" + remoteWarn;

        // リモート時は破壊的確認（既定キャンセル）に切替。
        var ok = _rdp.IsCurrentSessionRemote()
            ? _dialogs.ConfirmDestructive(body, confirmTitle)
            : _dialogs.Confirm(body, confirmTitle);
        if (!ok) return;

        if (!_privilege.IsRunningAsAdministrator())
        {
            var choice = _dialogs.ConfirmElevation("NIC省電力OFF", isFromError: false);
            switch (choice)
            {
                case ElevationChoice.RestartAsAdmin: RequestRestartAsAdministrator(); return;
                case ElevationChoice.ContinueAnyway: break;
                case ElevationChoice.Cancel:
                default: return;
            }
        }

        IsNicPowerBusy = true;
        StatusMessage = "⏳ NICの省電力をOFFにしています…（一瞬通信が途切れる場合があります）";
        try
        {
            int ok2 = 0, partial = 0, failed = 0;
            string? lastMsg = null;
            foreach (var a in targets)
            {
                var (outcome, msg) = await System.Threading.Tasks.Task.Run(() =>
                {
                    var r = _nic.ApplyPowerSavingOff(a.Id, out var m);
                    return (r, m);
                });
                lastMsg = msg ?? lastMsg;
                switch (outcome)
                {
                    case NicPowerOutcome.Success: ok2++; break;
                    case NicPowerOutcome.PartiallyApplied: partial++; break;
                    default: failed++; break;
                }
            }
            RefreshNicPowerStatus();
            StatusMessage = BuildNicResultMessage("省電力OFF", targets.Count, ok2, partial, failed, lastMsg);
        }
        finally
        {
            IsNicPowerBusy = false;
        }
    }

    /// <summary>選択アダプタの NIC省電力を退避から原状復帰（「↩ 省電力を元に戻す」）。要管理者。</summary>
    [RelayCommand(CanExecute = nameof(CanOperateNicPower))]
    private async System.Threading.Tasks.Task RestoreNicPower()
    {
        if (_privilege.IsShutdownPending || IsNicPowerBusy) return;

        var targets = ResolveSelectedAdapters();
        if (targets == null) return;

        var restorable = targets.Where(a => _nic.HasBackup(a.Id)).ToList();
        if (restorable.Count == 0)
        {
            _dialogs.Info(
                "選んだアダプタに、省電力プリセットで戻せる変更がありません。\n" +
                "（このツールで省電力OFFを適用すると、ここから戻せるようになります）",
                "戻す対象なし");
            StatusMessage = "ℹ️ NIC省電力の戻す対象がありません。";
            return;
        }

        var ok = _dialogs.Confirm(
            "NICの省電力設定を、適用前の状態に戻します。\n\n" +
            $"対象アダプタ：{string.Join("・", restorable.Select(a => a.Name))}\n\n" +
            "・⚡ この操作でも数秒だけ通信が途切れることがあります。\n\n実行しますか？",
            "NIC省電力を元に戻す");
        if (!ok) return;

        if (!_privilege.IsRunningAsAdministrator())
        {
            var choice = _dialogs.ConfirmElevation("NIC省電力を元に戻す", isFromError: false);
            switch (choice)
            {
                case ElevationChoice.RestartAsAdmin: RequestRestartAsAdministrator(); return;
                case ElevationChoice.ContinueAnyway: break;
                case ElevationChoice.Cancel:
                default: return;
            }
        }

        IsNicPowerBusy = true;
        StatusMessage = "⏳ NICの省電力を元に戻しています…";
        try
        {
            int ok2 = 0, partial = 0, failed = 0;
            string? lastMsg = null;
            foreach (var a in restorable)
            {
                var (outcome, msg) = await System.Threading.Tasks.Task.Run(() =>
                {
                    var r = _nic.RestoreOriginal(a.Id, out var m);
                    return (r, m);
                });
                lastMsg = msg ?? lastMsg;
                switch (outcome)
                {
                    case NicPowerOutcome.Success: ok2++; break;
                    case NicPowerOutcome.PartiallyApplied: partial++; break;
                    default: failed++; break;
                }
            }
            RefreshNicPowerStatus();
            StatusMessage = BuildNicResultMessage("省電力を元に戻す", restorable.Count, ok2, partial, failed, lastMsg);
        }
        finally
        {
            IsNicPowerBusy = false;
        }
    }

    /// <summary>NIC 操作の集計メッセージ。全成功／一部／失敗を 1 行で。</summary>
    private static string BuildNicResultMessage(
        string label, int total, int ok, int partial, int failed, string? detail)
    {
        if (failed == 0 && partial == 0)
            return $"✅ {label}を適用しました（{ok}/{total} アダプタ）。";
        if (ok > 0 || partial > 0)
            return $"⚠️ {label}：{ok} 件成功・{partial} 件一部・{failed} 件失敗（{total} 中）。" +
                   (detail != null ? $"\n{detail}" : "");
        return $"❌ {label}に失敗しました（{total} アダプタ）。" + (detail != null ? $"\n{detail}" : "");
    }

    // ============ 昇格ヘルパー（StartupListViewModel から複製・M-1 で共通化予定） ============

    /// <summary>権限不足フォールバック。管理者でも拒否なら別原因を案内、非管理者なら昇格 3 択。</summary>
    private bool HandleElevationNeeded(string itemName)
    {
        if (_privilege.IsRunningAsAdministrator())
        {
            _dialogs.Error(
                $"「{itemName}」の操作に失敗しました。\n" +
                "管理者として実行中ですが書込が拒否されました。\n" +
                "グループポリシー・セキュリティソフトなどの可能性があります。",
                "操作失敗（管理者でも拒否）");
            StatusMessage = "❌ 操作失敗（管理者でも拒否）";
            return true;
        }

        var choice = _dialogs.ConfirmElevation(itemName, isFromError: true);
        if (choice == ElevationChoice.RestartAsAdmin)
            RequestRestartAsAdministrator();
        else
            StatusMessage = "❌ 操作失敗：管理者権限が必要";
        return true;
    }

    /// <summary>管理者として再起動。失敗時は Warn 表示。</summary>
    private void RequestRestartAsAdministrator()
    {
        if (_privilege.TryRestartAsAdministrator(out var err))
            _privilege.ShutdownCurrentProcess();
        else
        {
            _dialogs.Warn(err, "管理者起動できませんでした");
            StatusMessage = "❌ 管理者起動キャンセル";
        }
    }

    /// <summary>例外メッセージから「権限不足」を推測（英/日両ロケール）。</summary>
    private static bool LooksLikeAccessDenied(string? msg)
    {
        if (string.IsNullOrEmpty(msg)) return false;
        return msg.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Access denied", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("アクセスが拒否されました", StringComparison.Ordinal)
            || msg.Contains("Requires elevation", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("要求された操作には特権が必要", StringComparison.Ordinal)
            || msg.Contains("ERROR_ACCESS_DENIED", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>詳細層：DNS 適用対象アダプタのチェック項目（Phase 7c）。</summary>
public partial class AdapterSelect : ObservableObject
{
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }

    /// <summary>このアダプタを DNS 適用対象に含めるか（既定＝true＝従来の全件挙動）。</summary>
    [ObservableProperty]
    private bool _isSelected = true;

    public AdapterSelect(AdapterInfo info)
    {
        Id = info.Id;
        Name = info.Name;
        Description = info.Description;
    }
}
