using System.Globalization;
using System.Text.Json;
using MantisZip.Core.Utils;

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
            _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonDefaults.CaseInsensitive)
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
                var parsed = ParseLanguageMetadata(File.ReadAllText(path));
                if (parsed.Count > 0)
                {
                    _availableLanguages = parsed;
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
            new() { Code = "zh-CN", DisplayName = "简体中文", TranslatorText = "MantisZip 团队" },
            new() { Code = "zh-TW", DisplayName = "繁體中文", TranslatorText = "peter8777555" },
            new() { Code = "en", DisplayName = "English", TranslatorText = "Community Contributors" },
        };
    }

    /// <summary>
    /// 解析 <c>Resources/languages.json</c>（<c>{ "zh": { "name": …, "translator": … } }</c>）。
    /// </summary>
    /// <remarks>
    /// 必须走 <see cref="JsonDefaults.CaseInsensitive"/>（等价于显式开 <see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/>）：
    /// json 的键名是全小写（<c>name</c> / <c>translator</c>），而 <see cref="LanguageMetadata"/> 的属性是 PascalCase；
    /// 默认区分大小写时反序列化**不会**报错，但两个字段全是空串，于是设置窗口语言页那条
    /// 「翻译贡献者: {}」只显示冒号（2026-10-28 修复的真实症状）。
    /// </remarks>
    internal static List<LanguageInfo> ParseLanguageMetadata(string json)
    {
        var metadata = JsonSerializer.Deserialize<Dictionary<string, LanguageMetadata>>(
            json,
            JsonDefaults.CaseInsensitive);

        var result = new List<LanguageInfo>();
        if (metadata is null) return result;

        foreach (var kvp in metadata)
        {
            result.Add(new LanguageInfo
            {
                // languages.json 与 AppSettings.Language 用 "zh"，界面/比较用 "zh-CN"
                Code = kvp.Key == "zh" ? "zh-CN" : kvp.Key,
                DisplayName = kvp.Value.Name,
                TranslatorText = kvp.Value.Translator,
            });
        }

        return result;
    }
}
