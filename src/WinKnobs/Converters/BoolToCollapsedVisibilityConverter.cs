using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinKnobs.Converters;

/// <summary>
/// bool true → Collapsed / false → Visible。
/// MainWindow.xaml の既存 DataGrid（ItemsGrid）を Startup カテゴリ選択時に隠すために使用。
/// 設計書 5-1（plan-validator #1 反映後の Visibility 切替方式）参照。
///
/// 使い方（XAML）：
///   Visibility="{Binding IsStartupCategorySelected, Converter={StaticResource BoolToCollapsedVis}}"
/// </summary>
public class BoolToCollapsedVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
