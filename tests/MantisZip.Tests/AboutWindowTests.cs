using System.Text.Json;
using Xunit;

namespace MantisZip.Tests;

/// <summary>
/// 关于窗口常量冒烟测试。
/// 验证本地化 JSON 文件中 About_* 相关键的存在性与一致性。
/// 不实例化任何 WPF 控件，仅测试本地化数据层。
/// </summary>
public class AboutWindowTests
{
    private static readonly string ZhJsonPath = Path.Combine(
        GetRepoRoot(), "src", "MantisZip.UI", "Resources", "strings.zh.json");
    private static readonly string EnJsonPath = Path.Combine(
        GetRepoRoot(), "src", "MantisZip.UI", "Resources", "strings.en.json");

    private static string GetRepoRoot()
    {
        // Walk up from test assembly output until we find a directory
        // that contains the `src` folder (repo root marker).
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException(
            "Cannot locate repo root (no 'src' directory found in ancestor chain). " +
            $"Base directory: {AppContext.BaseDirectory}");
    }

    // 从 L.cs 中提取的全部 About_* 键
    private static readonly string[] ExpectedAboutKeys =
    [
        "About_Title",
        "About_Tab_About",
        "About_Tab_Author",
        "About_Tab_Dependencies",
        "About_Tab_Acknowledgments",
        "About_Version",
        "About_Description",
        "About_Formats",
        "About_License",
        "About_GitHub",
        "About_Author_Name",
        "About_Author_Email",
        "About_Author_GitHub",
        "About_Author_Gitee",
        "About_Library_Name",
        "About_Library_Version",
        "About_Library_License",
        "About_Library_Purpose",
        "About_Thanks_OSS",
        "About_Thanks_AI",
        "About_Thanks_7Zip"
    ];

    private static Dictionary<string, string> LoadJson(string path)
    {
        Assert.True(File.Exists(path), $"JSON 文件不存在: {Path.GetFullPath(path)}");
        var json = File.ReadAllText(path);
        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        Assert.NotNull(result);
        return result;
    }

    // ──────────────────────────────────────────────
    // 1. 文件存在性
    // ──────────────────────────────────────────────

    [Fact]
    public void ZhJsonFile_Exists()
    {
        Assert.True(File.Exists(ZhJsonPath));
    }

    [Fact]
    public void EnJsonFile_Exists()
    {
        Assert.True(File.Exists(EnJsonPath));
    }

    // ──────────────────────────────────────────────
    // 2. About_* 键的存在性（中文）
    // ──────────────────────────────────────────────

    [Fact]
    public void ZhJson_HasAllExpectedAboutKeys()
    {
        var data = LoadJson(ZhJsonPath);
        foreach (var key in ExpectedAboutKeys)
        {
            Assert.True(data.ContainsKey(key), $"中文 JSON 缺少键: {key}");
        }
    }

    [Fact]
    public void ZhJson_AboutKeys_AllHaveNonEmptyValues()
    {
        var data = LoadJson(ZhJsonPath);
        foreach (var key in ExpectedAboutKeys)
        {
            Assert.False(string.IsNullOrWhiteSpace(data[key]), $"中文 JSON 键 {key} 的值为空");
        }
    }

    // ──────────────────────────────────────────────
    // 3. About_* 键的存在性（英文）
    // ──────────────────────────────────────────────

    [Fact]
    public void EnJson_HasAllExpectedAboutKeys()
    {
        var data = LoadJson(EnJsonPath);
        foreach (var key in ExpectedAboutKeys)
        {
            Assert.True(data.ContainsKey(key), $"英文 JSON 缺少键: {key}");
        }
    }

    [Fact]
    public void EnJson_AboutKeys_AllHaveNonEmptyValues()
    {
        var data = LoadJson(EnJsonPath);
        foreach (var key in ExpectedAboutKeys)
        {
            Assert.False(string.IsNullOrWhiteSpace(data[key]), $"英文 JSON 键 {key} 的值为空");
        }
    }

    // ──────────────────────────────────────────────
    // 4. About_* 键数一致性（中英文相互比照）
    // ──────────────────────────────────────────────

    [Fact]
    public void BothLanguages_HaveSameAboutKeySet()
    {
        var zh = LoadJson(ZhJsonPath);
        var en = LoadJson(EnJsonPath);

        var zhAboutKeys = zh.Keys.Where(k => k.StartsWith("About_")).OrderBy(k => k).ToArray();
        var enAboutKeys = en.Keys.Where(k => k.StartsWith("About_")).OrderBy(k => k).ToArray();

        Assert.Equal(zhAboutKeys, enAboutKeys);
    }

    [Fact]
    public void AboutKeyCount_MeetsMinimum()
    {
        var zh = LoadJson(ZhJsonPath);
        int count = zh.Keys.Count(k => k.StartsWith("About_"));
        Assert.True(count >= 21, $"About_* 键的数量 ({count}) 小于 21");
    }

    // ──────────────────────────────────────────────
    // 5. 向后兼容：Main_About_Text 和 Main_About_Title
    // ──────────────────────────────────────────────

    [Fact]
    public void Main_About_Text_ExistsInZh()
    {
        var data = LoadJson(ZhJsonPath);
        Assert.True(data.ContainsKey("Main_About_Text"));
        Assert.False(string.IsNullOrWhiteSpace(data["Main_About_Text"]));
    }

    [Fact]
    public void Main_About_Text_ExistsInEn()
    {
        var data = LoadJson(EnJsonPath);
        Assert.True(data.ContainsKey("Main_About_Text"));
        Assert.False(string.IsNullOrWhiteSpace(data["Main_About_Text"]));
    }

    [Fact]
    public void Main_About_Title_ExistsInZh()
    {
        var data = LoadJson(ZhJsonPath);
        Assert.True(data.ContainsKey("Main_About_Title"));
        Assert.False(string.IsNullOrWhiteSpace(data["Main_About_Title"]));
    }

    [Fact]
    public void Main_About_Title_ExistsInEn()
    {
        var data = LoadJson(EnJsonPath);
        Assert.True(data.ContainsKey("Main_About_Title"));
        Assert.False(string.IsNullOrWhiteSpace(data["Main_About_Title"]));
    }

    // ──────────────────────────────────────────────
    // 6. 本地化字符串一致性：中文非空时英文也应有值
    // ──────────────────────────────────────────────

    [Fact]
    public void AllAboutKeys_WhenZhHasValue_EnAlsoHasValue()
    {
        var zh = LoadJson(ZhJsonPath);
        var en = LoadJson(EnJsonPath);

        foreach (var key in ExpectedAboutKeys)
        {
            bool zhHasValue = zh.TryGetValue(key, out var zhVal) && !string.IsNullOrWhiteSpace(zhVal);
            bool enHasValue = en.TryGetValue(key, out var enVal) && !string.IsNullOrWhiteSpace(enVal);

            // 如果中文有值，英文也必须有值
            if (zhHasValue)
            {
                Assert.True(enHasValue, $"英文 JSON 缺少键或值为空 (About key: {key})");
            }
        }
    }
}
