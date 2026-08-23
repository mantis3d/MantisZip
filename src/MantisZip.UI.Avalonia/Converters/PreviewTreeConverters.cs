using System.Globalization;
using Avalonia;
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
/// 将 PreviewTreeNode.ForegroundKey 转换为对应画刷：Purple → 紫色，ConflictRed → 红色，其他 → 主题色。
/// 优先级：空存档(紫) > 文件冲突(红) > 默认回退主题色（从 Application.Current 动态解析）。
/// </summary>
public class NodeForegroundConverter : IValueConverter
{
    private static readonly SolidColorBrush PurpleBrush = new(0xFF9C27B0);
    private static readonly SolidColorBrush RedBrush = new(0xFFD32F2F);
    private static readonly SolidColorBrush BlueBrush = new(0xFF2196F3);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key)
        {
            return key switch
            {
                "Purple" => PurpleBrush,
                "ConflictRed" => RedBrush,
                "Blue" => BlueBrush,
                _ => GetThemeBrush(),
            };
        }
        return GetThemeBrush();
    }

    /// <summary>
    /// 从 Application.Current 动态解析 ThemeTextPrimaryBrush。
    /// 每次调用时重新读取，确保在主题切换后绑定重新求值时返回正确的画刷。
    /// Avalonia 12 的 TryGetResource 需要传入 ThemeVariant 参数。
    /// </summary>
    private static IBrush GetThemeBrush()
    {
        var app = Application.Current;
        if (app != null
            && app.TryGetResource("ThemeTextPrimaryBrush", app.ActualThemeVariant, out var rsc)
            && rsc is IBrush brush)
            return brush;
        return new SolidColorBrush(Colors.Black);
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
