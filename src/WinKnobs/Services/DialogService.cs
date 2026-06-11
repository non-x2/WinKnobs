using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using WinKnobs.Models;
using WinKnobs.ViewModels;
using WinKnobs.ViewModels.Startup;
using WinKnobs.Views;
using WinKnobs.Views.Startup;

namespace WinKnobs.Services;

/// <summary>
/// v0.1.4 BUG-3：管理者として再起動するかどうかのユーザー選択結果。
/// </summary>
public enum ElevationChoice
{
    /// <summary>管理者として再起動する。</summary>
    RestartAsAdmin,
    /// <summary>このまま続行する（書込で失敗する可能性あり）。</summary>
    ContinueAnyway,
    /// <summary>キャンセル（何もしない）。</summary>
    Cancel
}

/// <summary>
/// ダイアログ表示の責務を集約するサービス。
/// 致命度高（F-2）対策：Owner = Application.Current.MainWindow を必ず指定。
/// エッジケース（E-3）対策：SettingDialog の重複起動を _currentDialog でガード。
/// v0.1.4 BUG-3：ConfirmElevation で「管理者として再起動」3択を提供。
/// </summary>
public class DialogService
{
    private readonly IServiceProvider _services;
    private SettingDialog? _currentDialog;

    public DialogService(IServiceProvider services)
    {
        _services = services;
    }

    private static Window? Owner => Application.Current?.MainWindow;

