using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinKnobs.Converters;

/// <summary>
/// Enum 値と ConverterParameter（文字列）を比較して Visibility を返す。
/// A2 で導入：SettingDialog の編集UI（Boolean/DWord/String/ExpandString）切替に使用。
///
/// 使い方（XAML）：
///   Visibility="{Binding ValueType, Converter={StaticResource EnumToVis}, ConverterParameter=Boolean}"
/// </summary>
public class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return Visibility.Collapsed;
        return value.ToString() == parameter.ToString()
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
