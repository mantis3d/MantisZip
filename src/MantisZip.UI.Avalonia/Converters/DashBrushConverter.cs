using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using System.Globalization;

namespace MantisZip.UI.Avalonia.Converters;

/// <summary>
/// 统计卡行值画刷转换：值为 —/null/空 → ThemeTextSecondaryBrush（灰显占位），
/// 真实值 → ConverterParameter 指定的强调色资源键（解析失败回退 secondary，
/// secondary 也失败返回 null 由 Foreground 继承父控件）。
/// 用于三行统计卡的动态 — 单元格（5 处：卡1/2/3/4 行 3 与总大小卡行 2，D3）。
/// </summary>
public class DashBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isDash = value is not string s || string.IsNullOrEmpty(s) || s == "—";
        var key = isDash || parameter is not string accentKey
            ? "ThemeTextSecondaryBrush"
            : accentKey;
        if (Application.Current?.Resources.TryGetResource(key, ThemeVariant.Default, out var resource) == true
            && resource is IBrush brush)
            return brush;
        // 回退 secondary（若仍失败返回 null，Foreground 继承父控件）
        if (!isDash
            && Application.Current?.Resources.TryGetResource("ThemeTextSecondaryBrush", ThemeVariant.Default, out var fallback) == true
            && fallback is IBrush secondary)
            return secondary;
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
