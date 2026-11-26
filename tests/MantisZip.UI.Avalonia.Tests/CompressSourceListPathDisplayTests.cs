// ============================================================================
// 压缩设置窗「已选源文件」路径显示与复制路径的回归绊线
//
// 背景（2026-11-18）：该列表原来用 TextTrimming="CharacterEllipsis" 显示**完整路径**。
// CharacterEllipsis 从尾部裁剪，而文件名正好在路径尾部 —— 路径一深，用户最先看不见的
// 正是文件名本身。改成 PathSegmentEllipsis 后按「路径段」挖掉中段，尾部文件名保住。
//
// 为什么要有这条绊线：TextTrimming 是纯 XAML 字面量，写错/回退成 CharacterEllipsis
// 照样 0 警告、照样构建通过，只有真机上把路径变长才看得出来。同理「复制路径」菜单
// 一旦被误删，用户就再也拿不到可以粘贴的完整路径（列表里永远是裁剪过的）。
// ============================================================================
using System;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 「已选源文件」列表的显示策略与右键复制入口的源码级回归。
/// 只校验属性写出了正确值，不校验渲染观感（那需要真机目视）。
/// </summary>
public class CompressSourceListPathDisplayTests
{
    private const string XamlRelativePath = "src/MantisZip.UI.Avalonia/Dialogs/CompressSettingsWindow.axaml";
    private const string CodeBehindRelativePath = "src/MantisZip.UI.Avalonia/Dialogs/CompressSettingsWindow.axaml.cs";

    /// <summary>列表控件的 x:Name —— code-behind 的移除/复制都靠它定位。</summary>
    private const string ListName = "SourceFilesList";

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

    private static string ReadRepoFile(string relativePath)
    {
        var path = Path.Combine(GetRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"文件不在预期位置：{path}", path);
        }
        return File.ReadAllText(path);
    }

    /// <summary>
    /// 切出 <c>&lt;ListBox x:Name="SourceFilesList" ...&gt; ... &lt;/ListBox&gt;</c> 这一段。
    /// ListBox 内部没有嵌套 ListBox，非贪婪匹配到第一个结束标签即可。
    /// </summary>
    private static string ExtractSourceFilesList(string xaml)
    {
        var match = Regex.Match(
            xaml,
            $"<ListBox[^>]*x:Name=\"{Regex.Escape(ListName)}\"[\\s\\S]*?</ListBox>",
            RegexOptions.None);
        Assert.True(match.Success, $"XAML 里找不到 x:Name=\"{ListName}\" 的 ListBox");
        return match.Value;
    }

    /// <summary>
    /// 文件名必须活下来：裁剪策略必须是 PathSegmentEllipsis（挖中段、保尾部）。
    /// 回归目标：回退成 CharacterEllipsis —— 那正是本次要修的 bug。
    /// </summary>
    [Fact]
    public void SourceList_UsesPathSegmentTrimming()
    {
        var list = ExtractSourceFilesList(ReadRepoFile(XamlRelativePath));

        Assert.Contains("TextTrimming=\"PathSegmentEllipsis\"", list, StringComparison.Ordinal);
        Assert.DoesNotContain("TextTrimming=\"CharacterEllipsis\"", list, StringComparison.Ordinal);
    }

    /// <summary>
    /// 完整路径必须还有出口：列表里永远是裁剪过的，用户要粘贴原样路径只能靠右键菜单。
    /// </summary>
    [Fact]
    public void SourceList_HasCopyPathMenuItemWiredToHandler()
    {
        var list = ExtractSourceFilesList(ReadRepoFile(XamlRelativePath));

        Assert.Contains("<ListBox.ContextMenu>", list, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CopyPathMenuItem\"", list, StringComparison.Ordinal);
        // 文案复用主窗口既有的「复制路径」，不另起一份三语文案
        Assert.Contains("LocalizedStrings[Ctx_CopyPath]", list, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnSourceFileCopyPathClick\"", list, StringComparison.Ordinal);
        // 没有选区时需要按情况禁用菜单项，靠 ContextRequested 落地
        Assert.Contains("ContextRequested=\"OnSourceFilesListContextRequested\"", list, StringComparison.Ordinal);
    }

    /// <summary>两个事件处理与剪贴板写入必须真的存在于 code-behind（XAML 里写了名字但没实现会运行期炸）。</summary>
    [Fact]
    public void CodeBehind_ImplementsCopyHandlers()
    {
        var code = ReadRepoFile(CodeBehindRelativePath);

        Assert.Contains("private void OnSourceFilesListContextRequested(", code, StringComparison.Ordinal);
        Assert.Contains("private async void OnSourceFileCopyPathClick(", code, StringComparison.Ordinal);
        // Avalonia 12 的剪贴板 API：必须走 DataTransfer，老的 SetTextAsync 已移除
        Assert.Contains("clipboard.SetDataAsync(transfer)", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// 前置条件守卫：XAML 里那个字符串必须真能被 Avalonia 解析成 PathSegmentEllipsis。
    /// 拼错会抛 FormatException（TextTrimming.Parse 对未知名字直接抛），单测先把这条路封住。
    /// </summary>
    [Fact]
    public void AvaloniaTextTrimming_ParsesPathSegmentEllipsis()
    {
        var parsed = TextTrimming.Parse("PathSegmentEllipsis");

        Assert.NotNull(parsed);
        Assert.Equal("PathSegmentEllipsis", parsed.ToString());
        // 同一实例：确认它不是被当成 CharacterEllipsis 之类的别名吃掉了
        Assert.Same(TextTrimming.PathSegmentEllipsis, parsed);
    }
}
