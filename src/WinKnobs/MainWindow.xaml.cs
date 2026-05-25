using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using WinKnobs.Models;
using WinKnobs.Services;
using WinKnobs.ViewModels;

namespace WinKnobs;

/// <summary>
/// メインウィンドウ。DataContext は App.xaml.cs で MainViewModel が注入される。
///
/// 採用済みの plan-validator 指摘：
/// - F-4：DataGrid.MouseDoubleClick で行ヘッダ・空行クリックの誤発火を VisualTreeHelper で防止
///
/// 2026-05-20 追加（機能要望対応）：
/// - ウィンドウ位置・サイズの永続化（OnSourceInitialized で復元・OnClosing で保存）
/// </summary>
public partial class MainWindow : Window
{
    private readonly WindowStateService? _windowStateService;

    public MainWindow()
    {
        InitializeComponent();
        // DI から WindowStateService を取得（App.xaml.cs で AddSingleton 登録済）
        // App.Current が null（XAML デザイナ等）のケースは握り潰す
        try
        {
            _windowStateService = (App.Current as App)?.Services?.GetService<WindowStateService>();
        }
        catch
        {
            _windowStateService = null;
        }
    }

    /// <summary>
    /// ウィンドウハンドル確定後・Show 前のタイミングで保存位置を復元。
    /// OnSourceInitialized は OnContentRendered より前で WindowStartupLocation の影響を受ける前。
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (_windowStateService == null) return;
        var saved = _windowStateService.Load();
        if (saved == null) return;  // 初回 or 不正値 or 画面外 → 既定値（XAML の初期値）を維持

        Top    = saved.Top;
        Left   = saved.Left;
        Width  = saved.Width;
        Height = saved.Height;

        // WindowState は Normal/Maximized のみ
        if (saved.WindowState == "Maximized")
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// 閉じる直前にウィンドウ状態を保存。
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel)
        {
            _windowStateService?.Save(this);
        }
    }

    /// <summary>
    /// F-4 対策：DataGridRow を辿って、データ行クリック時のみコマンド実行。
    /// </summary>
    private void ItemsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element != null && element is not DataGridRow)
        {
            element = VisualTreeHelper.GetParent(element);
        }

        if (element is DataGridRow row && row.Item is SettingItem item && DataContext is MainViewModel vm)
        {
            vm.SelectedItem = item;
            if (vm.OpenSelectedItemCommand.CanExecute(null))
            {
                vm.OpenSelectedItemCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// TreeView の SelectedItem は読み取り専用なので、ViewModel へは手動で伝える。
    /// </summary>
    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm && e.NewValue is SettingCategory cat)
        {
            vm.SelectedCategory = cat;
        }
    }

    /// <summary>
    /// 編集メニュー→検索：検索ボックスにフォーカス。
    /// </summary>
    private void SearchMenu_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.SelectAll();
    }
}
