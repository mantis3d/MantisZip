using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MantisZip.UI.Avalonia.Converters;

/// <summary>
/// 将字符串转换为 bool：非 null 且非空 → true。
/// 用于控制控件的 IsVisible 绑定。
/// </summary>
public class StringNotEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s && !string.IsNullOrEmpty(s);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
