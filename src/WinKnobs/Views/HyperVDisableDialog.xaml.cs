using System.ComponentModel;
using System.Windows;
using WinKnobs.ViewModels;

namespace WinKnobs.Views;

/// <summary>
/// v0.2.1 Step5：Hyper-V（ハイパーバイザー起動）**無効化（OFF）** の Red 専用 強確認ダイアログ（🔴Red）。
/// 構造は <see cref="RemoteDesktopEnableDialog"/> と同じ標準パターン：
/// ViewModel の DialogResult 変更を購読してウィンドウを閉じる。
///
/// ■ 誤確定防止：読み込み完了時に既定フォーカスを [キャンセル] に当てる。
///   [無効化] には IsDefault を付けていないため、Enter 連打では確定しない（チェック ON も必須）。
/// </summary>
public partial class HyperVDisableDialog : Window
{
    public HyperVDisableDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 既定フォーカス＝[キャンセル]（誤確定防止）
        BtnCancel.Focus();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is HyperVDisableViewModel oldVm)
        {
            oldVm.PropertyChanged -= Vm_PropertyChanged;
        }
        if (e.NewValue is HyperVDisableViewModel newVm)
        {
            newVm.PropertyChanged += Vm_PropertyChanged;
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HyperVDisableViewModel.DialogResult)) return;
        if (sender is not HyperVDisableViewModel vm) return;
        if (vm.DialogResult.HasValue)
        {
            DialogResult = vm.DialogResult;
            Close();
        }
    }
}
