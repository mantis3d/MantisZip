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
        if (values.Length == 2 && values[0] is double ratio && values[1] is double width)
            return ratio * width;
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
