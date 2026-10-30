// ============================================================================
// 本地化绑定接线守卫测试 —— 源码文本级回归绊线
//
// 背景：XAML 里的 {Binding LocalizedStrings[Key]} 不会因为「没人登记这个 key」
// 而报错，只会在运行时静默显示空白。2026-10-30 压缩设置对话框新增
// 「压缩完成后打开输出目录」勾选时正是这样：CompressSettingsWindow.axaml 绑了
// Compress_OpenFolderAfter，但 CompressSettingsViewModel 的注册块漏了它，构建
// 0 警告、测试全绿，界面却只有一个空勾选框。
//
// 规则（两条，均对 src 下所有 .axaml 生效）：
//   ① 每个 LocalizedStrings[Key] 引用的 key，必须在 src 下某个 .cs 里以 "Key"
//      字面量出现过 —— 即「有人把它接进某个 ViewModel 的字典」；
//   ② 该 key 必须同时存在于三份 strings.*.json 里 —— 否则 T() 拿不到文案。
//
// 诚实的局限：①只证明「某处登记过」，不证明「登记的正是这个 XAML 的 DataContext
// ——那不是静态文本能判定的；②不校验文案是否为空串（空串由 -Task i18n 负责）。
// 这两点比「什么都不查」好得多，且不引入新依赖。
// ============================================================================
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 本地化绑定（LocalizedStrings[...]）接线守卫：XAML 引用的 key 必须被某个
/// ViewModel 登记、且三语都有文案。目的与局限见文件头注释。
/// </summary>
public class LocalizedStringsWiringTests
{
    private const string AppRootRelative = "src/MantisZip.UI.Avalonia";

    private static readonly string[] LanguageFiles =
    [
        "Localization/strings.zh-CN.json",
        "Localization/strings.en.json",
        "Localization/strings.zh-TW.json",
    ];

    /// <summary>XAML 中 LocalizedStrings[Key] 的引用模式（Key 不含 XAML 绑定表达式）。</summary>
    private static readonly Regex RefPattern = new(@"LocalizedStrings\[([^\]{}]+)\]", RegexOptions.Compiled);

    /// <summary>仓库根定位：从测试程序集目录向上找到含 src 目录的一层（同 ExtractParallelDegreeWiringTests）。</summary>
    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        if (dir == null)
        {
            throw new DirectoryNotFoundException(
                $"无法从测试输出目录 {AppContext.BaseDirectory} 向上找到含 src 的仓库根目录");
        }
        return dir;
    }

    private static string Resolve(params string[] parts)
    {
        var segments = new List<string> { GetRepoRoot() };
        segments.AddRange(parts.Select(p => p.Replace('/', Path.DirectorySeparatorChar)));
        return Path.Combine(segments.ToArray());
    }

    /// <summary>收集 src 下所有 XAML 中引用的本地化 key（去重，保留来源文件名便于报错）。</summary>
    private static SortedDictionary<string, List<string>> CollectXamlReferences()
    {
        var root = Resolve(AppRootRelative);
        var result = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match match in RefPattern.Matches(text))
            {
                var key = match.Groups[1].Value.Trim();
                if (key.Length == 0 || key.Contains('{'))
                {
                    continue;
                }
                if (!result.TryGetValue(key, out var owners))
                {
                    owners = [];
                    result[key] = owners;
                }
                var name = Path.GetFileName(file);
                if (!owners.Contains(name))
                {
                    owners.Add(name);
                }
            }
        }
        return result;
    }

    /// <summary>src 下所有 C# 源码的合并文本，用来判断某个 key 是否被登记过。</summary>
    private static string ReadAllCSharpText()
    {
        var root = Resolve(AppRootRelative);
        var builder = new System.Text.StringBuilder();
        foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            builder.AppendLine(File.ReadAllText(file));
        }
        return builder.ToString();
    }

    private static HashSet<string> ReadLanguageKeys(string relativePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Resolve(AppRootRelative, relativePath)));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            keys.Add(property.Name);
        }
        return keys;
    }

    /// <summary>前置条件：能从测试输出目录找到仓库根，且真的扫到了引用（防止「扫空集合 → 假通过」）。</summary>
    [Fact]
    public void RepoRoot_IsLocatable_AndXamlReferencesWereFound()
    {
        var references = CollectXamlReferences();
        Assert.True(
            references.Count > 100,
            $"XAML 本地化引用只有 {references.Count} 条，明显少于预期（应 >100）—— 扫描路径或正则可能已失效");
    }

    /// <summary>
    /// 规则①：XAML 引用的每个 key 都要在 src 的某个 .cs 里以字符串字面量登记。
    /// 回归目标：Compress_OpenFolderAfter（2026-10-30 空勾选框）与更早的
    /// Extract_ParallelNotSavedHint（解压对话框「加速」页签底部提示一直空白）。
    /// </summary>
    [Fact]
    public void EveryXamlLocalizedKey_IsRegisteredInSomeViewModel()
    {
        var references = CollectXamlReferences();
        var csharpText = ReadAllCSharpText();

        var unwired = references
            .Where(pair => !csharpText.Contains('"' + pair.Key + '"', StringComparison.Ordinal))
            .Select(pair => $"{pair.Key}（{string.Join(" / ", pair.Value)}）")
            .ToList();

        Assert.True(
            unwired.Count == 0,
            "以下 XAML 绑定的本地化 key 没有任何 C# 代码登记 → 运行时文字空白：" +
            Environment.NewLine + string.Join(Environment.NewLine, unwired));
    }

    /// <summary>规则②：XAML 引用的每个 key 都必须存在于三份 strings.*.json 中。</summary>
    [Fact]
    public void EveryXamlLocalizedKey_ExistsInAllThreeLanguages()
    {
        var references = CollectXamlReferences();
        var problems = new List<string>();
        foreach (var languageFile in LanguageFiles)
        {
            var keys = ReadLanguageKeys(languageFile);
            foreach (var pair in references.Where(pair => !keys.Contains(pair.Key)))
            {
                problems.Add($"{languageFile} 缺少 {pair.Key}（{string.Join(" / ", pair.Value)}）");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
