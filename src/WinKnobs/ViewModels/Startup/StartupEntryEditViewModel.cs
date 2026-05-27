using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WinKnobs.Models;

namespace WinKnobs.ViewModels.Startup;

/// <summary>
/// スタートアップエントリ編集ダイアログの ViewModel。
/// Phase R-2：既存エントリの Command / ValueKind 編集。
/// Phase R-3：Add モード追加（IsNew=true で Hive 選択 + Name 入力 enable）。
///
/// 設計書 4-3 / 5-3。
/// </summary>
public partial class StartupEntryEditViewModel : ObservableObject
{
    /// <summary>編集対象の元エントリ（Edit モード時のみ・OK 時に書き戻す参照）。</summary>
    private StartupEntryItem? _source;

    /// <summary>
    /// R-3 cross-reviewer 致命高1 対応：Add モードで「他ハイブ重複」を入力中にリアルタイム判定するためのコールバック。
    /// 戻り値：(同一ハイブ内重複, 他ハイブ重複の HiveDisplay or null)
    /// 注入方法：LoadNew 呼び元（StartupListViewModel.Add）が現在の Entries を見て関数を渡す。
    /// </summary>
    private Func<StartupHive, string, (bool sameHiveDup, string? otherHiveDup)>? _nameConflictChecker;

    /// <summary>新規追加モードか（R-3 追加）。true なら Hive ComboBox + Name TextBox を enable。</summary>
    [ObservableProperty]
    private bool _isNewMode;

    /// <summary>
    /// 編集中の名前。Add モード時のみ TextBox で入力可能、Edit モードでは Load() で受け取って読取専用扱い。
    /// </summary>
    [ObservableProperty]
    private string _editableName = string.Empty;

    /// <summary>
    /// 表示用：エントリ名（Edit モードの DisplayName 用。Add モードでは未使用）。
    /// </summary>
    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>
    /// 表示用：ハイブ名（Edit モードでは読取専用文字列）。
    /// </summary>
    [ObservableProperty]
    private string _hiveDisplay = string.Empty;

    /// <summary>Add モードで選択可能なハイブ候補（ComboBox バインド）。</summary>
    public IReadOnlyList<StartupHive> SelectableHives { get; } = new[]
    {
        StartupHive.HKCU_Run,
        StartupHive.HKCU_RunOnce,
        StartupHive.HKLM_Run,
        StartupHive.HKLM_RunOnce,
        // WOW64 系は 32bit OS 互換用・通常運用では選ばないので最後
        StartupHive.HKLM_Wow64_Run,
        StartupHive.HKLM_Wow64_RunOnce
    };

    /// <summary>Add モード時の選択ハイブ（既定は HKCU_Run）。</summary>
    [ObservableProperty]
    private StartupHive _selectedHive = StartupHive.HKCU_Run;

    /// <summary>
    /// 編集中の Command（起動コマンド）。
    /// </summary>
    [ObservableProperty]
    private string _editableCommand = string.Empty;

    /// <summary>
    /// 編集前の Command（差分検出用）。
    /// </summary>
    private string _originalCommand = string.Empty;

    /// <summary>
    /// REG_SZ ラジオの状態（true なら REG_SZ）。
    /// </summary>
    [ObservableProperty]
    private bool _isRegSz = true;

    /// <summary>
    /// REG_EXPAND_SZ ラジオの状態（true なら REG_EXPAND_SZ）。
    /// </summary>
    [ObservableProperty]
    private bool _isRegExpandSz;

    /// <summary>
    /// 編集前の ValueKind（差分検出用）。
    /// </summary>
    private RegistryValueKind _originalKind = RegistryValueKind.String;

    /// <summary>
    /// 編集中フラグ（OK 押下時の判断用）。
    /// Add モードは常に IsModified=true として扱う（新規追加 = 必ず変更あり）。
    /// </summary>
    public bool IsModified =>
        IsNewMode
        || EditableCommand != _originalCommand
        || (IsRegSz ? RegistryValueKind.String : RegistryValueKind.ExpandString) != _originalKind;

