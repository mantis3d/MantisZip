// ============================================================================
// 解压对话框线程数（ParallelExtractDegree）接线守卫测试 —— 源码文本级回归绊线
//
// 目的：缺陷修复后（解压设置对话框线程数选择被 ViewModel 忽略——确认回调不回传
// ParallelExtractDegree，ExtractFlow.ExtractAsync 只读 AppSettings，UI 选择恒
// 无效），锁定「对话框选择 → 实际解压选项」的完整接线链路。
//
// 为什么不写行为测试：ExtractFlow.ExtractAsync 是 static 且内部构造 ProgressWindow，
// 对话框回调在 MainWindow.axaml.cs 闭包内，均无法脱离 UI 运行时 mock；且刻意
// 不引入 Microsoft.CodeAnalysis.CSharp 新测试依赖（对齐 ExtractPreserveFullPathWiringTests）。
// 故采用源码文本守卫（architecture guard）。
//
// 诚实的局限：守卫只证明「接线文本在位」，不证明「值真的流到了引擎」——后者由
// 波 B 行为级测试（ResolveDisplayParallelDegree 重载）与人工 GUI 验证覆盖。
//
// 方法论：所有子串断言前先做空白归一（\s+ → 单空格），避免换行/缩进重排造成
// 假通过。与 ExtractPreserveFullPathWiringTests 同源。
// ============================================================================
using System.Text.RegularExpressions;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 解压对话框线程数接线的架构守卫测试（源码守卫 5 个 + 行为测试 2 个）。
/// 目的与诚实局限见文件头注释；每个测试的 XML 注释说明其锁定的具体回归。
/// </summary>
public class ExtractParallelDegreeWiringTests
{
    // ------------------------------------------------------------------
    // 被测生产源码（相对仓库根，'/' 分隔，读取时替换为平台分隔符）
    // ------------------------------------------------------------------

    /// <summary>对话框确认回调回传块所在文件。</summary>
    private const string MainWindowRelPath =
        "src/MantisZip.UI.Avalonia/Views/MainWindow.axaml.cs";

    /// <summary>解压公共流程 ExtractAsync 所在文件。</summary>
    private const string ExtractFlowRelPath =
        "src/MantisZip.UI.Avalonia/Services/ExtractFlow.cs";

    /// <summary>主窗口解压入口 ExtractArchive 所在文件。</summary>
    private const string MainViewModelRelPath =
        "src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs";

    /// <summary>解压设置对话框（线程数 NumericUpDown 绑定）所在文件。</summary>
    private const string ExtractSettingsWindowRelPath =
        "src/MantisZip.UI.Avalonia/Dialogs/ExtractSettingsWindow.axaml";

    // ==================================================================
    // 共享辅助：仓库根定位 / 源码读取 / 空白归一
    // ==================================================================

    /// <summary>
    /// 从测试程序集输出目录向上遍历，找到第一个包含 src 子目录的目录作为仓库根。
    /// 模式复制自 ExtractPreserveFullPathWiringTests.GetRepoRoot()。
    /// </summary>
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

