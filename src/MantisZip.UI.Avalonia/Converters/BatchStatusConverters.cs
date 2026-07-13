using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MantisZip.Core.Models;
using MantisZip.UI.Avalonia.Services;

namespace MantisZip.UI.Avalonia.Converters;

/// <summary>
/// 将 BatchItemStatus 转换为本地化状态文本。
/// </summary>
public class BatchStatusToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is BatchItemStatus status)
        {
            return status switch
            {
                BatchItemStatus.Pending => LocalizationManager.T("Progress_Batch_Status_Pending"),
                BatchItemStatus.InProgress => LocalizationManager.T("Progress_Batch_Status_InProgress"),
                BatchItemStatus.Completed => LocalizationManager.T("Progress_Batch_Status_Completed"),
                BatchItemStatus.Skipped => LocalizationManager.T("Progress_Batch_Status_Skipped"),
                BatchItemStatus.Failed => LocalizationManager.T("Progress_Batch_Status_Failed"),
                _ => status.ToString()
            };
        }
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 将 BatchItemStatus 转换为图标字符串。
/// </summary>
public class BatchStatusToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is BatchItemStatus status)
        {
            return status switch
            {
                BatchItemStatus.Pending => "\u23F3",
                BatchItemStatus.InProgress => "\uD83D\uDD04",
                BatchItemStatus.Completed => "\u2705",
                BatchItemStatus.Skipped => "\u23ED\uFE0F",
                BatchItemStatus.Failed => "\u274C",
                _ => "\u2753"
            };
        }
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 将 Progress (double) 和 Status (BatchItemStatus) 转换为背景进度条填充笔刷。
/// MultiBinding: values[0]=Progress, values[1]=Status
/// Avalonia 的 IMultiValueConverter 接收 <c>IList&lt;object?&gt;</c> 或 <c>object?[]</c>。
/// </summary>
public class ProgressStatusToBackgroundConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        double progress = values.Count > 0 && values[0] is double dp ? dp : 0d;
        var status = values.Count > 1 && values[1] is BatchItemStatus st ? st : BatchItemStatus.Pending;

        // 使用半透明颜色叠加在背景上，深浅主题均能正确显示。
        // 透明度 35%，剩余部分由 ListView 底色透出。
        const byte alpha = 0x59; // 89 ≈ 35%
        var color = status switch
        {
            BatchItemStatus.Failed => Color.FromArgb(alpha, 0xF4, 0x43, 0x36),     // red
            BatchItemStatus.Completed => Color.FromArgb(alpha, 0x4C, 0xAF, 0x50),  // green
            BatchItemStatus.Skipped => Color.FromArgb(alpha, 0x00, 0xBC, 0xD4),    // cyan
            _ => Color.FromArgb(alpha, 0x42, 0xA5, 0xF5)                            // blue
        };

        double offset = progress / 100.0;
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(color, 0.0),
                new GradientStop(color, offset),
                new GradientStop(Colors.Transparent, offset),
                new GradientStop(Colors.Transparent, 1.0),
            }
        };
    }

    public object? ConvertBack(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
