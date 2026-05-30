using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MantisZip.Core.Models;
using MantisZip.UI.Localization;

namespace MantisZip.UI.Converters;

/// <summary>
/// 将 BatchItemStatus 转换为本地化状态文本。
/// </summary>
public class BatchStatusToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is BatchItemStatus status)
        {
            return status switch
            {
                BatchItemStatus.Pending => L.T(L.Progress_Batch_Status_Pending),
                BatchItemStatus.InProgress => L.T(L.Progress_Batch_Status_InProgress),
                BatchItemStatus.Completed => L.T(L.Progress_Batch_Status_Completed),
                BatchItemStatus.Failed => L.T(L.Progress_Batch_Status_Failed),
                _ => status.ToString()
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 将 BatchItemStatus 转换为图标字符串。
/// </summary>
public class BatchStatusToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is BatchItemStatus status)
        {
            return status switch
            {
                BatchItemStatus.Pending => "⏳",
                BatchItemStatus.InProgress => "🔄",
                BatchItemStatus.Completed => "✅",
                BatchItemStatus.Failed => "❌",
                _ => "❓"
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 将 Progress (double) 和 Status (BatchItemStatus) 转换为背景进度条填充笔刷。
/// MultiBinding: values[0]=Progress, values[1]=Status
/// </summary>
public class ProgressStatusToBackgroundConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        double progress = values is [double p, ..] ? p : 0d;
        var status = values is [_, BatchItemStatus s, ..] ? s : BatchItemStatus.Pending;

        var color = status switch
        {
            BatchItemStatus.Failed => Color.FromRgb(0xF4, 0x43, 0x36),     // red
            BatchItemStatus.Completed => Color.FromRgb(0x4C, 0xAF, 0x50),  // green
            _ => Color.FromRgb(0x42, 0xA5, 0xF5)                            // blue
        };

        double offset = progress / 100.0;
        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(color, 0.0),
                new GradientStop(color, offset),
                new GradientStop(Colors.Transparent, offset),
                new GradientStop(Colors.Transparent, 1.0),
            }
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
