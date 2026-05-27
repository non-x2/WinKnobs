using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WinKnobs.ViewModels.Startup;

namespace WinKnobs.Views.Startup;

/// <summary>
/// スタートアップエントリ編集ダイアログ。
/// Phase R-2 実装。
///
/// ViewModel の DialogResult 変更を購読してウィンドウを閉じる。
/// （CommunityToolkit.Mvvm から WPF Window.Close を呼ぶ標準パターン）
/// </summary>
public partial class StartupEntryEditDialog : Window
{
    public StartupEntryEditDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is StartupEntryEditViewModel oldVm)
        {
            oldVm.PropertyChanged -= Vm_PropertyChanged;
        }
        if (e.NewValue is StartupEntryEditViewModel newVm)
        {
            newVm.PropertyChanged += Vm_PropertyChanged;
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StartupEntryEditViewModel.DialogResult)) return;
        if (sender is not StartupEntryEditViewModel vm) return;
        if (vm.DialogResult.HasValue)
        {
            DialogResult = vm.DialogResult;
            Close();
        }
    }

    /// <summary>
    /// OK ボタン Click ハンドラ。
    /// UI Automation の ValuePattern.SetValue では TextBox のバインド source が
    /// 確実に更新されないケースがある（特に UpdateSourceTrigger=PropertyChanged + Hidden モード時）。
    /// Click 時に明示的に BindingExpression.UpdateSource() を呼び、その後 OkCommand に渡す。
    /// R-2 UIA テスト save.verify FAIL の対策。
    /// R-3：Name TextBox（Add モード）にも同様の対策を適用。
    /// </summary>
    private void StartupEditOkButton_Click(object sender, RoutedEventArgs e)
    {
        var cmdBinding = StartupEditCommandBox.GetBindingExpression(TextBox.TextProperty);
        cmdBinding?.UpdateSource();

        // R-3：Name TextBox は Add モード時のみ表示（Visibility=Collapsed なら GetBindingExpression は null を返す）
        var nameBinding = StartupEditNameBox.GetBindingExpression(TextBox.TextProperty);
        nameBinding?.UpdateSource();
    }
}
