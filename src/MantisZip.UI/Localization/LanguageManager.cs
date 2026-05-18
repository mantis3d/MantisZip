using System.IO;
using System.Text.Json;

namespace MantisZip.UI.Localization;

/// <summary>
/// 语言管理器。单例。
/// 自动扫描 Resources/strings.*.json 发现可用语言，支持运行时切换。
/// 新增语言只需放入 strings.xx.json 并更新 languages.json 即可。
/// </summary>
public class LanguageManager
{
    private static LanguageManager? _instance;
    public static LanguageManager Instance => _instance ??= new LanguageManager();

    private readonly string _resourcesDir;

    /// <summary>所有已加载的语言数据：lang_code → (key → value)</summary>
    private Dictionary<string, Dictionary<string, string>> _data = new();

    /// <summary>语言代码 → 本地化显示名称（来自 languages.json）</summary>
    private Dictionary<string, string> _languageDisplayNames = new();

    private string _currentLanguage = "zh";

    // ── Public API ──

    /// <summary>当前语言代码（如 "zh"、"en"）</summary>
    public string CurrentLanguage => _currentLanguage;

    /// <summary>可用语言列表：(代码, 显示名称)</summary>
    public IReadOnlyList<(string Code, string DisplayName)> AvailableLanguages =>
        _data.Keys.Select(code => (code, _languageDisplayNames.GetValueOrDefault(code, code))).ToList();

    /// <summary>
    /// 按 key 获取当前语言的翻译文本。
    /// 回退链：当前语言 → "en" → "[KEY]" 标记。
    /// </summary>
    public string this[string key]
    {
        get
        {
            if (_data.TryGetValue(_currentLanguage, out var langStrings)
                && langStrings.TryGetValue(key, out var val)
                && !string.IsNullOrEmpty(val))
                return val;

            if (_currentLanguage != "en"
                && _data.TryGetValue("en", out var enStrings)
                && enStrings.TryGetValue(key, out var enVal)
                && !string.IsNullOrEmpty(enVal))
                return enVal;

            return $"[{key}]";
        }
    }

    /// <summary>语言切换时触发，各窗口订阅后更新 UI。</summary>
    public static event Action? LanguageChanged;

    private LanguageManager()
    {
        _resourcesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
    }

    // ── Initialization ──

    /// <summary>
    /// 扫描 Resources/ 目录，加载所有 strings.*.json 和 languages.json。
    /// 在 App.OnStartup 中调用。
    /// </summary>
    public void Initialize()
    {
        _languageDisplayNames = LoadLanguageDisplayNames();
        _data = ScanAndLoadStringFiles();

        // 从持久化设置恢复上一次使用的语言
        var savedLang = AppSettings.Instance.Language;
        if (!string.IsNullOrEmpty(savedLang) && _data.ContainsKey(savedLang))
            _currentLanguage = savedLang;
        else if (!_data.ContainsKey(_currentLanguage))
            _currentLanguage = _data.Keys.FirstOrDefault() ?? "zh";
    }

    /// <summary>切换到指定语言。触发 LanguageChanged 事件。</summary>
    public void SwitchTo(string lang)
    {
        if (lang == _currentLanguage) return;
        if (!_data.ContainsKey(lang)) return;

        _currentLanguage = lang;
        AppSettings.Instance.Language = lang;
        AppSettings.Instance.Save();

        LanguageChanged?.Invoke();
    }

    // ── Internal Loaders ──

    /// <summary>加载 languages.json（语言代码 → 显示名称）</summary>
    private Dictionary<string, string> LoadLanguageDisplayNames()
    {
        var path = Path.Combine(_resourcesDir, "languages.json");
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
        }
        catch (Exception ex)
        {
            App.LogDebug("LanguageManager.LoadLanguageDisplayNames: failed: {0}", ex.Message);
        }
        return new();
    }

    /// <summary>扫描 Resources/strings.*.json，返回 {code → strings}</summary>
    private Dictionary<string, Dictionary<string, string>> ScanAndLoadStringFiles()
    {
        var result = new Dictionary<string, Dictionary<string, string>>();

        try
        {
            if (!Directory.Exists(_resourcesDir))
            {
                App.LogDebug("LanguageManager: Resources directory not found: {0}", _resourcesDir);
                return result;
            }

            foreach (var file in Directory.EnumerateFiles(_resourcesDir, "strings.*.json"))
            {
                var fileName = Path.GetFileNameWithoutExtension(file); // "strings.zh"
                var code = fileName.Substring("strings.".Length);      // "zh"

                if (string.IsNullOrEmpty(code)) continue;

                try
                {
                    var json = File.ReadAllText(file);
                    var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (strings != null && strings.Count > 0)
                    {
                        // 去除 __ 开头的元字段（如 __display）
                        var clean = strings.Where(kvp => !kvp.Key.StartsWith("__"))
                                           .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                        result[code] = clean;
                        App.LogDebug("LanguageManager: loaded '{0}' ({1} keys)", code, clean.Count);
                    }
                }
                catch (Exception ex)
                {
                    App.LogDebug("LanguageManager: failed to load '{0}': {1}", file, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            App.LogDebug("LanguageManager.ScanAndLoadStringFiles: {0}", ex.Message);
        }

        return result;
    }
}
