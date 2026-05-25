using System.Globalization;
using System.Windows.Data;

namespace WinKnobs.Converters;

/// <summary>
/// bool 値を反転する Converter。
/// A2 で導入：SettingDialog の「無効 (0)」RadioButton で「NewBoolValue の逆」を IsChecked にバインドするために使用。
///
/// 使い方（XAML）：
///   IsChecked="{Binding NewBoolValue, Converter={StaticResource InvertBoolConv}}"
/// </summary>
public class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
