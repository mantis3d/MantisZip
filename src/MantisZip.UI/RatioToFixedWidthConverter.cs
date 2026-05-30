using System.Globalization;
using System.Windows.Data;

namespace MantisZip.UI;

/// <summary>
/// 单值转换器：将 ratio 转换为 ratio * maxWidth（maxWidth 由 ConverterParameter 传入）。
/// 用于进度条 Rectangle 的 Width 绑定，替代旧的 MultiBinding + ActualWidth 方案。
/// 避免在 DataGrid 初次 Measure 阶段依赖 ContentPresenter.ActualWidth，
/// 从而消除 DataGridCellsPanel.DetermineRealizedColumnsBlockList 布局崩溃。
/// </summary>
public class RatioToFixedWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double ratio && double.IsFinite(ratio) && ratio > 0)
        {
            double maxWidth = 100;
            if (parameter is string paramStr && double.TryParse(paramStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                maxWidth = parsed;

            return Math.Clamp(ratio * maxWidth, 0, maxWidth);
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
