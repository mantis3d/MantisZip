// ============================================================================
// 「解压默认落点 = 压缩包所在目录」接线守卫（KI-022 收口）—— 源码文本级回归绊线
//
// 背景：2026-11-13 把「智能解压」两档与解压设置对话框的默认落点统一成
// 「压缩包所在目录」（包名那一层交给 RootMode，默认 SmartWrap）；但两个入口
// 仍在拼「父目录\包名」，构成 KI-022 —— 那层由根策略补，再拼一次就多一层。
//
// 2026-11-24 同批修掉：
//   · ViewModels/MainWindowViewModel.Extract.cs 的 ExtractSelectedTo（右键「解压选中项到…」）
//   · Services/DragDropService.cs 的拖拽兜底弹窗
//
// 为什么不写行为测试：前者要真实打开压缩包并驱动 ShowExtractEntryPicker 委托，
// 后者是 internal 且内部 new ExtractSettingsWindow（会弹模态窗）—— 无头环境跑不了。
// 故沿用 ExtractPreserveFullPathWiringTests 的源码文本守卫方法论（同样的诚实局限：
// 它只证明「旧的拼接式落点没有回来」，不证明「新值真的流到了解压调用」）。
// ============================================================================
using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 解压默认落点的架构守卫（KI-022）：
/// ① 两个入口不得再拼「父目录 + 包名」；② 必须用父目录；③ 注释要写清为什么。
/// </summary>
public class ExtractDefaultDestWiringTests
{
    private const string ExtractViewModelRelPath =
        "src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Extract.cs";

    private const string DragDropServiceRelPath =
        "src/MantisZip.UI.Avalonia/Services/DragDropService.cs";

    /// <summary>从测试程序集输出目录向上找第一个含 src 的目录作为仓库根（同其他源码守卫）。</summary>
    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException(
            "无法定位仓库根（祖先目录链中未找到 'src' 目录）。" +
            $"Base directory: {AppContext.BaseDirectory}");
    }

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(GetRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>空白归一：换行/缩进重排不应让同一逻辑文本产生不同字节形态。</summary>
    private static string NormalizeWhitespace(string text) => Regex.Replace(text, @"\s+", " ");

    [Fact]
    public void RepoRoot_IsLocatable()
    {
        var root = GetRepoRoot();
        Assert.True(Directory.Exists(Path.Combine(root, "src")), $"仓库根 {root} 下没有 src 目录");
    }

    /// <summary>
    /// 两个入口都不得再用 <c>Path.Combine(parentDir, Path.GetFileNameWithoutExtension(...))</c>
    /// 拼默认落点 —— 那正是 KI-022 的病灶：包名那一层已由 RootMode（默认 SmartWrap）负责。
    /// </summary>
    [Theory]
    [InlineData(ExtractViewModelRelPath)]
    [InlineData(DragDropServiceRelPath)]
    public void NoEntry_BuildsDefaultDestFromArchiveName(string relativePath)
    {
        var text = NormalizeWhitespace(ReadSource(relativePath));

        // 病灶原文（两侧只有变量名不同）：Path.Combine(parentDir, Path.GetFileNameWithoutExtension(x))
        var bad = Regex.Match(
            text,
            @"Path\.Combine\(\s*parentDir\s*,\s*Path\.GetFileNameWithoutExtension",
            RegexOptions.IgnoreCase);
        Assert.False(
            bad.Success,
            $"{relativePath} 又在拼「父目录\\包名」当解压默认落点：包名那一层已由 RootMode 补，" +
            "再拼一次就会解出两层（KI-022）");

        Assert.DoesNotContain("var defaultDest = Path.Combine", text, StringComparison.Ordinal);
        Assert.DoesNotContain("var initialPath = Path.Combine", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 落点必须直接是父目录。断言的是「赋值右侧只有 parentDir」这个形态，
    /// 不锚定行号 —— 这样重排/加注释不会误报。
    /// </summary>
    [Fact]
    public void ExtractSelectedTo_UsesParentDirDirectly()
    {
        var text = NormalizeWhitespace(ReadSource(ExtractViewModelRelPath));

        Assert.Matches(@"var defaultDest = parentDir;", text);
        Assert.Contains("KI-022", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DragDropFallback_UsesParentDirDirectly()
    {
        var text = NormalizeWhitespace(ReadSource(DragDropServiceRelPath));

        Assert.Matches(@"var initialPath = parentDir;", text);
        Assert.Contains("KI-022", text, StringComparison.Ordinal);
    }
}
