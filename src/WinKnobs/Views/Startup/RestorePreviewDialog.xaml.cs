using System.ComponentModel;
using System.Windows;
using WinKnobs.ViewModels.Startup;

namespace WinKnobs.Views.Startup;

/// <summary>
/// v0.1.6 機能 C：snapshot 復元前プレビューダイアログ。
///
/// ViewModel の DialogResult 変更を購読してウィンドウを閉じる
/// （StartupEntryEditDialog と同じ標準パターン）。
/// このダイアログは TextBox を持たない（CheckBox + ボタンのみ）ため、
/// 編集ダイアログにあった BindingExpression.UpdateSource() の強制は不要。
/// </summary>
public partial class RestorePreviewDialog : Window
{
    public RestorePreviewDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is RestorePreviewViewModel oldVm)
        {
            oldVm.PropertyChanged -= Vm_PropertyChanged;
        }
        if (e.NewValue is RestorePreviewViewModel newVm)
        {
            newVm.PropertyChanged += Vm_PropertyChanged;
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RestorePreviewViewModel.DialogResult)) return;
        if (sender is not RestorePreviewViewModel vm) return;
        if (vm.DialogResult.HasValue)
        {
            DialogResult = vm.DialogResult;
            Close();
        }
    }
}
