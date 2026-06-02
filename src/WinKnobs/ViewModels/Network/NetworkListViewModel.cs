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

    [ObservableProperty]
    private string _statusMessage =
        "ネットワーク設定。「かんたん設定」または「Windowsの画面を開く」から操作できます。";

    // ── 今の状態 ──
    [ObservableProperty] private string _dnsSummary = "（Phase 4 で表示）";
    [ObservableProperty] private string _connectionSummary = "—";
    [ObservableProperty] private string _deliveryOptimizationSummary = "（読み込み中）";

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

    public NetworkListViewModel(
        ExternalLauncherService launcher,
        DialogService dialogs,
        NetworkService network,
        PrivilegeService privilege)
    {
        _launcher = launcher;
        _dialogs = dialogs;
        _network = network;
        _privilege = privilege;
    }

    /// <summary>
    /// カテゴリ選択時に MainViewModel から呼ばれる。「今の状態」を読み直す。
    /// </summary>
    public void Load()
    {
        RefreshStatus();
        RefreshAdapters();
        StatusMessage = "ネットワーク設定を開きました。「Windowsの画面を開く」はすぐに使えます。";
    }

    /// <summary>「今の状態」（配信最適化・DNS）を読み直してプロパティへ反映。</summary>
    private void RefreshStatus()
    {
        try { DeliveryOptimizationSummary = _network.GetDeliveryOptimizationSummary(); }
        catch { DeliveryOptimizationSummary = "（取得できません）"; }
        try { DnsSummary = _network.GetDnsSummary(); }
        catch { DnsSummary = "（取得できません）"; }
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
        if (_privilege.IsShutdownPending) return;

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
        if (_privilege.IsShutdownPending) return;

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
        if (_privilege.IsShutdownPending) return;

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
        if (_privilege.IsShutdownPending) return;

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
            "（対象：DNS の変更・「通信を軽くする」。ネットワークのリセットは元に戻せません）\n\n" +
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
