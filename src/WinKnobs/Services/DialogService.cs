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

    /// <summary>はい/いいえ確認。</summary>
    public bool Confirm(string message, string title = "確認")
    {
        var result = Owner != null
            ? MessageBox.Show(Owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
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
