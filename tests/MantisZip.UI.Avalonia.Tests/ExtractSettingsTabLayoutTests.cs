// ============================================================================
// 解压设置窗「哪个选项住在哪个页签」的回归绊线（与 CompressSettingsTabLayoutTests 同构）
//
// 背景（2026-11-23）：解压设置窗的「输出设置」页里塞着四张常驻卡 ——
// 目标目录、冲突策略、完成后行为、压缩包顶层目录。其中第二行是弹性行（内嵌目录
// 选择器），常驻卡一多，选择器就被压到几十像素（与压缩端 2026-11-17 是同一个病）。
// 于是把「冲突策略 + 打开目录 + 移入回收站」和「压缩包顶层目录」搬进新的
// 「解压设置」页，输出设置页只留「解压到哪」。
//
// 这类搬迁没有任何编译期保护：XAML 里放错页签照样 0 警告、照样能构建，只有用户
// 点开对话框才发现。更危险的是「复制粘贴后忘了改 x:Name」——两个同名控件会让
// FindControl 只找到第一个，事件接线指向错误的那个（压缩端施工时真的发生过）。
// 所以这里钉三件事：
//   ① 四张行为卡在新「解压设置」页、不在「输出设置」页；
//   ② 整个文件里这些 x:Name 只出现一次；
//   ③ 「输出设置」页的 Grid 行数 = 最高 Grid.Row + 1（只剩目标目录一条弹性行）。
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 解压设置窗页签归属的源码文本级回归：防止行为类选项被无意搬回「输出设置」页，
/// 或复制粘贴留下重名控件。目的与局限见文件头注释（只校验所在页签，不校验排版观感）。
/// </summary>
public class ExtractSettingsTabLayoutTests
{
    private const string XamlRelativePath = "src/MantisZip.UI.Avalonia/Dialogs/ExtractSettingsWindow.axaml";

    /// <summary>冲突策略下拉的 x:Name（与压缩端同名，便于两处对照阅读）。</summary>
    private const string ConflictComboName = "ConflictActionCombo";

    /// <summary>「解压完成后打开目标目录」勾选框的 x:Name。</summary>
    private const string OpenFolderCheckName = "OpenFolderAfterCheck";

    /// <summary>「解压后移入回收站」勾选框的 x:Name。</summary>
    private const string DeleteArchiveCheckName = "DeleteArchiveAfterCheck";

