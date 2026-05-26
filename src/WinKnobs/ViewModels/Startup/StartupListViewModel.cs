using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinKnobs.Models;
using WinKnobs.Services;
using WinKnobs.ViewModels.Items;

namespace WinKnobs.ViewModels.Startup;

/// <summary>
/// スタートアップ系リストの ViewModel。
/// Phase R-1：閲覧のみ。
/// Phase R-2：CheckBox 即時トグル + 編集ダイアログ + 保存ボタン。
/// Phase R-3：追加・削除・バックアップ。
/// 設計書 4-2 参照。
///
/// R-2 で追加された BUG-3 ヘルパー（HandleElevationNeeded / RequestRestartAsAdministrator /
/// LooksLikeAccessDenied）は SettingDialogViewModel から複製。
/// 将来課題（M-1 リファクタ・v0.3）：共通基底クラス ElevationAwareViewModel への昇格。
/// </summary>
public partial class StartupListViewModel : ObservableObject
{
    private readonly StartupItemsProvider _provider;
    private readonly DialogService _dialogs;
    private readonly PrivilegeService _privilege;

    public ObservableCollection<StartupEntryItem> Entries { get; } = new();
    public ICollectionView FilteredEntries { get; }

    [ObservableProperty]
    private StartupEntryItem? _selectedEntry;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _statusMessage = "スタートアップ項目を読み込んでいません（カテゴリ選択で読込）。";

    /// <summary>編集済みエントリが 1 件以上あるか（保存ボタンの有効化判定）。</summary>
    public bool HasDirtyEntries => Entries.Any(e => e.IsDirty);

    public StartupListViewModel(
        StartupItemsProvider provider,
        DialogService dialogs,
        PrivilegeService privilege)
    {
        _provider  = provider;
        _dialogs   = dialogs;
        _privilege = privilege;

        FilteredEntries = CollectionViewSource.GetDefaultView(Entries);
        FilteredEntries.Filter = FilterEntry;
    }

    /// <summary>
    /// 全 6 ハイブから読込。
    /// R-1 では同期実装（I/O 軽量で UI スレッド OK）。R-2 以降で非同期化を検討。
    /// </summary>
    [RelayCommand]
    public void Load()
    {
        UnsubscribeEntries();
        Entries.Clear();
        try
        {
            foreach (var e in _provider.LoadAll())
            {
                e.PropertyChanged += Entry_PropertyChanged;
                Entries.Add(e);
            }
            StatusMessage = $"📋 {Entries.Count} 件のスタートアップ項目を読み込みました。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"⚠️ 読込エラー：{ex.Message}";
        }
        NotifyDirtyChanged();
    }

    partial void OnSearchTextChanged(string value)
    {
        FilteredEntries?.Refresh();
    }

