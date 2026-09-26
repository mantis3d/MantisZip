using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using System.Globalization;

namespace MantisZip.UI.Avalonia.Converters;

/// <summary>
/// Converts a boolean toggle state to a background brush for the Total Commander-style
/// toggle icon box. true → ThemeToggleBrush (semi-transparent accent), false → Transparent (hollow).
/// </summary>
public class BoolToToggleBgBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
        {
            if (Application.Current?.Resources.TryGetResource("ThemeToggleBrush", ThemeVariant.Default, out var resource) == true
                && resource is IBrush toggleBg)
                return toggleBg;
        }

        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
