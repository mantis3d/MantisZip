using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Styling;
using System.Globalization;

namespace MantisZip.UI.Avalonia.Converters;

/// <summary>
/// Resolves a string resource key to a SolidColorBrush from Application.Current.Resources.
/// Used by progress bar columns to switch between normal and directory baseline colors.
/// </summary>
public class BrushResourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key)
        {
            if (Application.Current?.Resources.TryGetResource(key, ThemeVariant.Default, out var resource) == true)
                return resource;
        }
        // Fallback: return a white brush so bars remain visible
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
