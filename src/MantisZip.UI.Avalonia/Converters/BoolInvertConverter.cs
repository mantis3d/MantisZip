using Avalonia.Data.Converters;
using System.Globalization;

namespace MantisZip.UI.Avalonia.Converters;

/// <summary>
/// Inverts a boolean value. Used for binding IsVisible to "!IsArchiveLoaded".
/// </summary>
public class BoolInvertConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return false;
    }
}
