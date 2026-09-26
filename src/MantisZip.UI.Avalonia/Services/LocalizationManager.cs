using System.Globalization;
using System.Text.Json;

namespace MantisZip.UI.Avalonia.Services;

public enum AppLanguage
{
    Chinese,           // 简体中文（zh-CN）
    English,           // 英语（en）
    TraditionalChinese // 繁體中文（zh-TW）
}

public static class LocalizationManager
{
    private static Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);
    private static AppLanguage _currentLanguage = AppLanguage.Chinese;
    private static List<LanguageInfo> _availableLanguages = null!;

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

    public static string CurrentLanguageCode => _currentLanguage switch
    {
        AppLanguage.English => "en",
        AppLanguage.TraditionalChinese => "zh-TW",
        _ => "zh-CN",
    };

    /// <summary>
    /// AppLanguage → AppSettings.Language 持久化代码（"zh" / "en" / "zh-TW"）。
    /// </summary>
    public static string ToSettingsCode(AppLanguage lang) => lang switch
    {
        AppLanguage.English => "en",
        AppLanguage.TraditionalChinese => "zh-TW",
        _ => "zh",
    };

    /// <summary>
    /// AppSettings.Language → AppLanguage（未知值回退简体中文）。
    /// </summary>
    public static AppLanguage FromSettingsCode(string? code) => code switch
    {
        "en" => AppLanguage.English,
        "zh-TW" => AppLanguage.TraditionalChinese,
        _ => AppLanguage.Chinese,
    };

    static LocalizationManager()
    {
        LoadLanguageMetadata();
        LoadStrings(AppLanguage.Chinese);
    }

    private static void LoadStrings(AppLanguage lang)
    {
        var fileName = lang switch
        {
            AppLanguage.English => "strings.en.json",
            AppLanguage.TraditionalChinese => "strings.zh-TW.json",
            _ => "strings.zh-CN.json",
        };
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

    public class LanguageInfo
    {
        public string Code { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string TranslatorText { get; set; } = string.Empty;
    }

    public static List<LanguageInfo> AvailableLanguages => _availableLanguages;

    private class LanguageMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string Translator { get; set; } = string.Empty;
    }

    private static void LoadLanguageMetadata()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "languages.json");
        if (!File.Exists(path))
        {
            path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", "languages.json");
        }

        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var metadata = JsonSerializer.Deserialize<Dictionary<string, LanguageMetadata>>(json);
                if (metadata != null && metadata.Count > 0)
                {
                    _availableLanguages = new List<LanguageInfo>(metadata.Count);
                    foreach (var kvp in metadata)
                    {
                        var code = kvp.Key == "zh" ? "zh-CN" : kvp.Key;
                        _availableLanguages.Add(new LanguageInfo
                        {
                            Code = code,
                            DisplayName = kvp.Value.Name,
                            TranslatorText = kvp.Value.Translator
                        });
                    }
                    return;
                }
            }
            catch
            {
                // Fall through to hardcoded defaults
            }
        }

        _availableLanguages = new List<LanguageInfo>
        {
            new() { Code = "zh-CN", DisplayName = "中文", TranslatorText = "MantisZip 团队" },
            new() { Code = "zh-TW", DisplayName = "繁體中文", TranslatorText = "peter8777555" },
            new() { Code = "en", DisplayName = "English", TranslatorText = "Community Contributors" },
        };
    }
}
