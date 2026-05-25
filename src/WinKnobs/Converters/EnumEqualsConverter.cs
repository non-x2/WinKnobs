using System.Globalization;
using System.Windows.Data;

namespace WinKnobs.Converters;

/// <summary>
/// Enum 値と ConverterParameter（文字列）を比較して bool を返す。
/// 2026-05-16 追加：表示メニューの「ライト/ダーク/システム」テーマ項目で
/// 現在選択中のテーマだけにチェックマークを付けるために使用。
///
/// 使い方（XAML）：
///   IsChecked="{Binding CurrentThemeMode, Converter={StaticResource EnumEquals},
///                       ConverterParameter=Light, Mode=OneWay}"
/// </summary>
public class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        return value.ToString() == parameter.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
