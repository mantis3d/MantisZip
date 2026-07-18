using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MantisZip.UI.Avalonia.Converters;

/// <summary>
/// 将资源键名（字符串）转换为对应的 Geometry 对象。
/// 用于在运行时动态加载 PathIcon.Data 的 StaticResource。
/// </summary>
public class GeometryResourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string resourceKey && !string.IsNullOrEmpty(resourceKey))
        {
            if (Application.Current?.Resources.TryGetResource(resourceKey, null, out var resource) == true
                && resource is Geometry geometry)
                return geometry;
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
