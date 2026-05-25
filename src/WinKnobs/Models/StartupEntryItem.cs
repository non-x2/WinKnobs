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
    /// CheckBox 表示用：null は「有効扱い」として true で表示する。
    /// 設計書 3-2（Windows デフォルト挙動）に合わせ、CheckBox の中間状態を回避。
    /// </summary>
    public bool IsEnabledForDisplay => IsEnabled ?? true;

    /// <summary>IsEnabled が変わったら IsEnabledForDisplay の変更通知も流す（CheckBox 更新用）。</summary>
    partial void OnIsEnabledChanged(bool? value)
    {
        OnPropertyChanged(nameof(IsEnabledForDisplay));
    }

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

    // =================== R-2 cross-reviewer 致命15 対応：rollback 用 Original 保持 ===================

    /// <summary>編集前の Command（Load 直後 / 保存成功時に SnapshotOriginal で確定）。</summary>
    public string OriginalCommand { get; private set; } = string.Empty;

    /// <summary>編集前の ValueKind（同上）。</summary>
    public RegistryValueKind OriginalValueKind { get; private set; } = RegistryValueKind.String;

    /// <summary>Load 直後・保存成功直後に「現在値」を Original として確定する。</summary>
    public void SnapshotOriginal()
    {
        OriginalCommand   = Command;
        OriginalValueKind = ValueKind;
    }

    /// <summary>編集前の状態に戻す（Cancel・Save 失敗時の rollback 用）。</summary>
    public void RevertChanges()
    {
        Command   = OriginalCommand;
        ValueKind = OriginalValueKind;
        IsDirty   = false;
    }

    // =================== R-2 cross-reviewer 致命16 / plan致命2 対応：CheckBox 操作可否 ===================

    /// <summary>
    /// CheckBox（有効/無効トグル）を操作可能か。
    /// false になる条件：
    ///   1) RunOnce 系（StartupApproved 連動なし・タスクマネージャーも非表示）
    ///   2) IsDirty 中（編集と並行操作で IsDirty 整合性破綻するため）
    /// </summary>
    public bool CanToggleEnabled
    {
        get
        {
            if (Hive is StartupHive.HKLM_RunOnce
                     or StartupHive.HKCU_RunOnce
                     or StartupHive.HKLM_Wow64_RunOnce)
                return false;
            if (IsDirty) return false;
            return true;
        }
    }

    /// <summary>CheckBox のツールチップ（CanToggleEnabled の理由を表示）。</summary>
    public string CheckBoxTooltip
    {
        get
        {
            if (Hive is StartupHive.HKLM_RunOnce
                     or StartupHive.HKCU_RunOnce
                     or StartupHive.HKLM_Wow64_RunOnce)
                return "RunOnce 系は次回起動後に自動削除されるため、StartupApproved 連動非対応です。";
            if (IsDirty)
                return "保存していない編集があります。\n先に 💾保存 または ↺戻す を実行してください。";
            return "クリックで有効/無効を即時切替（StartupApproved 連動）";
        }
    }

    /// <summary>IsDirty 変更時に CanToggleEnabled / CheckBoxTooltip も再評価。</summary>
    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanToggleEnabled));
        OnPropertyChanged(nameof(CheckBoxTooltip));
    }

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