    private bool FilterEntry(object obj)
    {
        if (obj is not StartupEntryItem e) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var q = SearchText.Trim();
        return e.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
            || e.Command.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // =================== R-2 コマンド ===================

    /// <summary>
    /// CheckBox 即時トグル（タスクマネージャー互換 UX）。
    /// HKLM 系 + 非管理者 → ConfirmElevation 3 択 → 管理者再起動 or 続行 or キャンセル。
    /// 失敗時は IsEnabled を元に戻し、フォールバック誘導。
    /// </summary>
    [RelayCommand]
    private void ToggleEnabled(StartupEntryItem? entry)
    {
        if (entry == null) return;
        if (_privilege.IsShutdownPending) return;

        // 現状値（null は有効扱い → ここでは true として扱う）
        var oldState = entry.IsEnabled ?? true;
        var newState = !oldState;

        // HKLM 系 + 非管理者 → 事前 3 択
        if (IsHklm(entry.Hive) && !_privilege.IsRunningAsAdministrator())
        {
            var choice = _dialogs.ConfirmElevation(entry.Name, isFromError: false);
            switch (choice)
            {
                case ElevationChoice.RestartAsAdmin:
                    RequestRestartAsAdministrator();
                    return;
                case ElevationChoice.ContinueAnyway:
                    break;  // 下に進む（書込失敗の可能性あり）
                case ElevationChoice.Cancel:
                default:
                    return;
            }
        }

        try
        {
            _provider.SetEnabled(entry, newState);
            entry.IsEnabled = newState;
            StatusMessage = $"✅ {entry.Name} を{(newState ? "有効" : "無効")}化しました。";
        }
        catch (UnauthorizedAccessException) when (HandleElevationNeeded(entry.Name)) { return; }
        catch (SecurityException)             when (HandleElevationNeeded(entry.Name)) { return; }
        catch (InvalidOperationException ex)  when (LooksLikeAccessDenied(ex.Message) && HandleElevationNeeded(entry.Name)) { return; }
        catch (Exception ex)
        {
            _dialogs.Error($"有効/無効の切替に失敗しました：\n{ex.Message}", "操作失敗");
            StatusMessage = $"❌ 切替失敗: {ex.Message}";
        }
    }

    /// <summary>
    /// 選択中エントリを編集ダイアログで開く（Command / ValueKind のみ・Name 変更は R-3）。
    /// OK 押下時は EditViewModel が直接 SelectedEntry を更新し IsDirty=true をセット。
    /// </summary>
    [RelayCommand]
    private void Edit()
    {
        if (SelectedEntry == null)
        {
            _dialogs.Info("編集するエントリを一覧から選択してください。", "未選択");
            return;
        }
        if (_privilege.IsShutdownPending) return;

        var result = _dialogs.OpenStartupEdit(SelectedEntry);
        if (result == true)
        {
            // R-2 hotfix（VM E2E 検証 2026-05-26）：StartupEntryEditViewModel.Ok() が無変更 no-op になったので、
            // ステータス／HasDirtyEntries 通知も IsDirty 連動にする（誤検出メッセージ防止）。
            if (SelectedEntry.IsDirty)
            {
                StatusMessage = $"✏️ {SelectedEntry.Name} を編集しました。💾保存 ボタンで永続化してください。";
                NotifyDirtyChanged();
            }
            else
            {
                StatusMessage = $"（{SelectedEntry.Name} は変更なし）";
            }
        }
    }

    /// <summary>
    /// 編集済（IsDirty=true）エントリをまとめて Run キーに書込（楽観ロック付き）。
    /// HKLM 系 + 非管理者 → ConfirmElevation 3 択。
    /// 並行編集検出 → 上書き確認ダイアログ。
    /// </summary>
    [RelayCommand]
    private void SaveDirty()
    {
        if (_privilege.IsShutdownPending) return;

        var dirtyList = Entries.Where(e => e.IsDirty).ToList();
        if (dirtyList.Count == 0)
        {
            _dialogs.Info("保存対象（編集済）のエントリがありません。", "保存対象なし");
            return;
        }

        // HKLM 系の編集が含まれる + 非管理者 → 事前 3 択
        if (dirtyList.Any(e => IsHklm(e.Hive)) && !_privilege.IsRunningAsAdministrator())
        {
            var sampleName = dirtyList.First(e => IsHklm(e.Hive)).Name;
            var choice = _dialogs.ConfirmElevation(sampleName + " など", isFromError: false);
            switch (choice)
            {
                case ElevationChoice.RestartAsAdmin:
                    RequestRestartAsAdministrator();
                    return;
                case ElevationChoice.ContinueAnyway:
                    break;
                case ElevationChoice.Cancel:
                default:
                    return;
            }
        }

        // R-2 cross-reviewer 致命11 ズレB 対応：foreach の内側で try-catch することで
        //   「1 件目失敗で残全件スキップ」を回避。権限不足は全件失敗するので早期 return。
        int savedCount = 0;
        int skippedCount = 0;
        int errorCount = 0;
        bool elevationRequested = false;
        var errorList = new List<string>();

        foreach (var entry in dirtyList)
        {
            try
            {
                var result = _provider.Save(entry);

                if (result.ConflictDetected)
                {
                    var ok = _dialogs.Confirm(
                        $"「{entry.Name}」は外部で変更されています。\n\n" +
                        $"現在の値：\n{result.CurrentCommandOnConflict}\n\n" +
                        $"あなたの編集：\n{entry.Command}\n\n" +
                        "あなたの編集で上書きしますか？\n" +
                        "（いいえ：このエントリの編集を破棄して外部の値を維持）",
                        "並行編集の競合");

                    if (ok)
                    {
                        var forced = _provider.Save(entry, forceOverwrite: true);
                        if (forced.Success)
                        {
                            entry.SnapshotOriginal();
                            entry.IsDirty = false;
                            savedCount++;
                        }
                        else
                        {
                            errorCount++;
                            errorList.Add($"{entry.Name}：強制上書き失敗");
                        }
                    }
                    else
                    {
                        // No：外部値を採用 → メモリ上の編集を破棄
                        entry.RevertChanges();
                        skippedCount++;
                    }
                }
                else if (result.Success)
                {
                    entry.SnapshotOriginal();
                    entry.IsDirty = false;
                    savedCount++;
                }
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException
                || ex is SecurityException
                || (ex is InvalidOperationException && LooksLikeAccessDenied(ex.Message)))
            {
                // 権限不足は全件失敗するので、まとめて昇格誘導してから中止
                if (!elevationRequested)
                {
                    elevationRequested = true;
                    HandleElevationNeededForSave();
                }
                var remaining = dirtyList.Count - (savedCount + skippedCount + errorCount);
                StatusMessage = $"❌ 権限不足のため中止（{savedCount} 件保存・{remaining} 件未処理）";
                NotifyDirtyChanged();
                return;
            }
            catch (Exception ex)
            {
                errorCount++;
                errorList.Add($"{entry.Name}：{ex.Message}");
            }
        }

        var summary = $"💾 {savedCount} 件保存";
        if (skippedCount > 0) summary += $"・{skippedCount} 件スキップ（競合）";
        if (errorCount > 0)   summary += $"・{errorCount} 件失敗";
        StatusMessage = summary;
        if (errorList.Count > 0)
        {
            _dialogs.Error(
                $"以下のエントリは保存できませんでした：\n\n{string.Join("\n", errorList)}",
                "一部失敗");
        }
        NotifyDirtyChanged();
    }

    /// <summary>
    /// 編集中（IsDirty=true）の全エントリをメモリ上で破棄し、Original の値に戻す。
    /// R-2 cross-reviewer 致命15 対応：rollback 手段の提供。
    /// レジストリには触らない（永続化された値の復元は R-3 BackupSnapshot で別実装予定）。
    /// </summary>
    [RelayCommand]
    private void RevertAll()
    {
        if (_privilege.IsShutdownPending) return;

        var dirtyList = Entries.Where(e => e.IsDirty).ToList();
        if (dirtyList.Count == 0)
        {
            _dialogs.Info("戻す対象（編集済）のエントリがありません。", "対象なし");
            return;
        }

        var ok = _dialogs.Confirm(
            $"{dirtyList.Count} 件の編集を破棄して元の値に戻しますか？\n" +
            "（レジストリには触りません。メモリ上の編集のみ破棄します。）",
            "編集の破棄確認");
        if (!ok) return;

        foreach (var e in dirtyList) e.RevertChanges();
        StatusMessage = $"↺ {dirtyList.Count} 件の編集を破棄しました。";
        NotifyDirtyChanged();
    }

    // =================== R-2 内部ヘルパー（BUG-3 パターン複製） ===================

    /// <summary>エントリ単位の権限不足フォールバック。SettingDialogViewModel から複製（M-1 で共通化予定）。</summary>
    private bool HandleElevationNeeded(string itemName)
    {
        if (_privilege.IsRunningAsAdministrator())
        {
            _dialogs.Error(
                $"「{itemName}」の操作に失敗しました。\n" +
                "管理者として実行中ですが書込が拒否されました。\n" +
                "グループポリシー・セキュリティソフト・既存ロックなどの可能性があります。",
                "操作失敗（管理者でも拒否）");
            StatusMessage = "❌ 操作失敗（管理者でも拒否）";
            return true;
        }

        var choice = _dialogs.ConfirmElevation(itemName, isFromError: true);
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

    /// <summary>SaveDirty 全体の権限不足フォールバック（一括 Save 用に総称名）。</summary>
    private bool HandleElevationNeededForSave() => HandleElevationNeeded("スタートアップ保存");

    /// <summary>管理者として再起動を実行。失敗時は Warn 表示。</summary>
    private void RequestRestartAsAdministrator()
    {
        if (_privilege.TryRestartAsAdministrator(out var err))
        {
            _privilege.ShutdownCurrentProcess();
        }
        else
        {
            _dialogs.Warn(err, "管理者起動できませんでした");
            StatusMessage = "❌ 管理者起動キャンセル";
        }
    }

    /// <summary>例外メッセージから「権限不足」を推測（英/日両ロケール対応）。</summary>
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

    /// <summary>HKLM 系ハイブ（管理者必須）か判定。</summary>
    private static bool IsHklm(StartupHive hive) =>
        hive is StartupHive.HKLM_Run
             or StartupHive.HKLM_RunOnce
             or StartupHive.HKLM_Wow64_Run
             or StartupHive.HKLM_Wow64_RunOnce;

    // =================== IsDirty 変更通知の購読 ===================

    private void UnsubscribeEntries()
    {
        foreach (var e in Entries)
        {
            e.PropertyChanged -= Entry_PropertyChanged;
        }
    }

    private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StartupEntryItem.IsDirty))
        {
            NotifyDirtyChanged();
        }
    }

    private void NotifyDirtyChanged()
    {
        OnPropertyChanged(nameof(HasDirtyEntries));
    }
}
