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
/// 解压对话框线程数接线的架构守卫测试（源码守卫 7 个 + 行为测试 1 个）。
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

    /// <summary>选中条目解压服务（ExtractEntriesAsync 接线 resolver）所在文件。</summary>
    private const string SelectedItemsExtractServiceRelPath =
        "src/MantisZip.UI.Avalonia/Services/SelectedItemsExtractService.cs";

    /// <summary>解压设置对话框 ViewModel（并行选项镜像设置、不回写）所在文件。</summary>
    private const string ExtractSettingsViewModelRelPath =
        "src/MantisZip.UI.Avalonia/ViewModels/ExtractSettingsViewModel.cs";

    /// <summary>主窗口解压入口 ExtractArchive 所在文件（partial 主干）。</summary>
    private const string MainViewModelRelPath =
        "src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs";

    /// <summary>
    /// MainWindowViewModel 的全部 partial 源码（2026-10-20 大文件拆分后：解压族在 .Extract.cs）。
    /// ⚠ 新增 partial 时须同步追加，否则源码守卫只扫描一部分。
    /// </summary>
    private static readonly string[] MainViewModelRelPaths =
    {
        MainViewModelRelPath,
        "src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Password.cs",
        "src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Preview.cs",
        "src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Navigation.cs",
        "src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Extract.cs",
        "src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.Compress.cs",
    };

    /// <summary>解压设置对话框（线程数 NumericUpDown 绑定）所在文件。</summary>
    private const string ExtractSettingsWindowRelPath =
        "src/MantisZip.UI.Avalonia/Dialogs/ExtractSettingsWindow.axaml";

    /// <summary>解压会话（确认载荷 → ExtractFlow 的唯一执行入口）所在文件。</summary>
    private const string ExtractSessionRelPath =
        "src/MantisZip.UI.Avalonia/Services/ExtractSession.cs";

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
            SelectedItemsExtractServiceRelPath,
            ExtractSettingsViewModelRelPath,
            MainViewModelRelPath,
            ExtractSettingsWindowRelPath,
            ExtractSessionRelPath,
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
    /// 测试 2：MainWindow.axaml.cs 的 ShowExtractSettingsDialog 回调必须注入执行器并只消费
    /// <c>ConfirmOutcome&lt;ExtractSettings&gt;</c>（2026-10-26 接缝改造：关窗后回读 VM 的
    /// <c>CopyExecutionSettingsFrom</c> / <c>GetFilteredEntryKeys</c> / <c>DialogResult</c> 三轨已退休）。
    ///
    /// 锁定的回归：回传块曾只拷贝 DestinationPath/ConflictAction/OpenFolderAfterExtract/
    /// FilteredEntryKeys 四项，唯独漏掉线程数——对话框里的并行选择从未进入执行链。
    /// 现在并行快照由确认事务一次产出（<c>ParallelOverride = BuildParallelSettings()</c>），
    /// 回调若退回「ShowDialog&lt;bool&gt; + 补读」形态，本守卫失败。
    /// </summary>
    [Fact]
    public void MainWindow_DialogCallback_UsesConfirmPayload()
    {
        var source = ReadSource(MainWindowRelPath);
        var normalized = NormalizeWhitespace(source);

        // ① 执行器 + 宿主上下文注入（会话是唯一执行入口）
        Assert.Contains("var session = new ExtractSession()", normalized);
        Assert.Contains("new ExtractRunContext(", normalized);
        Assert.Contains("new ExtractSettingsWindow(session, context, archivePaths)", normalized);

        // ② 只消费确认载荷：ShowDialog 返回 ConfirmOutcome，确认后由窗口执行
        Assert.Contains("ShowDialog<ConfirmOutcome<ExtractSettings>>", normalized);
        Assert.Contains("await dialog.RunConfirmedAsync(confirmed.Value)", normalized);

        // ③ 旧三轨回读通道不得回流
        Assert.DoesNotContain("CopyExecutionSettingsFrom", normalized);
        Assert.DoesNotContain("GetFilteredEntryKeys", normalized);
        Assert.DoesNotContain("DialogResult", normalized);

        // ④ 并行快照在确认事务里产出（DTO 自带，不靠调用方手抄）
        var vmSource = NormalizeWhitespace(ReadSource(ExtractSettingsViewModelRelPath));
        Assert.Contains("ParallelOverride = BuildParallelSettings()", vmSource);
    }

    /// <summary>
    /// 测试 3：ExtractFlow.ExtractAsync 签名必须带「ParallelExtractSettings? parallelOverride = null」
    /// 可空参数（解压对话框本次设置快照的穿透通道），且方法体必须以
    /// 「parallelOverride ?? new ParallelExtractSettings { ... }」形态取设置快照（快照优先、否则读
    /// AppSettings），再经 <c>ParallelDegreeResolver.ResolveAsync</c> 解析并把结果写入
    /// options.ParallelExtractDegree（对话框快照接线后的唯一合法形态）。
    ///
    /// 锁定的回归：接线改造把「单个数字参数」升级为「整份设置快照」；若有人删掉签名参数、
    /// 绕过 resolver 直读设置，或漏写 options 赋值，守卫失败。
    ///
    /// 为什么空白归一：签名可能被折行，不归一会造成假失败。
    /// </summary>
    [Fact]
    public void ExtractFlow_ExtractAsync_TakesAndUsesNullableParallelOverride()
    {
        var source = ReadSource(ExtractFlowRelPath);
        var normalized = NormalizeWhitespace(source);

        // 签名通道（对话框快照穿透）
        Assert.Contains("ParallelExtractSettings? parallelOverride = null", normalized);

        // 方法体：快照优先，否则读 AppSettings
        Assert.Contains("parallelOverride ?? new ParallelExtractSettings", normalized);

        // 经 resolver 决策链解析（callerDegree 不再使用——手动线程数走快照的 ManualDegree）
        Assert.Contains("ParallelDegreeResolver.ResolveAsync", normalized);
        Assert.Contains("callerDegree: null", normalized);

        // 解析结果写入解压选项（引擎 SupportsParallelExtract 时生效）
        Assert.Contains("ParallelExtractDegree = resolvedDegree", normalized);

        // 显示回写：进度窗口并行度统计必须用 resolver 结果（而非重读设置）
        Assert.Contains(
            "ResolveDisplayParallelDegree(archivePath, resolvedDegree)",
            normalized);
    }

    /// <summary>
    /// 测试 4：对话框并行快照真正传入公共流程的唯一通道是
    /// <c>ExtractSession</c>（确认载荷 → <c>settings.ParallelOverride</c> → <c>ExtractFlow.ExtractAsync(...)</c>）。
    ///
    /// 锁定的回归：调用点漏传快照 → 参数缺省 null → 全部回落 AppSettings，
    /// 对话框里的并行选择恒无效。2026-10-26 接缝改造后执行下沉到会话，
    /// MainWindowViewModel 不再自己调 <c>ExtractFlow.ExtractAsync</c>——若它又长出直接调用，
    /// 等于绕过确认载荷（第二条执行链），本守卫同时把这条死路堵上。
    /// </summary>
    [Fact]
    public void ExtractSeam_CarriesParallelOverrideIntoSession()
    {
        var sessionSource = NormalizeWhitespace(ReadSource(ExtractSessionRelPath));

        // 载荷快照来自确认事务（settings 是 RunAsync 的不可变形参，不读窗口状态）
        Assert.Contains("var parallelOverride = settings.ParallelOverride", sessionSource);

        // 定位 ExtractFlow.ExtractAsync( 调用点并提取实参表（与实参顺序无关）
        var callIndex = sessionSource.IndexOf("ExtractFlow.ExtractAsync(", StringComparison.Ordinal);
        Assert.True(callIndex >= 0,
            "未在 ExtractSession.cs 中找到 ExtractFlow.ExtractAsync( 调用点。" +
            "被测源码可能已重构，守卫测试需同步更新。");
        var openParen = sessionSource.IndexOf('(', callIndex);
        var closeParen = FindMatchingParen(sessionSource, openParen);
        var argumentList = sessionSource.Substring(openParen + 1, closeParen - openParen - 1);
        Assert.Contains("parallelOverride", argumentList);

        // MainWindowViewModel 不得自己执行整包解压（唯一执行链是设置窗口 → 会话）
        var vmSource = NormalizeWhitespace(string.Join("\n", MainViewModelRelPaths.Select(ReadSource)));
        Assert.DoesNotContain("ExtractFlow.ExtractAsync(", vmSource);
    }

    /// <summary>
    /// 测试 5：ExtractSettingsWindow.axaml 新增「并行解压」子 Tab，且三个线程数
    /// NumericUpDown 均用 Mode=TwoWay 绑定（对话框值写回 dialog.ViewModel，
    /// 测试 2 的回传块/测试 4 的快照才有意义）。
    ///
    /// 锁定的回归：若绑定是 OneWay（Avalonia NumericUpDown 默认值未经显式声明，
    /// 升级或重构可能改变默认），用户在 UI 上的调整不会写回 dialog.ViewModel。
    /// </summary>
    [Fact]
    public void ExtractSettingsWindow_ParallelTab_BindsTwoWay()
    {
        var source = ReadSource(ExtractSettingsWindowRelPath);
        var normalized = NormalizeWhitespace(source);

        // 并行解压子 Tab 存在（与设置窗口对称）
        Assert.Contains("LocalizedStrings[Settings_ExtractTab_Parallel]", normalized);
        // 三个线程数输入均 TwoWay
        Assert.Contains("Value=\"{Binding SmartSsdDegree, Mode=TwoWay}\"", normalized);
        Assert.Contains("Value=\"{Binding SmartHddDegree, Mode=TwoWay}\"", normalized);
        Assert.Contains("Value=\"{Binding ManualParallelDegree, Mode=TwoWay}\"", normalized);
    }

    /// <summary>
    /// 测试 6：SelectedItemsExtractService.ExtractEntriesAsync（选中条目解压，无对话框覆盖）
    /// 必须经 <c>ParallelDegreeResolver.ResolveAsync</c> 解析线程数（callerDegree = null），
    /// 并把结果写入 options.ParallelExtractDegree。
    ///
    /// 锁定的回归：接线改造前该方法直读 AppSettings.ParallelExtractDegree；若有人漏改此
    /// 路径（只改 ExtractFlow），选中条目/拖拽解压会绕过智能并行度决策链，行为与全量
    /// 解压不一致。
    /// </summary>
    [Fact]
    public void SelectedItemsExtractService_UsesParallelDegreeResolver()
    {
        var source = ReadSource(SelectedItemsExtractServiceRelPath);
        var normalized = NormalizeWhitespace(source);

        Assert.Contains("ParallelDegreeResolver.ResolveAsync", normalized);
        // 选中条目解压无对话框覆盖 → callerDegree 恒为 null
        Assert.Contains("callerDegree: null", normalized);
        Assert.Contains("ParallelExtractDegree = resolvedDegree", normalized);
    }

    /// <summary>
    /// 测试 7：ExtractSettingsViewModel 的并行选项初值必须从设置读取（镜像设置），
    /// 且不得写回设置（本次解压一次性生效），并暴露 BuildParallelSettings() 供调用点取快照。
    ///
    /// 锁定的回归：若初值写死或不读设置，"读取设置但不影响回来"的契约破裂；
    /// 若出现 settings.X = ... 写回，则对话框会悄悄改掉用户默认设置。
    /// </summary>
    [Fact]
    public void ExtractSettingsViewModel_ParallelMirrorsSettings_NoWriteBack()
    {
        var source = ReadSource(ExtractSettingsViewModelRelPath);
        var normalized = NormalizeWhitespace(source);

        // 初值读取设置（镜像）
        Assert.Contains("_enableParallelExtract = settings.EnableParallelExtract", normalized);
        Assert.Contains(
            "_manualParallelDegree = Math.Clamp(settings.ParallelExtractDegree, 1, 16)", normalized);

        // 提供本次快照构建
        Assert.Contains("public ParallelExtractSettings BuildParallelSettings()", normalized);

        // 不回写设置（不存在 settings.X = ... 形态的赋值）
        Assert.DoesNotContain("settings.EnableParallelExtract =", normalized);
        Assert.DoesNotContain("settings.SmartParallelDegree =", normalized);
        Assert.DoesNotContain("settings.ParallelExtractDegree =", normalized);
        Assert.DoesNotContain("settings.LargeArchivePreTest =", normalized);
    }

    /// <summary>
    /// 行为测试：ResolveDisplayParallelDegree 的二参重载语义——
    /// 传入的 resolver 解析结果（&gt;0）直接采用（并行度统计的最终消费点）；
    /// 未传时回落 AppSettings；引擎不支持并行时无论传什么都返回 1（串行隐藏，Rule 6）。
    ///
    /// 锁定的回归：若实现只在签名加参数但方法体仍无条件读 AppSettings，
    /// resolver 结果无法反映到进度窗口统计（显示与实际线程数漂移）。
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
