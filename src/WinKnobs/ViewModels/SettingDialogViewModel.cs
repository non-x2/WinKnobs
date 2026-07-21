using System.IO;
using System.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using WinKnobs.Models;
using WinKnobs.Services;

namespace WinKnobs.ViewModels;

/// <summary>
/// 個別項目編集ダイアログの ViewModel。
///
/// 採用済みの plan-validator 指摘（F-1〜F-8 / E-1〜E-6 / G）：
/// - F-1：BackupService のヘッダ検証付き ExportKey を呼ぶ
/// - F-2：DialogService 経由（直接 MessageBox 呼ばない）
/// - F-6：RestorePointResult による分岐
/// - F-8：RegistryPath / RegistryValueName が null の項目を弾く
/// - E-2：LastBackupPath をプロパティ保持して RestoreCommand で再利用
///
/// A3-2-c 改訂（plan-validator V-1〜V-9 / VE-1〜VE-6）：
/// - V-2：DoApply に Binary / Command ケース追加
/// - V-5：OriginalValue / EditableValue の三者同期
/// - V-7：HasUnappliedChanges → IsModified にリネーム
/// - V-9：byte[] 16進文字列パース / WriteBinary 利用
/// - VE-6：RestoreSetting に File.Exists チェック
///
/// UX 改善：
/// - 「現在値（読み取り専用）」と「新しい値（編集可）」の 2 行構造を統合
/// - 1 つの編集 UI（ComboBox/TextBox）で OriginalValue を直接編集
/// - IsModified で「編集中（適用前）」状態を視覚化
///
/// v0.4.10（バックログ#7・plan-validator 2026-07-21 精査済み）：
/// - Command 型戻しトークンを BackupService（command_tokens.json）へ永続化。
///   設定画面を閉じても「戻す」が使えるようになる（従来は _lastCommandToken フィールドのみで揮発）。
/// - トークン保存タイミングは Apply 成功後（Apply 例外時に幽霊トークンを残さない・指摘1）
/// - Load 時にトークンが残っていれば IsApplied=true にして「戻す」を活性化（信頼境界・指摘4）
///
/// v0.4.11（バックログ#9・plan-validator 2026-07-21 精査済み）：
/// - レジストリ型 LastBackupPath（適用時 ExportKey した .reg パス）を BackupService
///   （registry_backup_paths.json）へ横展開して永続化。Command 型と同じく Apply 成功後に保存し、
///   Load 時に File.Exists を確認して復元（stale なら掃除して IsApplied は立てない）。
///   latest-wins（Command 型の first-touch-wins とは意図的に異なる＝BackupService 側 doc コメント参照）。
///
/// v0.4.12（バックログ#10・plan-validator 2026-07-21 精査済み）：
/// - Command 型の永続トークンに保存時刻（SavedAt）が付いたため、Load 時に StatusMessage へ鮮度表示を
///   差し込む（時刻あり：「◯月◯日 HH:mm適用時の状態に戻せます。」／時刻なし＝旧形式引き継ぎ：
///   「前回適用時のバックアップを復元できます（適用日時は記録されていません）」）。
///   レジストリ型は鮮度表示スコープ外（既存文言を維持）。
/// </summary>
public partial class SettingDialogViewModel : ObservableObject
{
    private readonly RegistryService _registry;
    private readonly BackupService _backup;
    private readonly RestorePointService _restore;
    private readonly IServiceProvider _services;
    private SettingItem? _item;

    /// <summary>A3-2-c：Command 型項目の Backup から受け取った復元用トークン。</summary>
    private string? _lastCommandToken;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _riskMark = "🟢";

    /// <summary>A3-2-c：起動時にスナップショットした「元の値」（編集不可）。</summary>
    [ObservableProperty] private string _originalValue = "";
    /// <summary>A3-2-c：元の値の UX 表示（"ON（有効）" 等）。</summary>
    [ObservableProperty] private string _originalValueDisplay = "";

    /// <summary>A3-2-c：ユーザー編集中の値（バインド先）。Boolean は "0"/"1"、Binary は "00 00 00 00"、Command は選択肢ラベル。</summary>
    [ObservableProperty] private string _editableValue = "";
    /// <summary>A3-2-c：編集中の値の UX 表示。</summary>
    [ObservableProperty] private string _editableValueDisplay = "";

    [ObservableProperty] private string _recommendedValue = "";
    [ObservableProperty] private string _defaultValue = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isApplied;
    [ObservableProperty] private string _registryPathDisplay = "";

    // 既存：ValueType ベースの編集UI切替
    [ObservableProperty] private SettingValueType _valueType = SettingValueType.String;
    /// <summary>A3-2-c：Boolean/Command 用 ComboBox SelectedIndex。</summary>
    [ObservableProperty] private int _editableBoolIndex;

    /// <summary>A3-2-c：編集中フラグ（EditableValue != OriginalValue）。</summary>
    [ObservableProperty] private bool _isModified;

    /// <summary>A3-2-c：再起動の粒度表示用ヒント（フッターに表示）。</summary>
    [ObservableProperty] private string _restartHint = "";

    /// <summary>A3-2-c：Command 型の選択肢（ComboBox.ItemsSource）。</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> Choices { get; } = new();

    /// <summary>A3-2-c：Boolean 型の選択肢（OFF/ON 固定）。</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> BooleanChoices { get; } = new()
    {
        "OFF（無効）", "ON（有効）"
    };

