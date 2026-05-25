using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinKnobs.Converters;

/// <summary>
/// bool true → FontWeights.Bold / false → FontWeights.Normal。
/// A3-2-c で導入：SettingDialog の「元の値」表示で
/// IsModified（編集中）の場合に太字化するために使用（plan-validator V-6）。
///
/// 使い方（XAML）：
///   FontWeight="{Binding IsModified, Converter={StaticResource BoolToFontWeight}}"
/// </summary>
public class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? FontWeights.Bold : FontWeights.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
