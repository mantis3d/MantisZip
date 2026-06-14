using System.Globalization;
using System.Text.Json;

namespace MantisZip.UI.Avalonia.Services;

public enum AppLanguage
{
    Chinese,
    English
}

public static class LocalizationManager
{
    private static Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);
    private static AppLanguage _currentLanguage = AppLanguage.Chinese;

    public static event EventHandler? CultureChanged;

    public static AppLanguage CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage == value) return;
            _currentLanguage = value;
            LoadStrings(value);
            CultureChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static string CurrentLanguageCode =>
        _currentLanguage == AppLanguage.English ? "en" : "zh-CN";

    static LocalizationManager()
    {
        LoadStrings(AppLanguage.Chinese);
    }

    private static void LoadStrings(AppLanguage lang)
    {
        var fileName = lang == AppLanguage.English ? "strings.en.json" : "strings.zh-CN.json";
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Localization", fileName);

        // Also check relative path for development
        if (!File.Exists(path))
        {
            path = Path.Combine(Directory.GetCurrentDirectory(), "Localization", fileName);
        }

        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            _strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Get localized string by key. Returns the key if not found.
    /// </summary>
    public static string T(string key)
    {
        return _strings.TryGetValue(key, out var value) ? value : key;
    }

    /// <summary>
    /// Get localized string with format arguments.
    /// </summary>
    public static string T(string key, params object?[] args)
    {
        var format = T(key);
        try { return string.Format(format, args); }
        catch { return format; }
    }
}