    /// <summary>E-2 対策：直前のバックアップパスを保持して RestoreCommand で参照。</summary>
    public string? LastBackupPath { get; private set; }

    /// <summary>OK ボタン押下で適用成功時に呼ぶ閉じる動作（View 側で設定）。</summary>
    public Action? CloseAction { get; set; }

    public SettingDialogViewModel(
        RegistryService registry,
        BackupService backup,
        RestorePointService restore,
        IServiceProvider services)
    {
        _registry = registry;
        _backup = backup;
        _restore = restore;
        _services = services;
    }

    // 循環依存回避のため遅延解決
    private DialogService Dialogs => _services.GetRequiredService<DialogService>();
    private ExternalLauncherService Launcher => _services.GetRequiredService<ExternalLauncherService>();
    // v0.1.4 BUG-3：管理者判定 + UAC 自己再起動
    private PrivilegeService Privilege => _services.GetRequiredService<PrivilegeService>();

    /// <summary>項目を読み込んでダイアログを初期化。</summary>
    public void Load(SettingItem item)
    {
        _item = item;
        Title = item.Title;
        Description = item.Description;
        RiskMark = item.RiskMark;
        ValueType = item.ValueType;
        RestartHint = item.RestartScope.ToHint();

        RecommendedValue = FormatDisplay(item, item.RecommendedValue, isRecommended: true);
        DefaultValue = FormatDisplay(item, item.DefaultValue, isRecommended: false);
        LastBackupPath = null;
        _lastCommandToken = null;
        IsApplied = false;

        // バックログ#7（plan-validator指摘4）：Command 型は前回適用時のトークンを永続ストアから復元。
        // トークン存在＝適用済みとみなす信頼境界（外部で手動復元済みでも「戻す」ボタンが出てしまい得る）。
        // 変数名は cross-reviewer ⚪指摘反映：LastBackupPath（レジストリ型の.reg存在）と紛らわしいため
        // 「command_tokens.json から読めた」ことを明示する名前にした。
        bool hasPersistedCommandToken = false;
        // v0.4.12（バックログ#10）：鮮度表示用。null＝旧形式（v0.4.10）引き継ぎで保存時刻が記録されていない。
        DateTimeOffset? commandTokenSavedAt = null;
        if (item.ValueType == SettingValueType.Command && item.Command?.CanRestore == true)
        {
            var savedToken = _backup.LoadCommandToken(item.Id);
            if (savedToken != null)
            {
                _lastCommandToken = savedToken;
                IsApplied = true;
                hasPersistedCommandToken = true;
                commandTokenSavedAt = _backup.LoadCommandTokenSavedAt(item.Id);
            }
        }

        // バックログ#9（plan-validator指摘）：レジストリ型は LastBackupPath（適用時 ExportKey した .reg パス）を
        // 永続ストアから復元。Command 型とは ValueType で排他（両立ケースはない）。
        // 信頼境界：外部（regedit・他ツール）で値が変更済みでも「戻す」ボタンが出てしまい得る（現在値との突合は
        // しない割り切り）。ここでは File.Exists のみ確認し .reg のヘッダ検証まではしない
        // （ImportKey 実行時に ValidateBackupFile が再検証するため、Load 時点の重複検証は不要）。
        bool hasPersistedRegistryBackup = false;
        bool staleRegistryBackupCleaned = false;
        if (item.ValueType != SettingValueType.Command && !string.IsNullOrEmpty(item.RegistryPath))
        {
            var savedPath = _backup.LoadRegistryBackupPath(item.Id);
            if (savedPath != null)
            {
                if (File.Exists(savedPath))
                {
                    LastBackupPath = savedPath;
                    IsApplied = true;   // ストア存在＝適用済みとみなす信頼境界（#7 と同じ割り切り）
                    hasPersistedRegistryBackup = true;
                }
                else
                {
                    // stale（ローテーション10件枠で.regが押し出された等）＝エントリ掃除・IsAppliedは立てない
                    // cross-reviewer 2026-07-21 should_fix②：Load() にこの書込副作用（RemoveRegistryBackupPath）
                    // がある点は既知の非対称（読み込むだけの想定の Load に永続ストアへの削除が混じる）。
                    // 現状はダイアログを単発オープンする用途のみで呼ばれる前提。将来 Load を一覧プレビュー等で
                    // ループ呼び出しする場合は、この掃除処理を Load 本体から分離すること。
                    _backup.RemoveRegistryBackupPath(item.Id);
                    staleRegistryBackupCleaned = true;
                }
            }
        }

        if (item.ValueType == SettingValueType.Command)
        {
            RegistryPathDisplay = "(コマンド型項目)";
            Choices.Clear();
            if (item.Command != null)
            {
                foreach (var c in item.Command.Choices) Choices.Add(c);
            }
        }
        else
        {
            RegistryPathDisplay = !string.IsNullOrEmpty(item.RegistryPath)
                ? $"{item.RegistryPath}  >  {item.RegistryValueName}"
                : "(レジストリ値非対応項目)";
        }

        // 現在値の読込と OriginalValue/EditableValue の三者同期
        ReadAndSyncCurrentValue();

        // 初期 EditableValue は OriginalValue と同じ（Boolean の選択肢同期も実施）
        SyncEditableFromOriginal();

        // cross-reviewer E-3 修正：「値なし」状態で EditableValue が RecommendedValue
        // にフォールバックされた場合、UpdateIsModified() を経由しても OriginalValue
        // との比較で IsModified=true になる。Load 完了直後は強制的に false にし、
        // OriginalValue も EditableValue と一致させて誤検知を防ぐ。
        if (OriginalValue.StartsWith("(") || string.IsNullOrEmpty(OriginalValue))
        {
            // 値なし or 読込失敗時：OriginalValue を表示用に保持しつつ
            //   比較用の OriginalValue は EditableValue と揃える
            //   （Display は元のまま「値なし」を見せる）
            OriginalValue = EditableValue;
        }

        // バックログ#9：Command 型とレジストリ型は排他なので両立ケースはない（if/else if で可読に）。
        // バックログ#10：Command 型のみ鮮度表示（保存時刻あり/なしで文言分岐）。レジストリ型は
        // 保存時刻を持たないためスコープ外＝従来文言を維持。
        if (hasPersistedCommandToken)
        {
            StatusMessage = commandTokenSavedAt.HasValue
                ? $"📦 {commandTokenSavedAt.Value.LocalDateTime:yyyy年M月d日 HH:mm}適用時の状態に戻せます。"
                : "📦 前回適用時のバックアップを復元できます（適用日時は記録されていません）。";
        }
        else if (hasPersistedRegistryBackup)
        {
            StatusMessage = "📦 前回適用時のバックアップを復元できます。";
        }
        else if (staleRegistryBackupCleaned)
        {
            // cross-reviewer 2026-07-21 should_fix①でローテーション保護（CleanupOldBackups の参照除外）を
            // 入れた後も、手動削除・手動保存等で押し出され得るため注意文言は残す。
            StatusMessage = "⏳ 前回のバックアップはローテーションで消去されたため「戻す」は使えません。（他の項目の変更でバックアップが押し出された可能性があります）";
        }
        else
        {
            StatusMessage = "値を変更して「適用」または「OK」で反映します。";
        }
        IsModified = false;   // Load 完了後の初期化は変更と見なさない
    }

