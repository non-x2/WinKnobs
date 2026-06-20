using System.ComponentModel;
using System.Windows;
using WinKnobs.ViewModels;

namespace WinKnobs.Views;

/// <summary>
/// v0.3.0 仮想メモリ（ページファイル）設定の 🔴Red 専用ダイアログ。
/// HyperVDisableDialog 同型：ViewModel の DialogResult 変更を購読してウィンドウを閉じる。
/// 既定フォーカスは [キャンセル]（誤確定防止）。
/// </summary>
public partial class VirtualMemoryDialog : Window
{
    public VirtualMemoryDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BtnVmCancel.Focus();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is VirtualMemoryViewModel oldVm)
            oldVm.PropertyChanged -= Vm_PropertyChanged;
        if (e.NewValue is VirtualMemoryViewModel newVm)
            newVm.PropertyChanged += Vm_PropertyChanged;
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VirtualMemoryViewModel.DialogResult)) return;
        if (sender is not VirtualMemoryViewModel vm) return;
        if (vm.DialogResult.HasValue)
        {
            DialogResult = vm.DialogResult;
            Close();
        }
    }
}
