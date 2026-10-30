using Avalonia.Headless.XUnit;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 设置窗口「语言」页回归测试。
///
/// 背景（2026-10-28 修复）：<c>Resources/languages.json</c> 的键名是全小写（<c>name</c> / <c>translator</c>），
/// 而反序列化目标类的属性是 PascalCase，`System.Text.Json` 默认**区分大小写**——于是解析「成功」
/// 但两个字段全是空串，语言页那条「翻译贡献者: {}」只剩下冒号。这类静默空值必须由测试盯住。
/// </summary>
public class LanguageMetadataTests
{
    private const string SampleJson = """
        {
          "zh": { "name": "简体中文", "translator": "甲" },
          "zh-TW": { "name": "繁體中文", "translator": "乙" },
          "en": { "name": "English", "translator": "丙" }
        }
        """;

    [AvaloniaFact]
    public void ParseLanguageMetadata_LowercaseKeys_BindNameAndTranslator()
    {
        var parsed = LocalizationManager.ParseLanguageMetadata(SampleJson);

        Assert.Equal(3, parsed.Count);
        Assert.All(parsed, info =>
        {
            Assert.False(string.IsNullOrWhiteSpace(info.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(info.TranslatorText));
        });
    }

    [AvaloniaFact]
    public void ParseLanguageMetadata_MapsZhKeyToZhCn()
    {
        var parsed = LocalizationManager.ParseLanguageMetadata(SampleJson);

        Assert.Equal(["zh-CN", "zh-TW", "en"], parsed.Select(i => i.Code).ToArray());
    }

    [AvaloniaFact]
    public void RepoLanguagesJson_EveryLanguageHasTranslator_AndChineseIsSimplified()
    {
        var parsed = LocalizationManager.ParseLanguageMetadata(ReadRepoFile("src/MantisZip.UI.Avalonia/Resources/languages.json"));

        Assert.Equal(3, parsed.Count);
        Assert.All(parsed, info => Assert.False(string.IsNullOrWhiteSpace(info.TranslatorText)));
        Assert.Equal("简体中文", parsed.Single(i => i.Code == "zh-CN").DisplayName);
    }

    [AvaloniaFact]
    public void AvailableLanguages_LoadTranslatorFromJson_NotEmpty()
    {
        // 输出目录里带 Resources/languages.json（csproj 声明 None CopyToOutputDirectory），
        // 因此这里读到的必须是 json 里的真值——空串就说明大小写绑定又坏了。
        Assert.NotEmpty(LocalizationManager.AvailableLanguages);
        Assert.All(LocalizationManager.AvailableLanguages, info =>
        {
            Assert.False(string.IsNullOrWhiteSpace(info.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(info.TranslatorText));
        });
    }

    [AvaloniaFact]
    public void LanguageOptions_ComeFromLanguagesJson_WithSettingsCodes()
    {
        var vm = new SettingsWindowViewModel();

        var options = vm.SelectedLanguageOptions.ToArray();

        Assert.Equal(["zh", "zh-TW", "en"], options.Select(o => o.Value).ToArray());
        Assert.Equal(LocalizationManager.AvailableLanguages.Select(i => i.DisplayName).ToArray(),
                     options.Select(o => o.Display).ToArray());
        Assert.Equal("简体中文", options[0].Display);
    }

    [AvaloniaFact]
    public void LanguageTranslatorText_FollowsSelectedOption()
    {
        var vm = new SettingsWindowViewModel();

        var texts = new List<string>();
        foreach (var option in vm.SelectedLanguageOptions)
        {
            vm.SelectedSelectedLanguageOption = option;
            texts.Add(vm.LanguageTranslatorText);
        }

        // ① 每一档都有东西可显示（不能只剩「翻译贡献者:」）；
        // ② 三档文案互不相同——说明跟随的是「所选语言」而不是恒定的当前语言。
        Assert.All(texts, text =>
        {
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("Settings_Language_Translator", text);
            Assert.False(text.EndsWith(": ", StringComparison.Ordinal) || text.EndsWith("：", StringComparison.Ordinal),
                         $"翻译贡献者一行为空: [{text}]");
        });
        Assert.Equal(texts.Count, texts.Distinct().Count());
    }

    /// <summary>从测试输出目录向上找到仓库根，再读相对路径（与 ProgressWindowPinTests 同款）。</summary>
    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"找不到仓库文件: {relativePath}");
    }
}