    /// <summary>レジストリ/Command から現在値を読込み、OriginalValue / OriginalValueDisplay を更新。</summary>
    private void ReadAndSyncCurrentValue()
    {
        if (_item == null)
        {
            OriginalValue = "";
            OriginalValueDisplay = "";
            return;
        }

        // Command 型：CommandAction.Read から取得
        if (_item.ValueType == SettingValueType.Command)
        {
            try
            {
                OriginalValue = _item.Command?.Read() ?? "";
                OriginalValueDisplay = OriginalValue;
            }
            catch (Exception ex)
            {
                OriginalValue = "(取得失敗)";
                OriginalValueDisplay = $"(取得失敗: {ex.Message})";
            }
            return;
        }

        // レジストリ型：F-8 null チェック
        if (string.IsNullOrEmpty(_item.RegistryPath) || string.IsNullOrEmpty(_item.RegistryValueName))
        {
            OriginalValue = "(レジストリ値非対応項目)";
            OriginalValueDisplay = OriginalValue;
            return;
        }

        try
        {
            var v = _registry.ReadValue<object>(_item.RegistryPath, _item.RegistryValueName);
            OriginalValue = v?.ToString() ?? "(値なし)";

            // ValueType ごとの UX 表示整形
            OriginalValueDisplay = FormatCurrentDisplay(_item, OriginalValue);
        }
        catch (Exception ex)
        {
            OriginalValue = $"(読込失敗: {ex.Message})";
            OriginalValueDisplay = OriginalValue;
        }
    }

