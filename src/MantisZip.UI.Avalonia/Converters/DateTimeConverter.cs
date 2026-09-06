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

/// <summary>
/// 在 DateTime? 和 DatePicker.SelectedDate（实际为 DateTimeOffset?）之间转换。
/// Avalonia 12 中 DatePicker.SelectedDate 在某些 NuGet 版本组合下仍为 DateTimeOffset?，
/// 直接绑定 DateTime? 会引发 InvalidCastException。
/// </summary>
public class DateTimeToOffsetConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dt)
            return new DateTimeOffset(dt);
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTimeOffset dto)
            return dto.DateTime;
        return null;
    }
}
