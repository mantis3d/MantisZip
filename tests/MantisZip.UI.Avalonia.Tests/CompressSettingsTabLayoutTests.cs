// ============================================================================
// 压缩设置窗「哪个选项住在哪个页签」的回归绊线
//
// 背景（2026-11-17）：用户反馈「输出设置页内容太多太挤」——那页的第 2 行是弹性行
// （内嵌目录选择器），四张 Auto 卡把高度吃满后选择器被压到几十像素。于是把
// 「文件冲突策略」与「完成后打开输出目录」搬进「压缩设置」页，并合成一张卡。
// 2026-11-18 又把「包内根目录」照同样理由搬过去 —— 输出页只留「产物放在哪、叫什么」。
//
// 这类搬迁没有任何编译期保护：XAML 里放错页签照样 0 警告、照样能构建，
// 只有用户点开对话框才发现。更危险的是「复制粘贴后忘了改 x:Name」——
// 两个同名控件会让 FindControl 只找到第一个，事件接线指向错误的那个
// （本次施工中真的发生过一次，构建依旧全绿）。所以这里钉两件事：
//   ① 冲突下拉 / 「打开输出目录」/ 根目录名在「压缩设置」页、不在「输出设置」页；
//   ② 整个文件里这些 x:Name 只出现一次。
// ============================================================================
using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 压缩设置窗页签归属的源码文本级回归：防止选项在页签之间「搬走又搬回」，
/// 或复制粘贴留下重名控件。目的与局限见文件头注释（只校验所在页签，不校验排版观感）。
/// </summary>
public class CompressSettingsTabLayoutTests
{
    private const string XamlRelativePath = "src/MantisZip.UI.Avalonia/Dialogs/CompressSettingsWindow.axaml";

    /// <summary>冲突策略下拉的 x:Name（FindControl 与 code-behind 都靠它）。</summary>
    private const string ConflictComboName = "ConflictActionCombo";

    /// <summary>「压缩完成后打开输出目录」勾选框的 x:Name（无头用例靠它 FindControl）。</summary>
    private const string OpenFolderCheckName = "OpenFolderAfterCheck";

    /// <summary>根目录名输入框的 x:Name（<c>PlaceholderInsertButton.TargetTextBox</c> 靠它定位）。</summary>
    private const string WrapRootBoxName = "WrapRootBox";

    /// <summary>仓库根定位：从测试程序集目录向上找到含 src 目录的一层（同 LocalizedStringsWiringTests）。</summary>
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