    /// <summary>OriginalValue → EditableValue の同期（Boolean/Command は SelectedIndex も同期）。</summary>
    private void SyncEditableFromOriginal()
    {
        if (_item == null) return;

        switch (_item.ValueType)
        {
            case SettingValueType.Boolean:
                bool initBool;
                if (int.TryParse(OriginalValue, out var curInt))
                {
                    bool registryOn = curInt != 0;
                    initBool = _item.InvertDisplay ? !registryOn : registryOn;
                }
                else
                {
                    initBool = _item.RecommendedValue switch
                    {
                        bool b => b,
                        int i => i != 0,
                        string s => s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase),
                        _ => false
                    };
                }
                EditableBoolIndex = initBool ? 1 : 0;
                EditableValue = initBool ? "1" : "0";   // ユーザー視点（適用時に Invert 再変換）
                EditableValueDisplay = BooleanChoices[EditableBoolIndex];
                break;

            case SettingValueType.Command:
                EditableValue = OriginalValue;   // Command は選択肢ラベルそのまま
                EditableValueDisplay = OriginalValue;
                EditableBoolIndex = Math.Max(0, Choices.IndexOf(OriginalValue));
                break;

            case SettingValueType.Binary:
                // OriginalValue は ReadValue<object> 経由で hex 整形済み
                EditableValue = string.IsNullOrEmpty(OriginalValue) || OriginalValue.StartsWith("(")
                    ? (_item.RecommendedValue?.ToString() ?? "")
                    : OriginalValue;
                EditableValueDisplay = EditableValue;
                break;

            case SettingValueType.DWord:
            case SettingValueType.String:
            case SettingValueType.ExpandString:
            default:
                EditableValue = string.IsNullOrEmpty(OriginalValue) || OriginalValue.StartsWith("(")
                    ? (_item.RecommendedValue?.ToString() ?? "")
                    : OriginalValue;
                EditableValueDisplay = EditableValue;
                break;
        }
    }

    /// <summary>EditableValue 変化時に IsModified を再計算。</summary>
    partial void OnEditableValueChanged(string value)
    {
        UpdateIsModified();
    }

    /// <summary>Boolean/Command 用 SelectedIndex 変化時に EditableValue を同期。</summary>
    partial void OnEditableBoolIndexChanged(int value)
    {
        if (_item == null) return;

        switch (_item.ValueType)
        {
            case SettingValueType.Boolean:
                EditableValue = value == 1 ? "1" : "0";
                EditableValueDisplay = BooleanChoices[Math.Max(0, Math.Min(1, value))];
                break;
            case SettingValueType.Command:
                if (value >= 0 && value < Choices.Count)
                {
                    EditableValue = Choices[value];
                    EditableValueDisplay = Choices[value];
                }
                break;
        }
    }

    /// <summary>IsModified を OriginalValue と EditableValue の比較で再計算。</summary>
    private void UpdateIsModified()
    {
        // Boolean は EditableValue ("0"/"1") と OriginalValue（InvertDisplay 考慮）を比較
        if (_item == null)
        {
            IsModified = false;
            return;
        }

        if (_item.ValueType == SettingValueType.Boolean)
        {
            // OriginalValue がレジストリ生値（"0"/"1"）・EditableValue はユーザー視点（"0"/"1"）
            // InvertDisplay = true なら反転して比較
            if (!int.TryParse(OriginalValue, out var origInt))
            {
                IsModified = true;   // 元値読めないなら編集中扱い
                return;
            }
            bool origUser = _item.InvertDisplay ? origInt == 0 : origInt != 0;
            bool editUser = EditableValue == "1";
            IsModified = origUser != editUser;
            return;
        }

        IsModified = !string.Equals(OriginalValue, EditableValue, StringComparison.Ordinal);
    }

    [RelayCommand]
    private void Ok()
    {
        if (DoApply())
        {
            CloseAction?.Invoke();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseAction?.Invoke();
    }

    [RelayCommand]
    private void ApplySetting()
    {
        DoApply();
    }

    private bool DoApply()
    {
        if (_item == null) return false;

        // v0.1.4 BUG-3 #4：再起動予約中は二重実行ガード
        if (Privilege.IsShutdownPending) return false;

        // F-8：レジストリ系の必須チェック（Command は不要）
        if (_item.ValueType != SettingValueType.Command
            && (string.IsNullOrEmpty(_item.RegistryPath) || string.IsNullOrEmpty(_item.RegistryValueName)))
        {
            Dialogs.Warn("この項目はレジストリ値を持たないため適用できません。", "未対応");
            return false;
        }

        // v0.1.4 BUG-3：HKLM 系で非Admin → 3 択ダイアログ
        // 「管理者として再起動」/ 「このまま続行」/ 「キャンセル」
        // Command 型は内部で管理者必要なものがあるが、書込時の例外フォールバックで補足する（意識的割り切り）
        if (_item.ValueType != SettingValueType.Command
            && _item.RegistryPath!.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase)
            && !Privilege.IsRunningAsAdministrator())
        {
            var choice = Dialogs.ConfirmElevation(_item.Title, isFromError: false);
            switch (choice)
            {
                case ElevationChoice.RestartAsAdmin:
                    RequestRestartAsAdministrator();
                    return false;

                case ElevationChoice.ContinueAnyway:
                    // そのまま下に進む（書込失敗の可能性あり、catch でフォールバック）
                    break;

                case ElevationChoice.Cancel:
                default:
                    return false;
            }
        }

        // バックアップ（Command 型は CommandAction.Backup を呼ぶ）
        // cross-reviewer S-1 修正：2回目以降の適用で _lastCommandToken を上書きしない
        //   既に IsApplied=true なら初回の Backup（適用前状態）を保持する
        if (_item.ValueType == SettingValueType.Command)
        {
            if (!IsApplied)  // 初回適用時のみ Backup を呼ぶ
            {
                try
                {
                    _lastCommandToken = _item.Command?.Backup?.Invoke();
                    StatusMessage = _lastCommandToken != null
                        ? "📦 Command バックアップ完了"
                        : "⚠️ この Command は戻し未対応";
                }
                catch (Exception ex)
                {
                    Dialogs.Error($"Command バックアップに失敗しました：\n{ex.Message}", "バックアップ失敗");
                    return false;
                }
            }
            else
            {
                // 2 回目以降：既存トークン保持
                StatusMessage = "🔁 既存バックアップを保持して再適用";
            }
        }
        else
        {
            // F-1：レジストリ系は ExportKey
            try
            {
                LastBackupPath = _backup.ExportKey(_item.RegistryPath!, _item.Id);
                StatusMessage = $"📦 バックアップ作成: {Path.GetFileName(LastBackupPath)}";
            }
            catch (UnauthorizedAccessException) when (HandleElevationNeeded()) { return false; }
            catch (SecurityException)         when (HandleElevationNeeded()) { return false; }
            catch (InvalidOperationException ex) when (LooksLikeAccessDenied(ex.Message) && HandleElevationNeeded()) { return false; }
            catch (Exception ex)
            {
                Dialogs.Error($"バックアップ作成に失敗しました：\n{ex.Message}", "バックアップ失敗");
                StatusMessage = "❌ バックアップ失敗。適用を中止しました。";
                return false;
            }
        }

        // F-6：Red 危険度なら復元ポイント
        if (_item.Risk == RiskLevel.Red)
        {
            var makeRestorePoint = Dialogs.Confirm(
                "この項目は危険度 🔴（上級者向け）です。\n復元ポイントを作成しますか？（推奨）",
                "復元ポイント");
            if (makeRestorePoint)
            {
                var result = _restore.Create($"WinKnobs: {_item.Title}", out var err);
                switch (result)
                {
                    case RestorePointResult.Success:
                        StatusMessage = "✅ 復元ポイント作成完了";
                        break;
                    case RestorePointResult.FailedFrequency:
                        var continueAnyway = Dialogs.Confirm(
                            "24時間以内に既に復元ポイントが作成されています。\n（Windowsの仕様で同日内に1回しか作れません）\n\n復元ポイントなしで続行しますか？",
                            "24時間制限");
                        if (!continueAnyway) return false;
                        StatusMessage = "⚠️ 復元ポイントスキップ（24時間制限）";
                        break;
                    case RestorePointResult.FailedAccessDenied:
                        Dialogs.Warn(
                            "復元ポイント作成に管理者権限が必要です。\n変更は続行しますが、戻したい場合はバックアップ.regを使ってください。",
                            "権限不足");
                        break;
                    case RestorePointResult.FailedProtectionDisabled:
                        Dialogs.Warn(
                            "システム保護が無効化されています。\nコントロールパネル → 回復 → システムの復元の構成 で有効化できます。",
                            "システム保護無効");
                        break;
                    default:
                        Dialogs.Warn(
                            $"復元ポイント作成に失敗しました：\n{err}\n\n変更は続行します。",
                            "復元ポイント失敗");
                        break;
                }
            }
        }

        // 書込（ValueType ベース）
        try
        {
            switch (ValueType)
            {
                case SettingValueType.Boolean:
                    // EditableValue はユーザー視点 "0"/"1"。InvertDisplay を反映してレジストリへ
                    bool userOn = EditableValue == "1";
                    int boolInt = (_item.InvertDisplay ? !userOn : userOn) ? 1 : 0;
                    _registry.WriteValue<int>(_item.RegistryPath!, _item.RegistryValueName!,
                        boolInt, RegistryValueKind.DWord);
                    break;

                case SettingValueType.DWord:
                    if (!int.TryParse(EditableValue, out var dword) || dword < 0)
                    {
                        Dialogs.Warn(
                            $"数値項目に不正な値が入力されています：「{EditableValue}」\n0以上の半角数字（例：4096）を入力してください。",
                            "入力エラー");
                        StatusMessage = "❌ 入力エラー（数値項目）";
                        return false;
                    }
                    _registry.WriteValue<int>(_item.RegistryPath!, _item.RegistryValueName!,
                        dword, RegistryValueKind.DWord);
                    break;

                case SettingValueType.String:
                    // cross-reviewer U-3：NumericStringRange があれば範囲検証
                    if (_item.NumericStringRange is (int min, int max))
                    {
                        if (!int.TryParse(EditableValue, out var num) || num < min || num > max)
                        {
                            Dialogs.Warn(
                                $"数値文字列の範囲外です：「{EditableValue}」\n{min} 〜 {max} の整数を入力してください。",
                                "入力エラー");
                            StatusMessage = $"❌ 入力エラー（範囲 {min}-{max}）";
                            return false;
                        }
                    }
                    _registry.WriteValue<string>(_item.RegistryPath!, _item.RegistryValueName!,
                        EditableValue, RegistryValueKind.String);
                    break;

                case SettingValueType.ExpandString:
                    _registry.WriteValue<string>(_item.RegistryPath!, _item.RegistryValueName!,
                        EditableValue, RegistryValueKind.ExpandString);
                    break;

                case SettingValueType.Binary:
                    // 2026-05-20 cross-reviewer E-4 対応：詳細エラーメッセージ取得版を使用
                    if (!TryParseHexBytes(EditableValue, out var bytes, out var parseErr))
                    {
                        Dialogs.Warn(
                            "Binary 形式が不正です。\n\n" +
                            $"詳細：{parseErr}\n\n" +
                            "例：00 00 00 00（半角 16 進数を空白区切りで）",
                            "入力エラー");
                        StatusMessage = $"❌ 入力エラー（{parseErr}）";
                        return false;
                    }
                    if (_item.ExpectedByteLength is int expected && bytes.Length != expected)
                    {
                        Dialogs.Warn(
                            $"この項目のバイト数は {expected} バイト固定です。\n\n" +
                            $"現在の入力：{bytes.Length} バイト\n" +
                            $"期待値：{expected} バイト",
                            "入力エラー");
                        StatusMessage = $"❌ Binary バイト数不一致（{bytes.Length}/{expected}）";
                        return false;
                    }
                    _registry.WriteBinary(_item.RegistryPath!, _item.RegistryValueName!, bytes);
                    break;

                case SettingValueType.Command:
                    if (_item.Command == null)
                    {
                        Dialogs.Warn("Command 設定が未定義のため適用できません。", "未対応");
                        return false;
                    }
                    _item.Command.Apply(EditableValue);
                    break;
            }

            IsApplied = true;
            // バックログ#7（plan-validator指摘1）：Command 型の戻しトークンは Apply 成功後に永続化する
            // （Backup.Invoke() 直後ではなくここで呼ぶことで、Apply 例外時にディスクへ幽霊トークンを残さない）。
            // cross-reviewer 2026-07-21 should_fix1：トークン保存はレジストリ変更（Apply 本体）とは独立の
            // 後始末 I/O なので、失敗しても Apply 成功の報告を覆さない（外側の汎用 catch に落とすと
            // 「実際は適用済みなのに ❌ 適用失敗」と誤報告になる）。失敗時は副次警告のみ。
            bool tokenSaveFailed = false;
            if (_item.ValueType == SettingValueType.Command && _lastCommandToken != null)
            {
                try { _backup.SaveCommandTokenIfAbsent(_item.Id, _lastCommandToken); }
                catch { tokenSaveFailed = true; }
            }
            // バックログ#9（plan-validator指摘）：レジストリ型の LastBackupPath も同じ理由（幽霊エントリ防止）
            // で ExportKey 直後（バックアップ作成時点）ではなく Apply 成功後のここで永続化する。
            if (_item.ValueType != SettingValueType.Command && LastBackupPath != null)
            {
                try { _backup.SaveRegistryBackupPath(_item.Id, LastBackupPath); }
                catch { tokenSaveFailed = true; }
            }
            // 適用後は OriginalValue を最新に更新（三者同期）
            ReadAndSyncCurrentValue();
            // EditableValue は変えず（ユーザーの選択を保持）、IsModified を再評価
            UpdateIsModified();
            StatusMessage = tokenSaveFailed
                ? $"✅ 適用完了。⚠️「戻す」用バックアップの保存に失敗（画面を閉じると戻せません）。{_item.RestartScope.ToHint()}"
                : $"✅ 適用完了。{_item.RestartScope.ToHint()}";
            return true;
        }
        // v0.1.4 BUG-3：権限不足例外を分岐 → 管理者再起動を提案
        catch (UnauthorizedAccessException) when (HandleElevationNeeded()) { return false; }
        catch (SecurityException)         when (HandleElevationNeeded()) { return false; }
        catch (InvalidOperationException ex) when (LooksLikeAccessDenied(ex.Message) && HandleElevationNeeded()) { return false; }
        catch (Exception ex)
        {
            Dialogs.Error($"書込に失敗しました：\n{ex.Message}", "書込失敗");
            StatusMessage = $"❌ 適用失敗: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// v0.1.4 BUG-3：管理者として再起動を実行する（事前ダイアログで Yes が選ばれた場合）。
    /// 失敗時は Warn を出す。成功時は ShutdownCurrentProcess を呼んで現プロセスを終了。
    /// </summary>
    private void RequestRestartAsAdministrator()
    {
        if (Privilege.TryRestartAsAdministrator(out var err))
        {
            Privilege.ShutdownCurrentProcess();
        }
        else
        {
            Dialogs.Warn(err, "管理者起動できませんでした");
            StatusMessage = "❌ 管理者起動キャンセル";
        }
    }

    /// <summary>
    /// v0.1.4 BUG-3：書込/バックアップ/復元で権限不足例外を捕捉したときに呼ぶ。
    /// 既に管理者で動作している場合は別エラーとして扱う（plan-validator エッジケース4）。
    /// 戻り値 true：catch when でこの catch ブロックを採用する（return は呼び出し側で）。
    /// </summary>
    private bool HandleElevationNeeded()
    {
        if (_item == null) return false;

        // 既に管理者で動いているのに権限不足 → グループポリシー等による制限
        if (Privilege.IsRunningAsAdministrator())
        {
            Dialogs.Error(
                $"「{_item.Title}」の操作に失敗しました。\n" +
                "管理者として実行中ですが書込が拒否されました。\n" +
                "グループポリシー・セキュリティソフト・既存ロックなどの可能性があります。",
                "操作失敗（管理者でも拒否）");
            StatusMessage = "❌ 操作失敗（管理者でも拒否）";
            return true;
        }

        var choice = Dialogs.ConfirmElevation(_item.Title, isFromError: true);
        if (choice == ElevationChoice.RestartAsAdmin)
        {
            RequestRestartAsAdministrator();
        }
        else
        {
            StatusMessage = "❌ 操作失敗：管理者権限が必要";
        }
        return true;
    }

    /// <summary>
    /// v0.1.4 BUG-3：例外メッセージから「権限不足」を推測する（fsutil/netsh 等の InvalidOperationException 用）。
    /// 英語/日本語ロケール両方に対応。
    /// </summary>
    private static bool LooksLikeAccessDenied(string? msg)
    {
        if (string.IsNullOrEmpty(msg)) return false;
        return msg.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Access denied", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("アクセスが拒否されました", StringComparison.Ordinal)
            || msg.Contains("アクセスは拒否されました", StringComparison.Ordinal)
            || msg.Contains("Requires elevation", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("要求された操作には特権が必要", StringComparison.Ordinal)
            || msg.Contains("要求された操作には管理者特権", StringComparison.Ordinal)
            || msg.Contains("ERROR_ACCESS_DENIED", StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private void RestoreSetting()
    {
        if (_item == null) return;

        // v0.1.4 BUG-3 #4：再起動予約中は二重実行ガード
        if (Privilege.IsShutdownPending) return;

        // cross-reviewer E-2 修正：IsModified 優先（適用後でも未適用編集が優先）
        // 「適用後さらに編集 → 戻す」では編集破棄が直感的（バックアップ復元は別操作）
        if (IsModified)
        {
            SyncEditableFromOriginal();
            UpdateIsModified();
            StatusMessage = "🔙 編集中の変更を破棄しました。";
            return;
        }

        // Case B：適用済み（IsModified=false） → CommandAction.Restore or LastBackupPath
        if (IsApplied)
        {
            // Command 型：CommandAction.Restore
            if (_item.ValueType == SettingValueType.Command)
            {
                if (_item.Command?.CanRestore != true || _lastCommandToken == null)
                {
                    Dialogs.Warn("この Command 項目は戻し未対応です。", "戻し不可");
                    return;
                }

                var ok = Dialogs.Confirm("適用前の状態に戻します。続行しますか？", "戻す");
                if (!ok) return;

                try
                {
                    _item.Command.Restore!(_lastCommandToken);
                    // バックログ#7：戻し成功後は永続ストアのトークンを削除（後始末）。
                    _backup.RemoveCommandToken(_item.Id);
                    StatusMessage = "✅ Command を適用前の状態に戻しました。";
                    IsApplied = false;
                    ReadAndSyncCurrentValue();
                    SyncEditableFromOriginal();
                    UpdateIsModified();
                }
                // v0.1.4 BUG-3：Command 型の Restore も権限不足の可能性（fsutil/netsh 等）
                catch (UnauthorizedAccessException) when (HandleElevationNeeded()) { return; }
                catch (SecurityException)         when (HandleElevationNeeded()) { return; }
                catch (InvalidOperationException ex) when (LooksLikeAccessDenied(ex.Message) && HandleElevationNeeded()) { return; }
                // cross-reviewer should_fix①：委譲型 Command（マーカーのみのトークン）で実データが既に無い場合
                // （SystemSoundService.NoBackupDataMessage 等）。マーカーと実データがズレたまま持ち越さないよう
                // 幽霊トークンを掃除する（掃除しないと次回 Load でも「戻す」が誤って活性化し続ける）。
                catch (InvalidOperationException ex) when (ex.Message == SystemSoundService.NoBackupDataMessage)
                {
                    _backup.RemoveCommandToken(_item.Id);
                    Dialogs.Error($"戻しに失敗しました：\n{ex.Message}", "戻し失敗");
                    StatusMessage = "❌ 戻し失敗：退避データなし（トークンを破棄しました）";
                }
                catch (Exception ex)
                {
                    Dialogs.Error($"戻しに失敗しました：\n{ex.Message}", "戻し失敗");
                }
                return;
            }

            // レジストリ型：VE-6 File.Exists チェック
            if (string.IsNullOrEmpty(LastBackupPath) || !File.Exists(LastBackupPath))
            {
                Dialogs.Warn(
                    "バックアップファイルが見つかりません。\nバックアップフォルダから手動で .reg を読込んでください（「読込」ボタン）。",
                    "バックアップなし");
                return;
            }

            var confirm = Dialogs.Confirm(
                $"バックアップから復元します：\n{Path.GetFileName(LastBackupPath)}\n\n※ 直前の適用前の状態に戻します（初期状態とは限りません）。\n※ 適用後に外部で値を変更していた場合、その変更もバックアップ時点の状態で上書きされます。\n\n続行しますか？",
                "戻す");
            if (!confirm) return;

            try
            {
                _backup.ImportKey(LastBackupPath);
                // バックログ#9：戻し成功後は永続ストアのエントリを削除（後始末・Command型 CaseB と同順）。
                _backup.RemoveRegistryBackupPath(_item.Id);
                LastBackupPath = null;
                StatusMessage = "✅ バックアップから復元しました。";
                IsApplied = false;
                ReadAndSyncCurrentValue();
                SyncEditableFromOriginal();
                UpdateIsModified();
            }
            // v0.1.4 BUG-3：HKLM バックアップの import も標準権限では失敗する → 管理者再起動を提案
            catch (UnauthorizedAccessException) when (HandleElevationNeeded()) { return; }
            catch (SecurityException)         when (HandleElevationNeeded()) { return; }
            catch (InvalidOperationException ex) when (LooksLikeAccessDenied(ex.Message) && HandleElevationNeeded()) { return; }
            catch (Exception ex)
            {
                Dialogs.Error($"復元に失敗しました：\n{ex.Message}", "復元失敗");
                StatusMessage = $"❌ 復元失敗: {ex.Message}";
            }
            return;
        }

        // Case C：何もしていない
        Dialogs.Warn(
            "戻す対象がありません（編集も適用も行われていません）。",
            "戻す対象なし");
    }

    /// <summary>
    /// 「戻す」ボタンの活性条件（cross-reviewer U-1 修正）。
    /// - 編集中（IsModified）：常に戻せる（編集破棄）
    /// - 適用済み（IsApplied）：
    ///   - Command 型なら CommandAction.CanRestore が true
    ///   - レジストリ型なら LastBackupPath が File.Exists
    /// </summary>
    private bool CanRestore()
    {
        if (_item == null) return false;
        if (IsModified) return true;
        if (!IsApplied) return false;

        if (_item.ValueType == SettingValueType.Command)
        {
            return _item.Command?.CanRestore == true && _lastCommandToken != null;
        }
        return !string.IsNullOrEmpty(LastBackupPath) && File.Exists(LastBackupPath);
    }

    /// <summary>IsModified / IsApplied 変化時に RestoreSettingCommand の CanExecute を再評価。</summary>
    partial void OnIsModifiedChanged(bool value) => RestoreSettingCommand.NotifyCanExecuteChanged();

    partial void OnIsAppliedChanged(bool value) => RestoreSettingCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void SaveSetting()
    {
        if (_item == null) return;
        if (_item.ValueType == SettingValueType.Command || string.IsNullOrEmpty(_item.RegistryPath))
        {
            Dialogs.Warn("この項目はファイル保存に対応していません（Command 型・パス未設定）。", "保存不可");
            return;
        }
        var path = Dialogs.ShowSaveFileDialog(
            "Reg files (*.reg)|*.reg|All files (*.*)|*.*",
            $"{_item.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.reg");
        if (path == null) return;

        try
        {
            var backupPath = _backup.ExportKey(_item.RegistryPath, _item.Id);
            File.Copy(backupPath, path, overwrite: true);
            StatusMessage = $"✅ 保存しました: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            Dialogs.Error($"保存に失敗しました：\n{ex.Message}", "保存失敗");
        }
    }

    [RelayCommand]
    private void LoadSetting()
    {
        if (_item?.ValueType == SettingValueType.Command)
        {
            Dialogs.Warn("Command 型項目は .reg 読込に対応していません。", "読込不可");
            return;
        }

        var path = Dialogs.ShowOpenFileDialog("Reg files (*.reg)|*.reg|All files (*.*)|*.*");
        if (path == null) return;

        var ok = Dialogs.Confirm(
            $"読込ファイル: {Path.GetFileName(path)}\n\nレジストリにインポートしますか？",
            "読込確認");
        if (!ok) return;

        try
        {
            _backup.ImportKey(path);
            StatusMessage = "✅ 読込・インポート完了。";
            ReadAndSyncCurrentValue();
            SyncEditableFromOriginal();
            UpdateIsModified();
        }
        // v0.1.4 BUG-3（cross-reviewer M-2）：手動 .reg 読込でも HKLM 系は権限不足で失敗 → 管理者再起動を提案
        catch (UnauthorizedAccessException) when (HandleElevationNeeded()) { return; }
        catch (SecurityException)         when (HandleElevationNeeded()) { return; }
        catch (InvalidOperationException ex) when (LooksLikeAccessDenied(ex.Message) && HandleElevationNeeded()) { return; }
        catch (Exception ex)
        {
            Dialogs.Error($"読込に失敗しました：\n{ex.Message}", "読込失敗");
        }
    }

    [RelayCommand]
    private void Help()
    {
        // v0.1.0 時点は GitHub プレースホルダ URL
        Launcher.OpenUrl("https://github.com/");
    }

    /// <summary>
    /// 推奨値/既定値を ValueType に応じて UX 表示に整形。
    /// Boolean：ON/OFF（推奨ラベル付き）/ Command：選択肢ラベルそのまま / その他：ToString。
    /// </summary>
    private static string FormatDisplay(SettingItem item, object? value, bool isRecommended)
    {
        if (value == null) return "";

        switch (item.ValueType)
        {
            case SettingValueType.Boolean:
                bool boolValue = value switch
                {
                    bool b => b,
                    int i => i != 0,
                    string s => s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase),
                    _ => false
                };
                string baseText = boolValue ? "ON" : "OFF";
                return isRecommended ? $"{baseText}（推奨）" : baseText;

            default:
                return value.ToString() ?? "";
        }
    }

    /// <summary>
    /// 「元の値」表示用の整形。Boolean は「有効/無効 (0/1)」、その他はそのまま。
    /// InvertDisplay 考慮。
    /// </summary>
    private static string FormatCurrentDisplay(SettingItem item, string rawValue)
    {
        if (item.ValueType != SettingValueType.Boolean) return rawValue;

        bool invert = item.InvertDisplay;
        return rawValue switch
        {
            "1" => invert ? "無効 (1)" : "有効 (1)",
            "0" => invert ? "有効 (0)" : "無効 (0)",
            _ => rawValue
        };
    }

    /// <summary>Binary パース時の最大バイト長（256 = レジストリ REG_BINARY 実用上限）</summary>
    public const int MaxBinaryByteLength = 256;

    /// <summary>
    /// "00 11 22 FF" 形式の16進文字列を byte[] にパース（A3-2-c V-9）。
    /// 区切り文字は空白・タブ・改行・カンマを許容。
    /// 2026-05-20 cross-reviewer E-4 対応：詳細エラーメッセージを返すオーバーロード追加。
    /// </summary>
    public static bool TryParseHexBytes(string text, out byte[] bytes, out string errorMessage)
    {
        bytes = Array.Empty<byte>();
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            errorMessage = "値が空です。半角の16進数を空白区切りで入力してください（例：00 00 00 00）。";
            return false;
        }

        var parts = text.Split(
            new[] { ' ', '\t', '\r', '\n', ',' },
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            errorMessage = "16進数バイトが見つかりません。区切り文字（空白・タブ・カンマ）のみです。";
            return false;
        }

        if (parts.Length > MaxBinaryByteLength)
        {
            errorMessage = $"バイト数が上限を超えました（{parts.Length} バイト・最大 {MaxBinaryByteLength} バイト）。";
            return false;
        }

        var result = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!byte.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out result[i]))
            {
                errorMessage = $"{i + 1} 番目のトークン '{parts[i]}' が 16 進数として解釈できません（00〜FF の範囲で入力してください）。";
                return false;
            }
        }
        bytes = result;
        return true;
    }

    /// <summary>
    /// 後方互換用シンプル版（エラーメッセージ不要な場合）。
    /// </summary>
    public static bool TryParseHexBytes(string text, out byte[] bytes)
    {
        return TryParseHexBytes(text, out bytes, out _);
    }
}
