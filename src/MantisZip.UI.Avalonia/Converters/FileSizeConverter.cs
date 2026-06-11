using System.Globalization;
using Avalonia.Data.Converters;

namespace MantisZip.UI.Avalonia.Converters;

public class FileSizeConverter : IValueConverter
{
    private static readonly string[] SizeUnits = ["B", "KB", "MB", "GB", "TB"];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes)
            return null;

        if (bytes == 0)
            return "0 B";

        var unitIndex = 0;
        var size = (double)bytes;

        while (size >= 1024 && unitIndex < SizeUnits.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{bytes} {SizeUnits[unitIndex]}"
            : $"{size:F2} {SizeUnits[unitIndex]}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
