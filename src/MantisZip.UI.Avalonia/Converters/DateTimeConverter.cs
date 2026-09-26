using Avalonia.Data.Converters;
using System.Globalization;

namespace MantisZip.UI.Avalonia.Converters;

/// <summary>
/// 在 long? 和 string 之间转换（用于 TextBox 绑定）。
/// 空字符串转换为 null，输入文本尝试解析为 long。
/// </summary>
public class NullableLongConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long l)
            return l.ToString();
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && long.TryParse(s, out var result))
            return result;
        return null;
    }
}
