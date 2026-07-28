using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MantisZip.UI.Avalonia.Converters;

/// <summary>
/// 将 bool（IsArchiveNode）转换为 FontWeight：true → Bold，false → Normal。
/// </summary>
public class BoolToBoldConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return FontWeight.Bold;
        return FontWeight.Normal;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingNotification.UnsetValue;
}

/// <summary>
/// 将 bool（IsArchiveNode）转换为 FontSize：true → 14，false → 12。
/// </summary>
public class BoolToArchiveFontSizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return 14.0;
        return 12.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingNotification.UnsetValue;
}

/// <summary>
/// 将 bool（ExistsAtDestination）转换为前景画刷：true → 红色，false → 默认（UnsetValue 以回退到主题）。
/// </summary>
public class BoolToConflictBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ConflictBrush = new(0xFFD32F2F);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return ConflictBrush;
        return BindingNotification.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingNotification.UnsetValue;
}

/// <summary>
/// 将 bool（IsEmptyDirectory）转换为双精度不透明度：true → 0.45，false → 1.0。
/// </summary>
public class BoolToEmptyDirOpacityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return 0.45;
        return 1.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingNotification.UnsetValue;
}
