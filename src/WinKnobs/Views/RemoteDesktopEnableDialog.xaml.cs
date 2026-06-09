using System.ComponentModel;
using System.Windows;
using WinKnobs.ViewModels;

namespace WinKnobs.Views;

/// <summary>
/// v0.1.19 Step3：リモートデスクトップ **有効化（ON）** の Red 専用 強確認ダイアログ（🔴Red）。
///
/// 構造は <see cref="WinKnobs.Views.Startup.RestorePreviewDialog"/> と同じ標準パターン：
/// ViewModel の DialogResult 変更を購読してウィンドウを閉じる。
/// このダイアログは TextBox を持たない（CheckBox + ボタンのみ）ため UpdateSource 強制は不要。
///
/// ■ 誤確定防止（設計ドラフト §4-5）：読み込み完了時に既定フォーカスを [キャンセル] に当てる。
///   [有効化] には IsDefault を付けていないため、Enter 連打では確定しない（チェック ON も必須）。
/// </summary>
public partial class RemoteDesktopEnableDialog : Window
{
    public RemoteDesktopEnableDialog()
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
        if (e.OldValue is RemoteDesktopEnableViewModel oldVm)
        {
            oldVm.PropertyChanged -= Vm_PropertyChanged;
        }
        if (e.NewValue is RemoteDesktopEnableViewModel newVm)
        {
            newVm.PropertyChanged += Vm_PropertyChanged;
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RemoteDesktopEnableViewModel.DialogResult)) return;
        if (sender is not RemoteDesktopEnableViewModel vm) return;
        if (vm.DialogResult.HasValue)
        {
            DialogResult = vm.DialogResult;
            Close();
        }
    }
}
