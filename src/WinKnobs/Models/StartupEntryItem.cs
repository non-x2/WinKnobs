using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;

namespace WinKnobs.Models;

/// <summary>
/// スタートアップエントリの 1 件分。
/// Run キー族のレジストリ値と StartupApproved の有効/無効を 1 つにまとめたモデル。
/// 設計書 3-1 参照。
/// </summary>
public partial class StartupEntryItem : ObservableObject
{
    /// <summary>
    /// 表示用の名前（= レジストリ値名から RunOnce プレフィックス `!` `*` を除いたもの）。
    /// 編集 UI で見せるのはこちら。
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// 生のレジストリ値名（RunOnce 用プレフィックス `!`/`*` 込）。
    /// 書込時はこちらを使う（プレフィックスを維持）。設計書 3-3 エッジ#1。
    /// </summary>
    [ObservableProperty]
    private string _rawName = string.Empty;

    /// <summary>起動コマンド（実行ファイルパス＋引数）。REG_SZ または REG_EXPAND_SZ。</summary>
    [ObservableProperty]
    private string _command = string.Empty;

    /// <summary>所属ハイブ（読取専用・enum で表現）。</summary>
    public StartupHive Hive { get; init; }

    /// <summary>
    /// StartupApproved の有効/無効。
    /// null = StartupApproved 未登録 = 有効扱い（Windows デフォルト挙動）。
    /// </summary>
    [ObservableProperty]
    private bool? _isEnabled;

    /// <summary>
    /// REG_SZ か REG_EXPAND_SZ かの区別（書込時に保持）。
    /// 既存値を上書きする時は元の Kind を必ず維持（環境変数展開バグ防止・設計書 3-3 エッジ#4）。
    /// </summary>
    public RegistryValueKind ValueKind { get; set; } = RegistryValueKind.String;

    /// <summary>
    /// StartupApproved の byte[1..11]（MS未公開・タイムスタンプ等）。
    /// 既存値を保持してトグル時の byte[0] のみ書換に使う。設計書 3-2 / 3-3。
    /// </summary>
    public byte[]? StartupApprovedTrailing { get; set; }

    /// <summary>新規追加された未保存エントリか（保存ボタンで永続化）。</summary>
    public bool IsNew { get; set; }

    /// <summary>編集中フラグ（オレンジ枠表示用・R-2 以降）。</summary>
    [ObservableProperty]
    private bool _isDirty;

    /// <summary>表示用：ハイブを短い日本語にする。</summary>
    public string HiveDisplay => Hive switch
    {
        StartupHive.HKLM_Run           => @"HKLM\Run",
        StartupHive.HKLM_RunOnce       => @"HKLM\RunOnce",
        StartupHive.HKCU_Run           => @"HKCU\Run",
        StartupHive.HKCU_RunOnce       => @"HKCU\RunOnce",
        StartupHive.HKLM_Wow64_Run     => @"HKLM\WOW64\Run",
        StartupHive.HKLM_Wow64_RunOnce => @"HKLM\WOW64\RunOnce",
        _ => Hive.ToString()
    };

    /// <summary>表示用：危険度マーク（HKLM 系は管理者権限要のため黄色）。</summary>
    public string RiskMark => Hive is StartupHive.HKLM_Run
                                    or StartupHive.HKLM_RunOnce
                                    or StartupHive.HKLM_Wow64_Run
                                    or StartupHive.HKLM_Wow64_RunOnce
        ? "🟡"
        : "🟢";
}