    /// <summary>はい/いいえ確認。既定ボタンは「はい」。</summary>
    public bool Confirm(string message, string title = "確認")
    {
        var result = Owner != null
            ? MessageBox.Show(Owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    /// <summary>
    /// 破壊的操作の確認（既定ボタン＝いいえ・警告アイコン）。
    /// 標準 Confirm は既定＝はいのため、誤操作（Enter 連打等）で破壊側に倒れてはいけない確認に使う。
    /// cross-reviewer 軽微1：整合性エラー時の「それでも復元しますか？（推奨いいえ）」が既定はいだと意図と逆転するため追加。
    /// </summary>
    public bool ConfirmDestructive(string message, string title = "確認")
    {
        var result = Owner != null
            ? MessageBox.Show(Owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    /// <summary>警告表示。</summary>
    public void Warn(string message, string title = "注意")
    {
        if (Owner != null)
            MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>情報表示。</summary>
    public void Info(string message, string title = "お知らせ")
    {
        if (Owner != null)
            MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>エラー表示。</summary>
    public void Error(string message, string title = "エラー")
    {
        if (Owner != null)
            MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        else
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>
    /// v0.1.4 BUG-3：管理者として再起動するかをユーザーに確認するダイアログ。
    /// isFromError=false（事前確認）：3 択（再起動/続行/キャンセル）
    /// isFromError=true（事後確認・cross-reviewer U-1）：2 択（再起動/戻る）。続行の選択肢は意味がないため。
    /// </summary>
    /// <param name="itemName">対象項目のタイトル（ダイアログ文に埋め込む）。</param>
    /// <param name="isFromError">true: 書込失敗後の事後確認 / false: HKLM 系の事前確認。</param>
    public ElevationChoice ConfirmElevation(string itemName, bool isFromError)
    {
        if (isFromError)
        {
            // 事後確認は YesNo 2 択（cross-reviewer U-1）
            string errMsg =
                $"「{itemName}」の操作に失敗しました（管理者権限が必要です）。\n\n" +
                "管理者として WinKnobs を再起動しますか？（推奨）\n" +
                "（UAC 同意ダイアログが出ます。\n" +
                "  再起動後、もう一度この項目を選んで適用してください。\n" +
                "  現在のダイアログは閉じられます。）";

            var errResult = Owner != null
                ? MessageBox.Show(Owner, errMsg, "管理者権限が必要",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.Yes)
                : MessageBox.Show(errMsg, "管理者権限が必要",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.Yes);

            return errResult == MessageBoxResult.Yes
                ? ElevationChoice.RestartAsAdmin
                : ElevationChoice.Cancel;
        }

        // 事前確認は 3 択（推奨：再起動 / 続行：自己責任 / キャンセル）
        string preMsg =
            $"「{itemName}」は HKLM 系（システム全体）の設定で\n管理者権限が必要です。\n\n" +
            "管理者として WinKnobs を再起動しますか？（推奨）\n" +
            "（UAC 同意ダイアログが出ます。\n" +
            "  再起動後、もう一度この項目を選んで適用してください。）\n\n" +
            "「いいえ」：このまま続行する（書込で失敗する可能性あり）。\n" +
            "「キャンセル」：何もせずに戻る。";

        var preResult = Owner != null
            ? MessageBox.Show(Owner, preMsg, "管理者権限が必要",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Yes)
            : MessageBox.Show(preMsg, "管理者権限が必要",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Yes);

        return preResult switch
        {
            MessageBoxResult.Yes => ElevationChoice.RestartAsAdmin,
            MessageBoxResult.No  => ElevationChoice.ContinueAnyway,
            _                    => ElevationChoice.Cancel,  // Cancel または × ボタン
        };
    }

    /// <summary>
    /// スタートアップエントリ編集ダイアログを開く（Phase R-2）。
    /// 戻り値：true=OK / false=キャンセル / null=オーナーなしで何もしなかった等。
    /// </summary>
    public bool? OpenStartupEdit(StartupEntryItem entry)
    {
        var vm = _services.GetRequiredService<StartupEntryEditViewModel>();
        vm.Load(entry);

        var dlg = _services.GetRequiredService<StartupEntryEditDialog>();
        dlg.DataContext = vm;
        dlg.Owner = Owner;

        return dlg.ShowDialog();
    }

    /// <summary>
    /// R-3：スタートアップエントリ追加ダイアログを開く（Add モード）。
    /// 戻り値：(DialogResult, CreatedEntry)。DialogResult==true なら CreatedEntry に新規エントリが入る。
    /// R-3 cross-reviewer 致命高1：Name 重複チェック関数を受け取り、ダイアログ内でリアルタイム警告を出す。
    /// </summary>
    public (bool? dialogResult, StartupEntryItem? createdEntry) OpenStartupAdd(
        StartupHive defaultHive,
        Func<StartupHive, string, (bool sameHiveDup, string? otherHiveDup)>? nameConflictChecker = null)
    {
        var vm = _services.GetRequiredService<StartupEntryEditViewModel>();
        vm.LoadNew(defaultHive, nameConflictChecker);

        var dlg = _services.GetRequiredService<StartupEntryEditDialog>();
        dlg.DataContext = vm;
        dlg.Owner = Owner;

        var result = dlg.ShowDialog();
        var created = (result == true) ? vm.CreateNewEntry() : null;
        return (result, created);
    }

    /// <summary>
    /// v0.1.6 機能 C：snapshot 復元前プレビューダイアログを開く。
    /// 3 ペイン（削除/変更/追加）で差分内訳を見せ、「内容を確認しました」チェック後のみ復元可。
    /// 戻り値：true=ユーザーが復元を承認 / false=キャンセル。
    /// 差分 0 件は呼び出し側で弾く前提（ここには 1 件以上の diff が来る）。
    /// </summary>
    public bool OpenRestorePreview(string snapshotPath, StartupDiffResult diff)
    {
        var vm = _services.GetRequiredService<RestorePreviewViewModel>();
        // 契約：Init は DataContext 設定より「前」（Init で値を確定 → DataContext 割当時に WPF が一括バインド）。
        vm.Init(diff, Path.GetFileName(snapshotPath), File.GetLastWriteTime(snapshotPath));

        var dlg = _services.GetRequiredService<RestorePreviewDialog>();
        dlg.DataContext = vm;
        dlg.Owner = Owner;

        return dlg.ShowDialog() == true;
    }

    /// <summary>
    /// v0.1.19 Step3：リモートデスクトップ **有効化（ON）** の Red 専用 強確認ダイアログを開く（🔴Red）。
    /// 現在の状態（<see cref="RdpStatus"/>）をコンテキスト提示し、「リスクを理解しました」チェック後のみ [有効化] が活性。
    /// 戻り値：true=ユーザーが同意して有効化を選択 / false=キャンセル。
    ///
    /// 素の <see cref="ConfirmDestructive"/>（MessageBox）では満たせない要件（チェック同意・状態提示）のための専用ダイアログ。
    /// Owner 指定で MainWindow に従属（F-2 と同方針）。
    /// </summary>
    public bool ConfirmRemoteDesktopEnable(RdpStatus status)
    {
        var vm = _services.GetRequiredService<RemoteDesktopEnableViewModel>();
        // 契約：Init は DataContext 設定より「前」（RestorePreviewViewModel と同流儀）。
        vm.Init(status);

        var dlg = _services.GetRequiredService<RemoteDesktopEnableDialog>();
        dlg.DataContext = vm;
        dlg.Owner = Owner;

        return dlg.ShowDialog() == true;
    }

    /// <summary>
    /// v0.2.1 Step5：Hyper-V **無効化（OFF）** の Red 専用 強確認ダイアログを開く（🔴Red）。
    /// 現在の launchtype とコア分離状態をコンテキスト提示し、「リスクを理解しました」チェック後のみ [無効化] が活性。
    /// 戻り値：true=ユーザーが同意して無効化を選択 / false=キャンセル。
    /// <see cref="ConfirmRemoteDesktopEnable"/> と同流儀（Init は DataContext 設定より「前」・Owner 指定）。
    /// </summary>
    /// <param name="currentStateLabel">HyperVHelper.DescribeMenuStatusLabel の生成ラベル。</param>
    /// <param name="isCoreIsolationOn">コア分離（HVCI）が有効か（true で依存ガイドを表示・plan-validator 致命2）。</param>
    public bool ConfirmHyperVDisable(string currentStateLabel, bool isCoreIsolationOn)
    {
        var vm = _services.GetRequiredService<HyperVDisableViewModel>();
        vm.Init(currentStateLabel, isCoreIsolationOn);

        var dlg = _services.GetRequiredService<HyperVDisableDialog>();
        dlg.DataContext = vm;
        dlg.Owner = Owner;

        return dlg.ShowDialog() == true;
    }

    /// <summary>
    /// SettingDialog を開く。既に開いていれば前面化のみ（E-3 対策）。
    /// </summary>
    public bool? OpenSettingItem(SettingItem item)
    {
        if (_currentDialog != null)
        {
            _currentDialog.Activate();
            return null;
        }

        var vm = _services.GetRequiredService<SettingDialogViewModel>();
        vm.Load(item);

        var dlg = _services.GetRequiredService<SettingDialog>();
        dlg.DataContext = vm;
        dlg.Owner = Owner; // F-2 対策

        _currentDialog = dlg;
        try
        {
            return dlg.ShowDialog();
        }
        finally
        {
            _currentDialog = null;
        }
    }

    /// <summary>保存ファイルダイアログ。</summary>
    public string? ShowSaveFileDialog(string filter, string defaultName = "")
    {
        var dlg = new SaveFileDialog
        {
            Filter = filter,
            FileName = defaultName
        };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    /// <summary>開くファイルダイアログ。</summary>
    public string? ShowOpenFileDialog(string filter)
    {
        var dlg = new OpenFileDialog
        {
            Filter = filter
        };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }
}
