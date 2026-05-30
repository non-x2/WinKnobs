using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
    private readonly BackupService _backup;  // R-3 追加

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

    /// <summary>選択中エントリがあるか（削除/編集ボタンの有効化判定・R-3 追加）。</summary>
    public bool HasSelectedEntry => SelectedEntry != null;

    public StartupListViewModel(
        StartupItemsProvider provider,
        DialogService dialogs,
        PrivilegeService privilege,
        BackupService backup)  // R-3 追加
    {
        _provider  = provider;
        _dialogs   = dialogs;
        _privilege = privilege;
        _backup    = backup;

        FilteredEntries = CollectionViewSource.GetDefaultView(Entries);
        FilteredEntries.Filter = FilterEntry;
    }

    partial void OnSelectedEntryChanged(StartupEntryItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedEntry));
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

        // R-3 plan-validator 致命中4 反映：Modify 直前 snapshot（毎回・専用ローテで軽量）
        EnsureSnapshot();

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

        // R-3 plan-validator 致命中4 反映：SaveDirty 直前 snapshot（毎回・専用ローテで軽量）
        EnsureSnapshot();

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
    /// レジストリには触らない（永続化された値の復元は R-3 RestoreSnapshot コマンド）。
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

    // =================== R-3 コマンド ===================

    /// <summary>
    /// R-3：新規エントリを追加（Add ダイアログ起動）。
    /// HKCU_Run 既定 + 他ハイブ選択時は権限フォールバック。
    /// 他ハイブ Name 重複は警告のみ（plan-validator 致命中5）。
    /// </summary>
    [RelayCommand]
    private void Add()
    {
        if (_privilege.IsShutdownPending) return;

        // R-3 cross-reviewer 致命高1 対応：ダイアログ内でリアルタイム重複チェック。
        // 同一ハイブ重複は OK ボタン無効化・他ハイブ重複は警告表示のみ（事後 Confirm は不要に）。
        Func<StartupHive, string, (bool sameHiveDup, string? otherHiveDup)> checker = (hive, name) =>
        {
            bool same = Entries.Any(e =>
                e.Hive == hive
                && string.Equals(e.RawName, name, StringComparison.OrdinalIgnoreCase));
            var otherEntry = Entries.FirstOrDefault(e =>
                e.Hive != hive
                && string.Equals(e.RawName, name, StringComparison.OrdinalIgnoreCase));
            return (same, otherEntry?.HiveDisplay);
        };

        var (result, created) = _dialogs.OpenStartupAdd(StartupHive.HKCU_Run, checker);
        if (result != true || created == null) return;

        // HKLM 系 + 非管理者 → 事前 3 択
        if (IsHklm(created.Hive) && !_privilege.IsRunningAsAdministrator())
        {
            var choice = _dialogs.ConfirmElevation(created.Name, isFromError: false);
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

        // R-3 plan-validator 致命中4 反映：Modify 直前 snapshot（毎回・専用ローテで軽量）
        EnsureSnapshot();

        try
        {
            _provider.AddNew(created);

            created.IsNew = false;        // 永続化済
            created.SnapshotOriginal();   // OriginalCommand を確定
            created.PropertyChanged += Entry_PropertyChanged;
            Entries.Add(created);

            SelectedEntry = created;
            StatusMessage = $"🆕 {created.Name} を {created.HiveDisplay} に追加しました。";
            NotifyDirtyChanged();
        }
        catch (UnauthorizedAccessException) when (HandleElevationNeeded(created.Name)) { return; }
        catch (SecurityException)             when (HandleElevationNeeded(created.Name)) { return; }
        catch (InvalidOperationException ex)  when (LooksLikeAccessDenied(ex.Message) && HandleElevationNeeded(created.Name)) { return; }
        catch (InvalidOperationException ex)
        {
            // 同名重複等
            _dialogs.Error($"追加に失敗しました：\n{ex.Message}", "追加失敗");
            StatusMessage = $"❌ 追加失敗: {ex.Message}";
        }
        catch (ArgumentException ex)
        {
            _dialogs.Error($"値名が無効です：\n{ex.Message}", "値名エラー");
            StatusMessage = $"❌ 追加失敗: {ex.Message}";
        }
        catch (Exception ex)
        {
            _dialogs.Error($"追加に失敗しました：\n{ex.Message}", "追加失敗");
            StatusMessage = $"❌ 追加失敗: {ex.Message}";
        }
    }

    /// <summary>
    /// R-3：選択中エントリを削除（Run キー + StartupApproved 両方）。
    /// 削除前 Confirm + Modify 直前 snapshot 自動取得。
    /// </summary>
    [RelayCommand]
    private void Delete()
    {
        if (SelectedEntry == null)
        {
            _dialogs.Info("削除するエントリを一覧から選択してください。", "未選択");
            return;
        }
        if (_privilege.IsShutdownPending) return;

        var target = SelectedEntry;

        var ok = _dialogs.Confirm(
            $"以下のスタートアップエントリを削除しますか？\n\n" +
            $"  名前：{target.Name}\n" +
            $"  ハイブ：{target.HiveDisplay}\n" +
            $"  コマンド：{target.Command}\n\n" +
            "※ レジストリ値と StartupApproved の両方から削除されます。\n" +
            "※ 削除直前の snapshot から「🔄 復元」で戻せます。",
            "削除確認");
        if (!ok) return;

        // HKLM 系 + 非管理者 → 事前 3 択
        if (IsHklm(target.Hive) && !_privilege.IsRunningAsAdministrator())
        {
            var choice = _dialogs.ConfirmElevation(target.Name, isFromError: false);
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

        // R-3 plan-validator 致命中4 反映：Modify 直前 snapshot
        EnsureSnapshot();

        try
        {
            _provider.Delete(target);

            // 購読解除 → 一覧から削除（メモリリーク防止）
            target.PropertyChanged -= Entry_PropertyChanged;
            Entries.Remove(target);

            StatusMessage = $"🗑 {target.Name} を {target.HiveDisplay} から削除しました。";
            NotifyDirtyChanged();
        }
        catch (UnauthorizedAccessException) when (HandleElevationNeeded(target.Name)) { return; }
        catch (SecurityException)             when (HandleElevationNeeded(target.Name)) { return; }
        catch (InvalidOperationException ex)  when (LooksLikeAccessDenied(ex.Message) && HandleElevationNeeded(target.Name)) { return; }
        catch (Exception ex)
        {
            _dialogs.Error($"削除に失敗しました：\n{ex.Message}", "削除失敗");
            StatusMessage = $"❌ 削除失敗: {ex.Message}";
        }
    }

    /// <summary>
    /// R-3：最新スタートアップ snapshot から復元（reg.exe import）。
    /// IsDirty 中エントリがあれば事前確認（plan-validator 致命高2）。
    /// HKLM 系を含むため、復元自体に管理者権限が必要。
    /// </summary>
    [RelayCommand]
    private void RestoreSnapshot()
    {
        if (_privilege.IsShutdownPending) return;

        // R-3 plan-validator 致命高2 反映：IsDirty 中エントリ保護
        var dirtyCount = Entries.Count(e => e.IsDirty);
        if (dirtyCount > 0)
        {
            var okDirty = _dialogs.Confirm(
                $"未保存の編集が {dirtyCount} 件あります。\n" +
                "スナップショット復元で全て破棄されます。\n\n続行しますか？",
                "未保存の編集あり");
            if (!okDirty) { StatusMessage = "🔄 復元をキャンセルしました。"; return; }
        }

        var latest = _backup.GetLatestStartupSnapshotPath();
        if (latest == null)
        {
            _dialogs.Info(
                "スタートアップ snapshot がまだ取得されていません。\n" +
                "（追加・削除・編集・トグル操作の直前に自動取得されます）",
                "snapshot なし");
            return;
        }

        // A（v0.1.6）：差分算出・復元より「前」に SHA256 整合性を検証する。
        // 壊れた／改ざんされた snapshot を差分解析や reg import に通さないため、ここが最初の関門。
        //   IsValid==false（改ざんの疑い or サイドカー破損）→ 赤色警告で明示確認（既定いいえ）。
        //   IsValid==null（サイドカーなし＝v0.1.5 以前の古い snapshot）→ 警告なしで通常フロー。
        var (integrityValid, expectedHash, actualHash) = _backup.VerifyStartupSnapshotIntegrity(latest);
        if (integrityValid == false)
        {
            var expShort = ShortenHash(expectedHash);
            var actShort = ShortenHash(actualHash);
            // cross-reviewer 軽微1：破壊側に倒れてはいけないので「既定＝いいえ」の ConfirmDestructive を使う。
            var continueAnyway = _dialogs.ConfirmDestructive(
                "⚠️ スナップショットの整合性チェックに失敗しました。\n\n" +
                $"  ファイル：{Path.GetFileName(latest)}\n" +
                $"  期待ハッシュ：{expShort}\n" +
                $"  実ハッシュ　：{actShort}\n\n" +
                "snapshot が改ざんされている可能性があります。\n" +
                "※ これはファイルの改ざん有無のみの確認です\n" +
                "　（内容の新しさは上の作成日時で判断してください）。\n\n" +
                "それでも復元しますか？（推奨：いいえ）",
                "整合性エラー");
            if (!continueAnyway) { StatusMessage = "🔄 復元をキャンセルしました（整合性エラー）。"; return; }
        }

        // B（v0.1.6）：復元前に差分件数を算出して Confirm に提示する。
        // snapshot は読込のみ（ImportKey せず）＝副作用なし。算出に失敗しても復元自体は止めない。
        // NOTE（cross-reviewer 重要3）：差分算出は _provider.LoadAll()（6 Run + 3 StartupApproved キーを
        //   レジストリ Open）を 1 回呼ぶ同期処理。通常エントリ数（数十件）で体感数十 ms。
        //   Confirm 提示が主目的で非同期化の効果が薄いため、意図的に同期実行する。
        StartupDiffResult diff;
        bool analyzeOk;
        try
        {
            diff = StartupSnapshotDiffer.AnalyzeFile(latest, _provider.LoadAll());
            analyzeOk = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Startup] 差分算出失敗（復元は継続可）: {ex.Message}");
            diff = StartupDiffResult.Empty;
            analyzeOk = false;
        }

        // C（v0.1.6）：差分の内訳を 3 ペインプレビューで見せる（cross-reviewer 重要1 の「算出失敗を
        //   安全と誤表示しない」精神を踏襲）。
        if (!analyzeOk)
        {
            // 解析失敗＝プレビュー不可。壊れた snapshot を安全と誤認させないため、既定いいえの破壊的確認。
            var goAnyway = _dialogs.ConfirmDestructive(
                "⚠️ 差分を確認できませんでした（snapshot の解析に失敗）。\n\n" +
                $"  ファイル：{Path.GetFileName(latest)}\n" +
                $"  作成日時：{File.GetLastWriteTime(latest):yyyy-MM-dd HH:mm:ss}\n\n" +
                "中身を確認できないまま復元するのは危険です。\n" +
                "それでも全 6 Run + 3 StartupApproved キーを snapshot 時点に上書きしますか？（推奨：いいえ）",
                "復元（差分確認不可）");
            if (!goAnyway) { StatusMessage = "🔄 復元をキャンセルしました。"; return; }
        }
        else if (diff.TotalCount == 0)
        {
            // EC-1：差分 0 件＝復元しても無変化 → プレビュー不要・案内して終了。
            _dialogs.Info(
                "snapshot と現在の状態は一致しています。\n復元する変更がありません。",
                "差分なし");
            return;
        }
        else
        {
            // 3 ペインプレビュー（🗑削除 / ✏変更 / ➕追加）で内訳を確認 → 承認なければ中止。
            var approved = _dialogs.OpenRestorePreview(latest, diff);
            if (!approved) { StatusMessage = "🔄 復元をキャンセルしました。"; return; }
        }

        // snapshot は HKLM 系を含むため非管理者なら事前 3 択
        if (!_privilege.IsRunningAsAdministrator())
        {
            var choice = _dialogs.ConfirmElevation("スナップショット復元", isFromError: false);
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

        // A（v0.1.6・cross-reviewer 重要3）：TOCTOU 対策。
        // 最初の Verify〜AnalyzeFile〜ここまでの間に、外部プロセスが .reg を差し替える窓がある
        // （ComputeFileSha256 は FileShare.Read で書込ロックしない）。import 直前にもう一度検証し、
        // 差し替えを検知したら復元を中止する（脅威モデル シナリオ2 のすり抜け防止）。
        var (recheckValid, _, _) = _backup.VerifyStartupSnapshotIntegrity(latest);
        if (recheckValid == false)
        {
            _dialogs.Error(
                "復元直前の再チェックで snapshot の整合性エラーを検出しました。\n" +
                "他のプロセスがファイルを書き換えた可能性があります。\n\n" +
                "安全のため復元を中止しました。",
                "整合性エラー（復元中止）");
            StatusMessage = "❌ 復元中止：整合性エラー（import 直前の再チェック）";
            return;
        }

        try
        {
            _backup.ImportKey(latest);

            // R-3 plan-validator 致命高2 既知分対応：復元後は Load() で Entries 全入替（購読も張り直し）
            Load();

            StatusMessage = $"🔄 snapshot から復元しました：{Path.GetFileName(latest)}";
        }
        catch (UnauthorizedAccessException) when (HandleElevationNeeded("snapshot 復元")) { return; }
        catch (SecurityException)             when (HandleElevationNeeded("snapshot 復元")) { return; }
        catch (InvalidOperationException ex)  when (LooksLikeAccessDenied(ex.Message) && HandleElevationNeeded("snapshot 復元")) { return; }
        catch (Exception ex)
        {
            _dialogs.Error($"snapshot 復元に失敗しました：\n{ex.Message}", "復元失敗");
            StatusMessage = $"❌ 復元失敗: {ex.Message}";
        }
    }

    /// <summary>R-3 cross-reviewer 致命中4：直近 snapshot 取得時刻（10 秒スロットル用）。</summary>
    private DateTime _lastSnapshotTime = DateTime.MinValue;

    /// <summary>R-3 cross-reviewer 致命中4：snapshot スロットル間隔（連続 Modify で 9 プロセス起動の体感固まりを回避）。</summary>
    private static readonly TimeSpan SnapshotThrottle = TimeSpan.FromSeconds(10);

    /// <summary>
    /// R-3：Modify 直前の snapshot 自動取得（plan-validator 致命中4：毎回方式 + cross-reviewer 致命中4：10 秒スロットル）。
    /// 専用サブディレクトリ + 独自ローテーション 10 件で軽量化（既存個別バックアップへの影響なし）。
    /// 連続 Modify では 1 回目だけ取得し、以降 10 秒は skip（reg.exe 9 連発による UI 固まり回避）。
    /// 失敗時は warn のみ・Modify は止めない（破壊操作を中断しない選択）。
    /// </summary>
    private void EnsureSnapshot()
    {
        var now = DateTime.UtcNow;
        if (now - _lastSnapshotTime < SnapshotThrottle)
        {
            Debug.WriteLine($"[Startup] snapshot スロットル（前回から {(now - _lastSnapshotTime).TotalSeconds:F1}秒・skip）");
            return;
        }

        try
        {
            _backup.BackupStartupSnapshot();
            _lastSnapshotTime = now;
        }
        catch (Exception ex)
        {
            // ログ出力のみ（StatusMessage は上書きすると本来の Modify 結果が見えなくなるので避ける）
            Debug.WriteLine($"[Startup] snapshot 取得失敗（Modify は続行）: {ex.Message}");
        }
    }

    /// <summary>A（v0.1.6）：整合性エラー表示用にハッシュを先頭 16 桁＋… に短縮（null/短い場合も安全）。</summary>
    private static string ShortenHash(string? hash)
    {
        if (string.IsNullOrEmpty(hash)) return "(取得不可)";
        return hash.Length <= 16 ? hash : hash.Substring(0, 16) + "...";
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