    /// <summary>
    /// バリデーション結果（OK ボタンの有効/無効に使用）。
    /// Add モードでは Name も必須 + 同一ハイブ重複（R-3 cross-reviewer 致命高1）をブロック。
    /// </summary>
    public bool IsValid
    {
        get
        {
            if (string.IsNullOrWhiteSpace(EditableCommand)) return false;
            if (IsNewMode && string.IsNullOrWhiteSpace(EditableName)) return false;
            if (IsNewMode && EditableName.Length > 255) return false;
            if (IsNewMode && HasSameHiveNameConflict) return false;  // R-3 cross-reviewer 致命高1
            return true;
        }
    }

    /// <summary>
    /// ダイアログを閉じるためのフラグ（View 側で DialogResult として参照）。
    /// </summary>
    [ObservableProperty]
    private bool? _dialogResult;

    /// <summary>
    /// ステータスメッセージ（バリデーション結果や注意書きを表示）。
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "コマンドを編集して OK を押してください。";

    /// <summary>
    /// Edit モード初期化：既存エントリの編集用フィールド設定。
    /// </summary>
    public void Load(StartupEntryItem source)
    {
        _source = source;
        IsNewMode = false;

        DisplayName = source.Name;
        EditableName = source.Name;       // 表示のみ
        HiveDisplay = source.HiveDisplay;
        SelectedHive = source.Hive;        // 読取専用扱い

        EditableCommand = source.Command;
        _originalCommand = source.Command;

        _originalKind = source.ValueKind;
        IsRegSz       = source.ValueKind == RegistryValueKind.String;
        IsRegExpandSz = source.ValueKind == RegistryValueKind.ExpandString;

        StatusMessage = "コマンドを編集して OK を押してください。Name 変更は v0.2 候補。";
    }