    private static string ReadXaml()
    {
        var path = Path.Combine(GetRepoRoot(), XamlRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"压缩设置窗 XAML 不在预期位置：{path}", path);
        }
        return File.ReadAllText(path);
    }

    /// <summary>
    /// 按 <c>&lt;TabItem ...&gt; ... &lt;/TabItem&gt;</c> 切出每个页签的正文，
    /// 以页签 Header 里绑定的本地化 key 作为索引（键值如 <c>Compress_TabOutput</c>）。
    /// </summary>
    private static System.Collections.Generic.Dictionary<string, string> SplitTabsByKey(string xaml)
    {
        var result = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        // TabItem 之间没有嵌套，非贪婪匹配到第一个 </TabItem> 即可。
        var pattern = new Regex(
            "<TabItem[^>]*Header=\"\\{Binding LocalizedStrings\\[(?<key>[^\\]{}]+)\\]\\}\"[\\s\\S]*?</TabItem>",
            RegexOptions.Compiled);
        foreach (Match match in pattern.Matches(xaml))
        {
            var key = match.Groups["key"].Value.Trim();
            if (key.Length > 0)
            {
                result[key] = match.Value;
            }
        }
        return result;
    }

    /// <summary>前置条件：真的切到了页签（防止正则失效后空集合导致「全部通过」的假绿）。</summary>
    [Fact]
    public void Tabs_AreParsable_AndBothRelevantTabsExist()
    {
        var tabs = SplitTabsByKey(ReadXaml());
        Assert.True(tabs.Count >= 6, $"只解析出 {tabs.Count} 个页签，压缩设置窗应有 6 个以上 —— 切分正则可能已失效");
        Assert.True(tabs.ContainsKey("Compress_TabOutput"), "未找到「输出设置」页签");
        Assert.True(tabs.ContainsKey("Compress_TabCompress"), "未找到「压缩设置」页签");
    }

    /// <summary>
    /// 两个选项在「压缩设置」页：冲突策略下拉 + 「压缩完成后打开输出目录」。
    /// 回归目标：2026-11-17 为给目录选择器腾高度而做的搬迁，被无意中搬回。
    /// </summary>
    [Fact]
    public void ConflictAndOpenFolder_LiveInCompressTab()
    {
        var tabs = SplitTabsByKey(ReadXaml());
        var compressTab = tabs["Compress_TabCompress"];

        Assert.True(
            compressTab.Contains(ConflictComboName, StringComparison.Ordinal),
            $"「压缩设置」页缺少 {ConflictComboName}：文件冲突策略应在本页（压缩设置页是 ScrollViewer，加卡不会挤压任何控件）");
        Assert.True(
            compressTab.Contains(OpenFolderCheckName, StringComparison.Ordinal),
            $"「压缩设置」页缺少 {OpenFolderCheckName}：「压缩完成后打开输出目录」应在本页");
        Assert.True(
            compressTab.Contains(WrapRootBoxName, StringComparison.Ordinal),
            $"「压缩设置」页缺少 {WrapRootBoxName}：包内根目录卡应在本页（2026-11-18 从输出设置页搬来）");
    }

    /// <summary>
    /// 「输出设置」页只留「产物放在哪、叫什么」（输出方式 / 目录选择器 / 文件名），
    /// 这两个行为类选项不得再出现——否则目录选择器那条弹性行又会被挤扁。
    /// </summary>
    [Fact]
    public void OutputTab_NoLongerHostsConflictOrOpenFolder()
    {
        var tabs = SplitTabsByKey(ReadXaml());
        var outputTab = tabs["Compress_TabOutput"];

        Assert.False(
            outputTab.Contains(ConflictComboName, StringComparison.Ordinal),
            $"「输出设置」页又出现了 {ConflictComboName}：冲突策略应只在「压缩设置」页，本页要保留高度给目录选择器");
        Assert.False(
            outputTab.Contains(OpenFolderCheckName, StringComparison.Ordinal),
            $"「输出设置」页又出现了 {OpenFolderCheckName}，应从本页移除");
        Assert.False(
            outputTab.Contains(WrapRootBoxName, StringComparison.Ordinal),
            $"「输出设置」页又出现了 {WrapRootBoxName}：包内根目录卡应只在「压缩设置」页");
    }

    /// <summary>
    /// 同名控件在整个 XAML 里只能出现一次。复制粘贴搬迁时最常见的失误，
    /// 且 FindControl 只会返回第一个、不报错——事件接线会指向用户看不见的那个。
    /// （本次施工中真的踩到过一次：搬卡时忘了删原卡，文件里出现两个
    /// `x:Name="ConflictActionCombo"`，构建与测试全绿，只有真机才发现。）
    /// </summary>
    [Fact]
    public void MovedControls_AreUniqueInWholeXaml()
    {
        var xaml = ReadXaml();
        foreach (var name in new[] { ConflictComboName, OpenFolderCheckName, WrapRootBoxName })
        {
            var occurrences = Regex.Matches(xaml, $"x:Name=\"{Regex.Escape(name)}\"").Count;
            Assert.True(
                occurrences == 1,
                $"x:Name=\"{name}\" 出现了 {occurrences} 次（应恰好 1 次）：重名会让 FindControl 只找到第一个，绑定/事件会静默指向错误的控件");
        }
    }

    /// <summary>
    /// 「输出设置」页的 Grid 行数（输出方式 / 弹性目录行 / 文件名卡）。
    /// 行数与 <c>Grid.Row</c> 最大值必须对得上，否则多出来的那张卡会跟别人叠在同一行。
    /// <para>
    /// 2026-11-18 把 <c>expectedRowCount</c> 从 4 改回 3：合并 beta 后本是 4 行（多了「包内根目录」），
    /// 同日又把那张卡搬去「压缩设置」页，输出页回到「产物放在哪、叫什么」三张卡。
    /// </para>
    /// </summary>
    [Fact]
    public void OutputTab_GridRowsMatchHighestRowIndex()
    {
        var outputTab = SplitTabsByKey(ReadXaml())["Compress_TabOutput"];

        // 与 XAML 里 <Grid.RowDefinitions> 的注释保持一一对应：
        // 0 输出方式 / 1 弹性目录行 / 2 文件名卡。
        const int expectedRowCount = 3;
        var rowDefs = Regex.Matches(outputTab, "<RowDefinition\\s+Height=").Count;
        Assert.True(
            rowDefs == expectedRowCount,
            $"「输出设置」页应有 {expectedRowCount} 条 RowDefinition（实际 {rowDefs} 条）：输出方式 / 弹性目录行 / 文件名卡");

        var rowIndexes = Regex.Matches(outputTab, "Grid\\.Row=\"([0-9]+)\"")
            .Select(m => int.Parse(m.Groups[1].Value))
            .DefaultIfEmpty(-1)
            .Max();
        Assert.True(
            rowIndexes < rowDefs,
            $"「输出设置」页出现 Grid.Row=\"{rowIndexes}\"，但只定义了 {rowDefs} 行 —— 该控件会叠到别的行上");
    }
}