    /// <summary>「压缩包顶层目录」四态下拉的 x:Name。</summary>
    private const string RootModeComboName = "ExtractRootModeCombo";

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
            throw new FileNotFoundException($"解压设置窗 XAML 不在预期位置：{path}", path);
        }
        return File.ReadAllText(path);
    }

    /// <summary>
    /// 按 <c>&lt;TabItem ...&gt; ... &lt;/TabItem&gt;</c> 切出每个页签的正文，
    /// 以页签 Header 里绑定的本地化 key 作为索引（键值如 <c>Extract_TabOutput</c>）。
    /// </summary>
    private static Dictionary<string, string> SplitTabsByKey(string xaml)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
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
        Assert.True(tabs.Count >= 5, $"只解析出 {tabs.Count} 个页签，解压设置窗应有 5 个以上 —— 切分正则可能已失效");
        Assert.True(tabs.ContainsKey("Extract_TabOutput"), "未找到「输出设置」页签");
        Assert.True(tabs.ContainsKey("Extract_TabOptions"), "未找到「解压设置」页签（2026-11-23 新增）");
    }

    /// <summary>
    /// 四张行为卡都在「解压设置」页：冲突策略 / 完成后打开目录 / 移入回收站 / 压缩包顶层目录。
    /// 回归目标：2026-11-23 为给目录选择器腾高度而做的搬迁，被无意中搬回。
    /// </summary>
    [Fact]
    public void BehaviorCards_LiveInOptionsTab()
    {
        var tabs = SplitTabsByKey(ReadXaml());
        var optionsTab = tabs["Extract_TabOptions"];

        Assert.True(
            optionsTab.Contains(ConflictComboName, StringComparison.Ordinal),
            $"「解压设置」页缺少 {ConflictComboName}：文件冲突策略应在本页");
        Assert.True(
            optionsTab.Contains(OpenFolderCheckName, StringComparison.Ordinal),
            $"「解压设置」页缺少 {OpenFolderCheckName}：「解压完成后打开目录」应在本页");
        Assert.True(
            optionsTab.Contains(DeleteArchiveCheckName, StringComparison.Ordinal),
            $"「解压设置」页缺少 {DeleteArchiveCheckName}：「解压后移入回收站」应在本页");
        Assert.True(
            optionsTab.Contains(RootModeComboName, StringComparison.Ordinal),
            $"「解压设置」页缺少 {RootModeComboName}：压缩包顶层目录下拉应在本页");
    }

    /// <summary>
    /// 「输出设置」页只留「解压到哪」（目标目录 + 内嵌选择器），
    /// 四张行为卡不得再出现 —— 否则目录选择器那条弹性行又会被挤扁。
    /// </summary>
    [Fact]
    public void OutputTab_OnlyHostsDestination()
    {
        var tabs = SplitTabsByKey(ReadXaml());
        var outputTab = tabs["Extract_TabOutput"];

        foreach (var name in new[] { ConflictComboName, OpenFolderCheckName, DeleteArchiveCheckName, RootModeComboName })
        {
            Assert.False(
                outputTab.Contains(name, StringComparison.Ordinal),
                $"「输出设置」页又出现了 {name}：该选项应只在「解压设置」页，本页要保留高度给目录选择器");
        }
        Assert.True(
            outputTab.Contains("DestinationPicker", StringComparison.Ordinal),
            "「输出设置」页缺少 DestinationPicker：内嵌目录选择器必须留在这页");
    }

    /// <summary>
    /// 同名控件在整个 XAML 里只能出现一次。复制粘贴搬迁时最常见的失误，
    /// 且 FindControl 只会返回第一个、不报错 —— 事件接线会指向用户看不见的那个。
    /// </summary>
    [Fact]
    public void MovedControls_AreUniqueInWholeXaml()
    {
        var xaml = ReadXaml();
        foreach (var name in new[] { ConflictComboName, OpenFolderCheckName, DeleteArchiveCheckName, RootModeComboName })
        {
            var occurrences = Regex.Matches(xaml, $"x:Name=\"{Regex.Escape(name)}\"").Count;
            Assert.True(
                occurrences == 1,
                $"x:Name=\"{name}\" 出现了 {occurrences} 次（应恰好 1 次）：重名会让 FindControl 只找到第一个，绑定/事件会静默指向错误的控件");
        }
    }

    /// <summary>
    /// 「输出设置」页的 Grid 行数（目标目录标题 / 可编辑输入行 / 弹性选择器行）。
    /// 行数与 <c>Grid.Row</c> 最大值必须对得上，否则多出来的控件会跟别人叠在同一行。
    /// <para>
    /// 2026-11-23 从 4 行收回 3 行：行为卡搬走后本页只剩「解压到哪」。
    /// </para>
    /// <para>
    /// 注意：本窗用的是简写形态 <c>RowDefinitions="Auto,Auto,*"</c>，不是压缩窗那种
    /// <c>&lt;RowDefinition Height="…" /&gt;</c> 元素写法 —— 两种形态都要能数，否则
    /// 换个写法测试就假绿（本次首版只认元素写法，实测数到 0 条才暴露）。
    /// </para>
    /// </summary>
    [Fact]
    public void OutputTab_GridRowsMatchHighestRowIndex()
    {
        var outputTab = SplitTabsByKey(ReadXaml())["Extract_TabOutput"];

        // 与 XAML 里 RowDefinitions 的注释保持一一对应：
        // 0 目标目录标题 / 1 可编辑输入行 / 2 弹性选择器行。
        const int expectedRowCount = 3;
        var rowDefs = CountRowDefinitions(outputTab);
        Assert.True(
            rowDefs == expectedRowCount,
            $"「输出设置」页应有 {expectedRowCount} 行（实际 {rowDefs} 行）：目标目录标题 / 可编辑输入行 / 弹性选择器行");

        var rowIndexes = Regex.Matches(outputTab, "Grid\\.Row=\"([0-9]+)\"")
            .Select(m => int.Parse(m.Groups[1].Value))
            .DefaultIfEmpty(-1)
            .Max();
        Assert.True(
            rowIndexes < rowDefs,
            $"「输出设置」页出现 Grid.Row=\"{rowIndexes}\"，但只定义了 {rowDefs} 行 —— 该控件会叠到别的行上");
    }

    /// <summary>
    /// 数一个页签正文里的 Grid 行定义，两种写法都支持：
    /// ① 元素写法 <c>&lt;RowDefinition Height="…" /&gt;</c>（压缩窗）；
    /// ② 简写写法 <c>RowDefinitions="Auto,Auto,*"</c>（解压窗）。
    /// 只数到页签里**第一个** Grid 的行定义 —— 本函数服务于「根 Grid 有几行」这一个问题。
    /// </summary>
    private static int CountRowDefinitions(string tabXaml)
    {
        var attribute = Regex.Match(tabXaml, "RowDefinitions=\"([^\"]*)\"");
        if (attribute.Success)
        {
            return attribute.Groups[1].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Length;
        }
        return Regex.Matches(tabXaml, "<RowDefinition\\s+Height=").Count;
    }
}
