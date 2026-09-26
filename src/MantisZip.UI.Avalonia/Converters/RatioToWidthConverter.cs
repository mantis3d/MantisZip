using Avalonia.Data;
using Avalonia.Data.Converters;
using System.Globalization;

namespace MantisZip.UI.Avalonia.Converters;

public class RatioToWidthConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && values[0] is double ratio && values[1] is double availableWidth)
        {
            return ratio * availableWidth;
        }
        return 0.0;
    }
}
