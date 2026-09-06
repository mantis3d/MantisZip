using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MantisZip.UI.Avalonia.Converters;

/// <summary>
/// 将 bool 转换为两个图标 Geometry 之一（false → 第一个 key，true → 第二个 key）。
/// 用于 PathIcon 的运行时图标切换（如密码显示/隐藏按钮：隐藏=IconEye，显示=IconEyeOff）。
/// 用法：Data="{Binding IsRevealed, Converter={StaticResource BoolToIconConverter}, ConverterParameter=IconEye|IconEyeOff}"
/// </summary>
public class BoolToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is string keys &&
            keys.Split('|') is { Length: 2 } parts)
        {
            var key = value is true ? parts[1].Trim() : parts[0].Trim();
            if (Application.Current?.Resources.TryGetResource(key, null, out var resource) == true
                && resource is Geometry geometry)
                return geometry;
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
