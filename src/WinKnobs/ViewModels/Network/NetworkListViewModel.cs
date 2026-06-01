using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinKnobs.Services;

namespace WinKnobs.ViewModels.Network;

/// <summary>
/// ネットワーク・カテゴリ専用ペインの ViewModel（v0.1.7・設計ドラフト 4-3）。
/// 三層構造（設計原則1）：A 初心者（かんたん設定＋純正ランチャー）／B 中級（▸ くわしい設定）／C 上級（v0.2.2）。
///
/// 実装フェーズ：
///   Phase 1（骨格）：状態表示プレースホルダ＋コマンド枠。
///   Phase 2（実装済）：機能0 Windows 純正画面ランチャー。
///   Phase 3-7：帯域抑制（DODownloadMode）／DNS 切替（netsh）／ぜんぶ戻す／軽いリセット／詳細層。
///
/// 複製元：StartupListViewModel（昇格・退避・確認ダイアログ等は各機能の実装フェーズで複製）。
/// </summary>
public partial class NetworkListViewModel : ObservableObject
{
    private readonly ExternalLauncherService _launcher;
    private readonly DialogService _dialogs;

    [ObservableProperty]
    private string _statusMessage =
        "ネットワーク設定。「かんたん設定」または「Windowsの画面を開く」から操作できます。";

    // ── 今の状態（Phase 3/4 で実値読込・Phase 1 はプレースホルダ）──
    [ObservableProperty] private string _dnsSummary = "（読み込み中）";
    [ObservableProperty] private string _connectionSummary = "—";
    [ObservableProperty] private string _deliveryOptimizationSummary = "—";

    /// <summary>▸ くわしい設定 Expander の開閉（既定：閉じる＝初心者の視界から隠す・設計原則1）。</summary>
    [ObservableProperty]
    private bool _isAdvancedExpanded;

    public NetworkListViewModel(ExternalLauncherService launcher, DialogService dialogs)
    {
        _launcher = launcher;
        _dialogs = dialogs;
    }

    /// <summary>
    /// カテゴリ選択時に MainViewModel から呼ばれる。
    /// Phase 3/4 で「今の状態」（DNS・配信最適化）の実値読込を実装する。
    /// </summary>
    public void Load()
    {
        // Phase 1：実状態の読込は未実装（ペイン表示の骨格確認用プレースホルダ）。
        StatusMessage = "ネットワーク設定を開きました。「Windowsの画面を開く」はすぐに使えます。";
    }

    // ============ Phase 2：機能0 Windows 純正画面ランチャー（実装済） ============

    /// <summary>
    /// Windows 純正のネットワーク画面を開く（開くだけ・設定値は変更しない・管理者不要・危険度🟢）。
    /// CommandParameter：status / wifi / adapters / sharing / proxy / vpn。
    /// 設計原則7「抱え込まず純正へ逃がす」の安全弁。
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

    // ============ Phase 3-7：かんたん設定（各フェーズで実装）============
    // 現在は骨格スタブ。実装時にこのメソッド本体を各機能のロジックへ差し替える。

    /// <summary>機能3 バックグラウンド帯域抑制（配信最適化 P2P オフ）。Phase 3 で実装。</summary>
    [RelayCommand]
    private void SetBandwidth() => NotifyComingSoon("通信を軽くする（配信最適化）");

    /// <summary>機能1 DNS プロファイル切替。Phase 4 で実装。</summary>
    [RelayCommand]
    private void ApplyDns() => NotifyComingSoon("DNSを速くする");

    /// <summary>機能2 ネットワーク不調リセット（軽い＝flushdns）。Phase 6 で実装。</summary>
    [RelayCommand]
    private void ResetNetwork() => NotifyComingSoon("不調を直す（DNSキャッシュのクリア）");

    /// <summary>共通「ぜんぶ元に戻す」。Phase 5 で実装。</summary>
    [RelayCommand]
    private void RevertAll() => NotifyComingSoon("ぜんぶ元の状態に戻す");

    /// <summary>骨格スタブ用：未実装の機能を押された時の案内。</summary>
    private void NotifyComingSoon(string feature)
        => StatusMessage = $"「{feature}」はこの後のフェーズで有効になります（現在は骨格を実装中）。";
}
