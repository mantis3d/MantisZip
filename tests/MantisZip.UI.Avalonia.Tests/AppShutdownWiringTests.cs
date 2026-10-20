// ============================================================================
// AppShutdown 接线架构守卫测试 —— 源码文本级回归绊线
//
// 目的：CLI 退出路径原先直接调用 desktop.Shutdown()，而 CLI 分发整段跑在
// OnFrameworkInitializationCompleted 内（Avalonia 主循环尚未启动）——Shutdown() 会先拆掉
// Dispatcher，随后 StartCore→MainLoop→PushFrame 抛
// InvalidOperationException("Cannot perform requested operation because the Dispatcher shut down")，
// 主线程未处理异常、进程以 0xE0434352 终止（实测 12 条 CLI 提前退出路径）。
// 修复方式：全仓唯一的退出漏斗 Services/AppShutdown.cs，主循环前改 Post 延后。
// 本文件读取生产 .cs 源码文本，断言「裸调用」与「状态位翻转点」不能悄悄回归。
//
// 为什么不写行为测试：真实崩溃只在「进程启动 → CLI 分发 → 主循环启动」这一时序里发生，
// 测试进程无法复现整条链（行为级取证由计划 §验证 的退出码矩阵覆盖：9 条命令 ExitCode=0 + stderr 空）。
//
// 诚实的局限：这些守卫只证明「坏的直调没有回来」「状态位没被挪到前面」，
// 不证明「退出时序真的正确」——后者由退出码矩阵与 GUI 人工验收覆盖。
//
// 方法论：所有子串扫描都跳过注释与字符串字面量（TrySkipNonCode），
// 否则 HandleShellCommand / AppShutdown 文档里提到的 desktop.Shutdown() 会被误判为真实调用。
// ============================================================================
using System.Text.RegularExpressions;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// AppShutdown 退出漏斗接线的架构守卫测试（共 4 个）。
/// 目的与诚实局限见文件头注释；每个测试的 XML 注释说明其锁定的具体回归。
/// </summary>
public class AppShutdownWiringTests
{
    // ------------------------------------------------------------------
    // 被测生产源码（相对仓库根，'/' 分隔，读取时替换为平台分隔符）
    // ------------------------------------------------------------------

    /// <summary>应用退出调用点（CLI 分发与各退出分支）所在文件（partial 主干）。</summary>
    private const string AppRelPath = "src/MantisZip.UI.Avalonia/App.axaml.cs";

    /// <summary>
    /// App 的全部 partial 源码文件（2026-10-20 大文件拆分后：CLI 解压/压缩/基础设施与主题各自独立）。
    /// ⚠ 新增 App.*.cs partial 时须同步追加，否则相关守卫只扫描一部分退出调用点。
    /// </summary>
    private static readonly string[] AppRelPaths =
    {
        AppRelPath,
        "src/MantisZip.UI.Avalonia/App.Theming.cs",
        "src/MantisZip.UI.Avalonia/App.CliExtract.cs",
        "src/MantisZip.UI.Avalonia/App.CliInfra.cs",
        "src/MantisZip.UI.Avalonia/App.CliCompress.cs",
    };

    /// <summary>主窗口「退出」命令所在文件（唯一一处非 App 的退出调用）。</summary>
    private const string MainViewModelRelPath =
        "src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs";

    // ==================================================================
    // 共享辅助：仓库根定位 / 源码读取 / 代码位扫描
    // （模式复制自 ExtractPreserveFullPathWiringTests）
    // ==================================================================

    /// <summary>
    /// 从测试程序集输出目录向上遍历，找到第一个包含 src 子目录的目录作为仓库根。
    /// 找不到时抛出带清晰消息的 InvalidOperationException，使 RepoRoot_IsLocatable
    /// 以可读原因最先失败，而不是让其余守卫报出令人困惑的文件缺失错误。
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

