using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WinKnobs.Models;

namespace WinKnobs.ViewModels.Startup;

/// <summary>
/// スタートアップエントリ編集ダイアログの ViewModel。
/// Phase R-2 実装。
///
/// R-2 スコープ：
///   ✅ Command（起動コマンド）の編集
///   ✅ ValueKind（REG_SZ / REG_EXPAND_SZ）の切替
///   ❌ Name 変更（R-3 で削除→追加として実装予定・整合性破壊リスク回避）
///
/// 設計書 4-3 / 5-3。
/// </summary>
public partial class StartupEntryEditViewModel : ObservableObject
{
    /// <summary>編集対象の元エントリ（OK 時に書き戻す参照）。</summary>
    private StartupEntryItem? _source;

    /// <summary>
    /// 表示用：エントリ名（読取専用・R-2 では編集不可）。
    /// </summary>
    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>
    /// 表示用：ハイブ名（読取専用）。
    /// </summary>
    [ObservableProperty]
    private string _hiveDisplay = string.Empty;

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
    /// </summary>
    public bool IsModified =>
        EditableCommand != _originalCommand
        || (IsRegSz ? RegistryValueKind.String : RegistryValueKind.ExpandString) != _originalKind;

    /// <summary>
    /// バリデーション結果（OK ボタンの有効/無効に使用）。
    /// </summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(EditableCommand);

    /// <summary>
    /// ダイアログを閉じるためのフラグ（View 側で DialogResult として参照）。
    /// </summary>
    [ObservableProperty]
    private bool? _dialogResult;

    /// <summary>
    /// ステータスメッセージ（バリデーション結果や注意書きを表示）。
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "コマンドを編集して OK を押してください。Name 変更は R-3 で対応予定です。";

    /// <summary>
    /// 編集対象エントリを設定して、編集用フィールドを初期化する。
    /// </summary>
    public void Load(StartupEntryItem source)
    {
        _source = source;

        DisplayName = source.Name;
        HiveDisplay = source.HiveDisplay;

        EditableCommand = source.Command;
        _originalCommand = source.Command;

        _originalKind = source.ValueKind;
        IsRegSz       = source.ValueKind == RegistryValueKind.String;
        IsRegExpandSz = source.ValueKind == RegistryValueKind.ExpandString;
    }

    partial void OnEditableCommandChanged(string value)
    {
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(IsModified));
        StatusMessage = string.IsNullOrWhiteSpace(value)
            ? "⚠️ コマンドが空です。OK は無効化されています。"
            : (IsModified
                ? "✏️ 編集中：OK を押すと一覧に反映され、💾保存 で永続化されます。"
                : "（変更ありません）");
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

    [RelayCommand]
    private void Ok()
    {
        if (_source == null || !IsValid) return;

        _source.Command   = EditableCommand;
        _source.ValueKind = IsRegSz ? RegistryValueKind.String : RegistryValueKind.ExpandString;
        _source.IsDirty   = true;

        DialogResult = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
    }
}
