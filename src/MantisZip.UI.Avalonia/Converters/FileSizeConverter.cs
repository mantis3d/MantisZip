using System.Globalization;
using Avalonia.Data.Converters;
using MantisZip.Core.Utils;

namespace MantisZip.UI.Avalonia.Converters;

public class FileSizeConverter : IValueConverter
{
    private static readonly string[] SizeUnits = ["B", "KB", "MB", "GB", "TB"];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes)
            return null;

        return FormatUtil.FormatSize(bytes);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
