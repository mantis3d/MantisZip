using System.Globalization;
using System.Windows.Data;

namespace MantisZip.UI;

/// <summary>
/// 多值转换器：将 (ratio, actualWidth) 转换为 ratio * actualWidth，
/// 用于进度条 Rectangle 的宽度绑定。
/// 四列共用同一个 Converter 实例。
/// </summary>
public class RatioToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // 安全保护：ratio 或 width 非有效值时返回 0，避免崩溃
        if (values.Length >= 2
            && values[0] is double ratio && double.IsFinite(ratio) && ratio > 0
            && values[1] is double width && double.IsFinite(width) && width > 0)
        {
            return Math.Min(ratio * width, width);  // 不超出列宽
        }
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