    /// <summary>把相对路径拼到仓库根下（'/' → 平台分隔符）。</summary>
    private static string SourcePath(string relativePath) =>
        Path.Combine(GetRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>读取被测生产源码文件全文（路径基于仓库根解析，共享给所有测试）。</summary>
    private static string ReadSource(string relativePath) =>
        File.ReadAllText(SourcePath(relativePath));

    /// <summary>
    /// 空白归一：把任意长度的空白串（含换行/制表/缩进）折叠为单个空格。
    /// 必须在所有子串断言之前调用，否则重排/折行会让同一逻辑文本产生不同的字节形态。
    /// </summary>
    private static string NormalizeWhitespace(string text) =>
        Regex.Replace(text, @"\s+", " ");

    // ==================================================================
    // 5 个守卫测试
    // ==================================================================

    /// <summary>
    /// 测试 1（fail-fast 前置守卫）：仓库根可定位，且四个被测源码文件都存在于磁盘上。
    ///
    /// 锁定的回归：仓库根发现逻辑或被测文件路径一旦失效，本测试必须最先以
    /// 「哪个路径缺失」的清晰消息失败，而不是让后续守卫报出令人困惑的
    /// 「子串未匹配」错误。
    /// </summary>
    [Fact]
    public void RepoRoot_IsLocatable()
    {
        var root = GetRepoRoot(); // 失败时抛出带 BaseDirectory 的清晰消息

        var expected = new[]
        {
            MainWindowRelPath,
            ExtractFlowRelPath,
            MainViewModelRelPath,
            ExtractSettingsWindowRelPath,
        };
        foreach (var rel in expected)
        {
            var path = SourcePath(rel);
            Assert.True(File.Exists(path),
                $"仓库根定位成功但源码文件不存在：{path}。" +
                "若文件被移动/重命名，请同步更新本测试的路径常量。");
        }
    }

    /// <summary>
    /// 测试 2：MainWindow.axaml.cs 的 ShowExtractSettingsDialog 回调在 result=true
    /// 回传块中必须包含 evm.ParallelExtractDegree = dialog.ViewModel.ParallelExtractDegree。
    ///
    /// 锁定的回归：缺陷的原始形态正是回传块只拷贝 DestinationPath/ConflictAction/
    /// OpenFolderAfterExtract/FilteredEntryKeys 四项，唯独漏掉线程数——对话框里
    /// 用户选择的线程数从未进入 evm，后续全部环节拿到的都是默认值。
    /// </summary>
    [Fact]
    public void MainWindow_DialogCallback_ReturnsParallelExtractDegree()
    {
        var source = ReadSource(MainWindowRelPath);
        var normalized = NormalizeWhitespace(source);

        Assert.Contains(
            "evm.ParallelExtractDegree = dialog.ViewModel.ParallelExtractDegree",
            normalized);
    }

    /// <summary>
    /// 测试 3：ExtractFlow.ExtractAsync 签名必须带「int? parallelExtractDegree = null」
    /// 可空参数（对话框值的穿透通道），且方法体必须以
    /// 「parallelExtractDegree ?? AppSettings.Load()?.ParallelExtractDegree ?? 0」
    /// 这一形态写入 options.ParallelExtractDegree（参数优先、设置兜底）。
    ///
    /// 锁定的回归：缺陷的第二环是 ExtractAsync 只读 AppSettings（对话框选择被
    /// 丢弃）。若有人删掉参数或改回直读设置，守卫失败。
    ///
    /// 为什么空白归一：签名可能被折行（本文件内 ExtractAsync 的 8 个参数就分 9 行
    /// 书写），不归一会造成假失败。
    /// </summary>
    [Fact]
    public void ExtractFlow_ExtractAsync_TakesAndUsesNullableParallelDegree()
    {
        var source = ReadSource(ExtractFlowRelPath);
        var normalized = NormalizeWhitespace(source);

        // 签名通道
        Assert.Contains("int? parallelExtractDegree = null", normalized);

        // 方法体：参数优先、AppSettings 兜底的唯一合法写入形态
        Assert.Contains(
            "parallelExtractDegree ?? AppSettings.Load()?.ParallelExtractDegree ?? 0",
            normalized);

        // 显示回写：进度窗口并行度统计必须用同一参数（而非重读设置）
        Assert.Contains(
            "ResolveDisplayParallelDegree(archivePath, parallelExtractDegree)",
            normalized);
    }

    /// <summary>
    /// 测试 4：MainWindowViewModel.ExtractArchive 的 ExtractFlow.ExtractAsync 调用点
    /// 实参表必须包含 vm.ParallelExtractDegree（对话框值真正传入公共流程）。
    ///
    /// 锁定的回归：缺陷的第三环是调用点不传线程数（参数缺省为 null → 全部回落
    /// AppSettings，对话框选择恒无效）。回传块修了但调用点没修，缺陷仍然存在。
    ///
    /// 为什么限定实参表而非全文：vm 是常见局部变量名，全文匹配可能命中无关文本；
    /// 提取 ExtractFlow.ExtractAsync( 的圆括号配对实参表后断言，与实参顺序无关。
    /// </summary>
    [Fact]
    public void MainWindowViewModel_ExtractArchive_PassesParallelDegree()
    {
        var source = ReadSource(MainViewModelRelPath);

        // 定位 ExtractFlow.ExtractAsync( 调用点并提取实参表
        var callIndex = source.IndexOf("ExtractFlow.ExtractAsync(", StringComparison.Ordinal);
        Assert.True(callIndex >= 0,
            "未在 MainWindowViewModel.cs 中找到 ExtractFlow.ExtractAsync( 调用点。" +
            "被测源码可能已重构，守卫测试需同步更新。");
        var openParen = source.IndexOf('(', callIndex);
        var closeParen = FindMatchingParen(source, openParen);
        var argumentList = NormalizeWhitespace(
            source.Substring(openParen + 1, closeParen - openParen - 1));

        Assert.Contains("vm.ParallelExtractDegree", argumentList);
    }

    /// <summary>
    /// 测试 5：ExtractSettingsWindow.axaml 的线程数 NumericUpDown 必须用
    /// Mode=TwoWay 绑定 ParallelExtractDegree（对话框值写回 ViewModel）。
    ///
    /// 锁定的回归：若绑定是 OneWay（Avalonia NumericUpDown 默认值未经显式声明，
    /// 升级或重构可能改变默认），用户在 UI 上的调整不会写回 dialog.ViewModel，
    /// 测试 2 的回传块将回传过期的默认值——缺陷以新形态回归。
    /// </summary>
    [Fact]
    public void ExtractSettingsWindow_NumericUpDown_BindsTwoWay()
    {
        var source = ReadSource(ExtractSettingsWindowRelPath);
        var normalized = NormalizeWhitespace(source);

        Assert.Contains(
            "Value=\"{Binding ParallelExtractDegree, Mode=TwoWay}\"",
            normalized);
    }

    /// <summary>
    /// 行为测试（波 B）：ResolveDisplayParallelDegree 的二参重载语义——
    /// 显式传入的线程数覆盖 AppSettings（对话框值的最终消费点）；
    /// 引擎不支持并行时无论传什么都返回 1（串行隐藏，Rule 6）。
    ///
    /// 锁定的回归：若实现只在签名加参数但方法体仍读 AppSettings，
    /// 对话框选择依旧无效（缺陷以新形态回归）。
    /// </summary>
    [Fact]
    public void ResolveDisplayParallelDegree_ExplicitParameter_OverridesSettings()
    {
        // Zip 支持并行：显式 3 → 显示 3（不读 AppSettings）
        Assert.Equal(3, ExtractFlow.ResolveDisplayParallelDegree("a.zip", 3));

        // TAR 不支持并行：显式 8 也被压制为 1（HasParallelDegree=false 隐藏统计）
        Assert.Equal(1, ExtractFlow.ResolveDisplayParallelDegree("a.tar", 8));
    }

    /// <summary>
    /// 圆括号配对：openIndex 处必须是 '('，返回配对 ')' 的下标。
    /// 简化版（相对 ExtractPreserveFullPathWiringTests 的 FindMatchingDelimiter）：
    /// 跳过字符串/注释字面量，但不处理字符字面量/逐字字符串（本调用点不涉及）。
    /// 失配时抛 InvalidOperationException（响亮失败，不静默返回错误范围）。
    /// </summary>
    private static int FindMatchingParen(string s, int openIndex)
    {
        if (openIndex < 0 || openIndex >= s.Length || s[openIndex] != '(')
            throw new InvalidOperationException(
                $"FindMatchingParen：下标 {openIndex} 处不是 '('，内部提取逻辑失效。");

        var depth = 0;
        var i = openIndex;
        while (i < s.Length)
        {
            var c = s[i];

            // 跳过行注释
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }

            // 跳过块注释
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
                i = Math.Min(i + 2, s.Length);
                continue;
            }

            // 跳过字符串字面量（反斜杠转义；插值洞内引号的已知简化同源测试）
            if (c == '"')
            {
                i++;
                while (i < s.Length)
                {
                    if (s[i] == '\\') { i += 2; continue; }
                    if (s[i] == '"') { i++; break; }
                    i++;
                }
                continue;
            }

            if (c == '(') depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0) return i;
            }
            i++;
        }
        throw new InvalidOperationException(
            $"未找到与下标 {openIndex} 处的 '(' 配对的 ')'——源码可能被截断。");
    }
}
