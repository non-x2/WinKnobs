using System.ComponentModel;
using System.Windows;
using WinKnobs.Models;
using WinKnobs.ViewModels;

namespace WinKnobs.Views;

/// <summary>
/// 個別設定項目の編集ダイアログ。
/// DataContext は DialogService.OpenSettingItem 内で SettingDialogViewModel が注入される。
///
/// A3-2-c 改訂：
/// - V-7：HasUnappliedChanges → IsModified にリネーム反映
/// - A2 race condition 対策のプロパティ名も EditableBoolIndex に変更
/// </summary>
public partial class SettingDialog : Window
{
    public SettingDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closing += OnDialogClosing;
    }

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is SettingDialogViewModel vm)
        {
            // VM から閉じる動作（OK ボタンや Cancel ボタン経由）
            vm.CloseAction = () =>
            {
                if (IsLoaded) Close();
            };

            // A2 Race condition 対策（plan-validator D）：
            // Boolean/Command 項目の ComboBox SelectedIndex バインディングが初期化順序で
            // 不正値（-1 等）になることがあるため、Loaded で再通知する。
            RoutedEventHandler? loadedHandler = null;
            loadedHandler = (s, ev) =>
            {
                if (vm.ValueType == SettingValueType.Boolean || vm.ValueType == SettingValueType.Command)
                {
                    var idx = vm.EditableBoolIndex;
                    vm.EditableBoolIndex = idx;   // 同値再代入で PropertyChanged 発火
                }
                Loaded -= loadedHandler!;
            };
            Loaded += loadedHandler;
        }
    }

    /// <summary>
    /// 未保存変更（IsModified）がある状態で閉じようとしたら警告。
    /// V-7：旧 HasUnappliedChanges → IsModified に変更。
    /// VE-4：IsApplied && IsModified（適用後さらに編集）も拾う。
    /// </summary>
    private void OnDialogClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not SettingDialogViewModel vm) return;

        // 未編集なら警告不要
        if (!vm.IsModified) return;

        var result = MessageBox.Show(
            "変更がまだ適用されていません。\n\n" +
            "「はい」  → 変更を適用して閉じる\n" +
            "「いいえ」→ 変更を破棄して閉じる\n" +
            "「キャンセル」→ 閉じない（編集に戻る）",
            "未保存の変更",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        switch (result)
        {
            case MessageBoxResult.Yes:
                if (vm.ApplySettingCommand.CanExecute(null))
                {
                    vm.ApplySettingCommand.Execute(null);
                }
                // 適用失敗時は閉じない
                if (vm.IsModified)
                {
                    e.Cancel = true;
                }
                break;
            case MessageBoxResult.No:
                // 破棄して閉じる
                break;
            case MessageBoxResult.Cancel:
                e.Cancel = true;
                break;
        }
    }
}
