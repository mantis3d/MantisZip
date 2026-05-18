using System.Windows.Markup;

namespace MantisZip.UI.Localization;

/// <summary>
/// XAML 标记扩展，用于从 LanguageManager 获取翻译文本。
/// 使用方式：Text="{l:L Menu_File}"
///
/// 注意：此扩展在 XAML 加载时一次性求值，不自动追踪语言切换。
/// 语言切换后，各 Window 应调用 ApplyLanguage() 手动刷新 UI 元素。
/// </summary>
public class LExtension : MarkupExtension
{
    /// <summary>翻译文本的 key。</summary>
    public string Key { get; set; }

    public LExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return LanguageManager.Instance[Key];
    }
}
