using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinKnobs.Converters;

/// <summary>
/// bool true → Visible / false → Collapsed。
/// MainWindow.xaml の StartupListView を Startup カテゴリ選択時のみ表示するために使用。
/// 設計書 5-1（plan-validator #1 反映後の Visibility 切替方式）参照。
///
/// 使い方（XAML）：
///   Visibility="{Binding IsStartupCategorySelected, Converter={StaticResource BoolToVisibleVis}}"
/// </summary>
public class BoolToVisibleVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