    /// <summary>
    /// R-3：Add モード初期化。Hive プリセット + Name/Command 空で開く。
    /// R-3 cross-reviewer 致命高1 対応：Name 重複チェック関数を受け取って、入力中にリアルタイム警告を出せるようにする。
    /// </summary>
    public void LoadNew(
        StartupHive defaultHive,
        Func<StartupHive, string, (bool sameHiveDup, string? otherHiveDup)>? nameConflictChecker = null)
    {
        _source = null;
        IsNewMode = true;
        _nameConflictChecker = nameConflictChecker;

        DisplayName = string.Empty;
        EditableName = string.Empty;
        SelectedHive = defaultHive;
        HiveDisplay = HiveDisplayOf(defaultHive);

        EditableCommand = string.Empty;
        _originalCommand = string.Empty;

        _originalKind = RegistryValueKind.String;
        IsRegSz = true;
        IsRegExpandSz = false;

        StatusMessage = "新規スタートアップエントリを作成します。Name と Command を入力してください。";
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(IsModified));
    }

    /// <summary>選択されたハイブから表示用文字列を生成（LoadNew 用）。</summary>
    private static string HiveDisplayOf(StartupHive hive) => hive switch
    {
        StartupHive.HKLM_Run           => @"HKLM\Run",
        StartupHive.HKLM_RunOnce       => @"HKLM\RunOnce",
        StartupHive.HKCU_Run           => @"HKCU\Run",
        StartupHive.HKCU_RunOnce       => @"HKCU\RunOnce",
        StartupHive.HKLM_Wow64_Run     => @"HKLM\WOW64\Run",
        StartupHive.HKLM_Wow64_RunOnce => @"HKLM\WOW64\RunOnce",
        _ => hive.ToString()
    };

    partial void OnSelectedHiveChanged(StartupHive value)
    {
        // Add モード時に Hive 表示文字列を追従更新（HiveDisplay は読取専用 TextBlock も流用するため）
        if (IsNewMode)
        {
            HiveDisplay = HiveDisplayOf(value);
            UpdateNameConflictStatus();  // Hive 変更で重複判定も変わる
            OnPropertyChanged(nameof(IsValid));
        }
    }

    /// <summary>
    /// R-3 cross-reviewer 致命高1 対応：Add モード入力中に他ハイブ重複をリアルタイム判定。
    /// 同一ハイブ重複：IsValid=false でブロック / 他ハイブ重複：警告表示のみ（OK は通る）。
    /// </summary>
    private void UpdateNameConflictStatus()
    {
        if (!IsNewMode || _nameConflictChecker == null) return;
        if (string.IsNullOrWhiteSpace(EditableName)) return;

        var (same, other) = _nameConflictChecker(SelectedHive, EditableName.Trim());
        if (same)
        {
            StatusMessage = $"⚠️ 値名 '{EditableName.Trim()}' は既に {HiveDisplay} に存在します。OK は無効化されています。";
        }
        else if (!string.IsNullOrEmpty(other))
        {
            StatusMessage = $"⚠️ 値名 '{EditableName.Trim()}' は別ハイブ {other} に存在します。\n（OK で続行可能・混乱の可能性に注意）";
        }
    }

    /// <summary>R-3 cross-reviewer 致命高1：IsValid 判定に「同一ハイブ重複」を追加。</summary>
    public bool HasSameHiveNameConflict
    {
        get
        {
            if (!IsNewMode || _nameConflictChecker == null) return false;
            if (string.IsNullOrWhiteSpace(EditableName)) return false;
            var (same, _) = _nameConflictChecker(SelectedHive, EditableName.Trim());
            return same;
        }
    }

    partial void OnEditableCommandChanged(string value)
    {
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(IsModified));
        StatusMessage = string.IsNullOrWhiteSpace(value)
            ? "⚠️ コマンドが空です。OK は無効化されています。"
            : (IsNewMode
                ? "🆕 新規エントリ：OK で一覧に追加されます。"
                : (IsModified
                    ? "✏️ 編集中：OK を押すと一覧に反映され、💾保存 で永続化されます。"
                    : "（変更ありません）"));
    }

    partial void OnEditableNameChanged(string value)
    {
        OnPropertyChanged(nameof(HasSameHiveNameConflict));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(IsModified));
        if (IsNewMode)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                StatusMessage = "⚠️ 名前が空です。OK は無効化されています。";
            }
            else if (value.Length > 255)
            {
                StatusMessage = $"⚠️ 名前が長すぎます（{value.Length} 文字・最大 255）。";
            }
            else
            {
                // R-3 cross-reviewer 致命高1：Hive と Name の組合せで重複チェック
                UpdateNameConflictStatus();
                if (!HasSameHiveNameConflict && _nameConflictChecker != null)
                {
                    var (_, other) = _nameConflictChecker(SelectedHive, value.Trim());
                    if (string.IsNullOrEmpty(other))
                    {
                        // 重複なし
                        StatusMessage = "🆕 新規エントリ：OK で一覧に追加されます。";
                    }
                }
            }
        }
    }

    partial void OnIsRegSzChanged(bool value)
    {
        if (value) IsRegExpandSz = false;
        OnPropertyChanged(nameof(IsModified));
    }

    partial void OnIsRegExpandSzChanged(bool value)
    {
        if (value) IsRegSz = false;
        OnPropertyChanged(nameof(IsModified));
    }

    /// <summary>R-3 追加：Add モードで作成したエントリを取得（DialogResult=true 後に呼び出し）。</summary>
    public StartupEntryItem? CreateNewEntry()
    {
        if (!IsNewMode) return null;
        var kind = IsRegSz ? RegistryValueKind.String : RegistryValueKind.ExpandString;
        var entry = new StartupEntryItem
        {
            Hive = SelectedHive,
            Name = EditableName.Trim(),
            RawName = EditableName.Trim(),  // Add 時は RunOnce プレフィックスを含めるかをユーザーに任せる（Name=RawName）
            Command = EditableCommand,
            ValueKind = kind,
            IsEnabled = true,               // 新規は有効でスタート（Windows デフォルト）
            IsNew = true,
            StartupApprovedTrailing = null
        };
        // OriginalCommand を空でセットしておく（Save 時の楽観ロックは Add では使わないが安全のため）
        entry.SnapshotOriginal();
        return entry;
    }

    [RelayCommand]
    private void Ok()
    {
        if (!IsValid) return;

        // Add モード：エントリ作成は呼び元の Add コマンドが CreateNewEntry() で取得
        if (IsNewMode)
        {
            DialogResult = true;
            return;
        }

        // Edit モード：既存ロジック維持（R-2 hotfix 1：無変更 no-op で IsDirty 残留回避）
        if (_source == null) return;

        if (IsModified)
        {
            _source.Command   = EditableCommand;
            _source.ValueKind = IsRegSz ? RegistryValueKind.String : RegistryValueKind.ExpandString;
            _source.IsDirty   = true;
        }

        DialogResult = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
    }
}