    /// <summary>读取被测生产源码文件全文。</summary>
    private static string ReadSource(string relativePath) =>
        File.ReadAllText(SourcePath(relativePath));

    /// <summary>字符是否是标识符字符（字母/数字/下划线）。</summary>
    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// 若 i 处是注释或字符串/字符字面量的起点，则跳过整个构造并返回 true（i 移到其后）；
    /// 否则返回 false（i 不变，按普通代码字符处理）。已处理：// 行注释、块注释、
    /// 普通字符串（反斜杠转义）、@ 逐字字符串（含 $@/@$ 前缀）、字符字面量。
    /// 已知简化：插值洞内的引号、raw string literal 不精确 —— 被测区域不存在这些形态，
    /// 若未来引入会以「扫描越界/计数偏差」的方式响亮暴露，不会静默误判。
    /// </summary>
    private static bool TrySkipNonCode(string s, ref int i)
    {
        if (i >= s.Length) return false;

        if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
        {
            while (i < s.Length && s[i] != '\n') i++;
            return true;
        }

        if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
        {
            i += 2;
            while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
            i = Math.Min(i + 2, s.Length);
            return true;
        }

        if (s[i] == '"')
        {
            var verbatim = i > 0 &&
                (s[i - 1] == '@' ||
                 (s[i - 1] == '$' && i > 1 && s[i - 2] == '@'));
            i++;
            while (i < s.Length)
            {
                if (s[i] == '\\' && !verbatim) { i += 2; continue; }
                if (s[i] == '"')
                {
                    if (verbatim && i + 1 < s.Length && s[i + 1] == '"') { i += 2; continue; }
                    i++;
                    return true;
                }
                i++;
            }
            return true;
        }

        if (s[i] == '\'')
        {
            i++;
            while (i < s.Length)
            {
                if (s[i] == '\\') { i += 2; continue; }
                if (s[i] == '\'') { i++; return true; }
                i++;
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// 从 from 开始查找 token 第一次以「代码形态」（非注释、非字面量内部）出现的下标；
    /// 要求 token 前一个字符不是标识符字符（避免命中更长标识符的后缀）。找不到返回 -1。
    /// </summary>
    private static int FindTokenInCode(string s, string token, int from)
    {
        var i = from;
        while (i < s.Length)
        {
            if (TrySkipNonCode(s, ref i)) continue;

            if (i + token.Length <= s.Length &&
                string.CompareOrdinal(s, i, token, 0, token.Length) == 0 &&
                (i == 0 || !IsIdentifierChar(s[i - 1])))
            {
                return i;
            }
            i++;
        }
        return -1;
    }

    /// <summary>统计 token 以代码形态出现的次数（跳过注释与字符串）。</summary>
    private static int CountTokenInCode(string s, string token)
    {
        var count = 0;
        var from = 0;
        while (true)
        {
            var idx = FindTokenInCode(s, token, from);
            if (idx < 0) return count;
            count++;
            from = idx + 1;
        }
    }

    // ==================================================================
    // 守卫测试
    // ==================================================================

    /// <summary>
    /// 测试 1（fail-fast 前置守卫）：仓库根可定位，且两个被测源码文件都存在。
    /// 锁定的回归：仓库根发现逻辑或被测文件路径一旦失效，本测试必须最先以
    /// 「哪个路径缺失」的清晰消息失败，而不是让测试 2-4 报出内部扫描错误。
    /// </summary>
    [Fact]
    public void RepoRoot_IsLocatable_AndSourcesExist()
    {
        var root = GetRepoRoot(); // 失败时抛出带 BaseDirectory 的清晰消息
        Assert.True(Directory.Exists(root), $"仓库根不存在: {root}");

        foreach (var rel in AppRelPaths.Append(MainViewModelRelPath))
        {
            var full = SourcePath(rel);
            Assert.True(File.Exists(full), $"被测源码缺失: {full}");
        }
    }

    /// <summary>
    /// 测试 2：App 全部 partial 与 MainWindowViewModel.cs 中不存在<b>裸</b>
    /// <c>desktop.Shutdown(</c> 调用 —— 退出必须全部经 AppShutdown.Request（唯一漏斗）。
    ///
    /// 锁定的回归：任何人在 CLI 分发同步段（主循环启动前）重新写回
    /// <c>desktop.Shutdown()</c>，即重新引入未处理异常（退出码 0xE0434352）。
    /// 已跳过注释/字符串，故 HandleShellCommand 文档里的同名文字不算违规。
    /// </summary>
    [Fact]
    public void NoBareDesktopShutdownCalls_RemainInExitPaths()
    {
        foreach (var rel in AppRelPaths.Append(MainViewModelRelPath))
        {
            var source = ReadSource(rel);
            var hits = CountTokenInCode(source, "desktop.Shutdown(");
            Assert.True(hits == 0,
                $"{rel} 中仍有 {hits} 处裸 desktop.Shutdown( 调用；" +
                "退出必须改为 AppShutdown.Request(desktop)（见 Services/AppShutdown.cs）。");
        }
    }

    /// <summary>
    /// 测试 3：状态位翻转点必须晚于 base 调用 ——
    /// <c>AppShutdown.MarkFrameworkInitCompleted()</c> 在文本上位于
    /// <c>base.OnFrameworkInitializationCompleted()</c> 之后。
    ///
    /// 锁定的回归：把翻转提前到 base 调用之前（例如挪到方法开头），
    /// 会让「主循环是否已启动」的判据对整个 CLI 分发段失效 ——
    /// 12 条 CLI 退出路径立刻退回崩溃行为。
    /// </summary>
    [Fact]
    public void MarkFrameworkInitCompleted_ComesAfterBaseCall()
    {
        var source = ReadSource(AppRelPath);

        var baseIdx = FindTokenInCode(source, "base.OnFrameworkInitializationCompleted()", 0);
        Assert.True(baseIdx >= 0,
            $"{AppRelPath} 中未找到 base.OnFrameworkInitializationCompleted() 调用；被测源码可能已重构。");

        var markIdx = FindTokenInCode(source, "AppShutdown.MarkFrameworkInitCompleted()", 0);
        Assert.True(markIdx >= 0,
            $"{AppRelPath} 中未找到 AppShutdown.MarkFrameworkInitCompleted() 调用；" +
            "主循环启动窗口的判定将失效，CLI 退出路径会退回未处理异常。");

        Assert.True(markIdx > baseIdx,
            "AppShutdown.MarkFrameworkInitCompleted() 必须晚于 base.OnFrameworkInitializationCompleted()："
            + $"当前 base@{baseIdx} / mark@{markIdx}。提前翻转会让整个 CLI 分发段退回崩溃行为。");
    }

    /// <summary>
    /// 测试 4：App 全部 partial 内的退出漏斗调用点合计数量不低于 30 ——
    /// 锁定的回归：有人把收口「改回去一半」（例如整段替换回 desktop.Shutdown()）。
    ///
    /// 为什么不匹固定数字：收口点数量会随 CLI 分支增减而变（当前 39 处），
    /// 写死数字会让每次新增 CLI 分支都要改测试；下限断言只锁「收口没有被大规模回退」这一事实，
    /// 真正的完整性由测试 2（裸调用清零）保证。
    /// </summary>
    [Fact]
    public void AppShutdownRequest_IsUsedThroughoutApp()
    {
        var hits = AppRelPaths.Sum(rel => CountTokenInCode(ReadSource(rel), "AppShutdown.Request("));
        Assert.True(hits >= 30,
            $"App 全部 partial 中 AppShutdown.Request( 合计仅出现 {hits} 次（下限 30）；" +
            "退出调用疑似被改回裸 desktop.Shutdown()（见测试 2 与 Services/AppShutdown.cs）。");
    }
}
