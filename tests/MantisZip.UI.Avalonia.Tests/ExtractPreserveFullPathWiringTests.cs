// ============================================================================
// ExtractPreserveFullPath 接线架构守卫测试 —— 源码文本级回归绊线
//
// 目的：缺陷修复后（解压目标文件夹对话框原先只返回路径字符串，调用方各自
// 重新读取 settings.ExtractPreserveFullPath，造成「预览展示的选项 ≠ 实际
// 解压行为」），对话框返回的勾选值（preserveFullPath）被一路穿透到实际解压
// 调用。本文件读取生产 .cs 源码文本，断言坏接线形态不能悄悄回归。
//
// 为什么不写 mock 行为测试：ExtractFlow.RunSelectedItemsExtractionAsync 是
// static 且内部构造 ProgressWindow，DragDropService 是 internal，均无法
// mock；且刻意不引入 Microsoft.CodeAnalysis.CSharp 新测试依赖。故采用源码
// 文本守卫（architecture guard）。
//
// 诚实的局限：这些守卫只证明「坏的直读没有回来」（the bad read did not come
// back），不证明「正确的值真的流动到了解压调用」（the right value flows）
// ——后者由行为级测试与人工验证覆盖。
//
// 方法论：所有子串断言前先做空白归一（\s+ → 单空格），避免换行/缩进重排
// 造成假通过；断言作用域限定在花括号/圆括号配对提取出的方法体/实参表内，
// 避免全文子串匹配被别处同名文本干扰。字面量扫描的已知简化见
// TrySkipNonCode 的注释。
// ============================================================================
using System.Text.RegularExpressions;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// ExtractPreserveFullPath 接线的架构守卫测试（共 4 个）。
/// 目的与诚实局限见文件头注释；每个测试的 XML 注释说明其锁定的具体回归。
/// </summary>
public class ExtractPreserveFullPathWiringTests
{
    // ------------------------------------------------------------------
    // 被测生产源码（相对仓库根，'/' 分隔，读取时替换为平台分隔符）
    // ------------------------------------------------------------------

    /// <summary>核心解压方法 ExtractSelectedEntriesCoreAsync 所在文件。</summary>
    private const string MainViewModelRelPath =
        "src/MantisZip.UI.Avalonia/ViewModels/MainWindowViewModel.cs";

    /// <summary>拖拽解压共享调用点所在文件。</summary>
    private const string DragDropServiceRelPath =
        "src/MantisZip.UI.Avalonia/Services/DragDropService.cs";

    /// <summary>两条路径共同汇入的漏斗 RunSelectedItemsExtractionAsync 所在文件。</summary>
    private const string ExtractFlowRelPath =
        "src/MantisZip.UI.Avalonia/Services/ExtractFlow.cs";

    // ==================================================================
    // 共享辅助：仓库根定位 / 源码读取 / 空白归一 / 字面量扫描 / 括号配对
    // ==================================================================

    /// <summary>
    /// 从测试程序集输出目录向上遍历，找到第一个包含 src 子目录的目录作为仓库根。
    /// 模式复制自 tests/MantisZip.Tests/AboutWindowTests.cs 的 GetRepoRoot()。
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

    /// <summary>读取被测生产源码文件全文（路径基于仓库根解析，共享给所有测试）。</summary>
    private static string ReadSource(string relativePath) =>
        File.ReadAllText(SourcePath(relativePath));

    /// <summary>
    /// 空白归一：把任意长度的空白串（含换行/制表/缩进）折叠为单个空格。
    /// 必须在所有子串断言之前调用，否则重排/折行会让同一逻辑文本产生不同的
    /// 字节形态——对「必须出现」的断言造成假失败，对「必须不出现」的断言造成假通过。
    /// </summary>
    private static string NormalizeWhitespace(string text) =>
        Regex.Replace(text, @"\s+", " ");

    /// <summary>
    /// 若 i 处是注释或字符串/字符字面量的起点，则跳过整个构造并返回 true（i 移动到其后）；
    /// 否则返回 false（i 不变，按普通代码字符处理）。
    ///
    /// 已处理：// 行注释、斜杠星号块注释、普通字符串（反斜杠转义）、
    /// @ 逐字字符串（双引号转义，含 $@/@$ 前缀）、字符字面量（反斜杠转义）。
    ///
    /// 已知简化（不假装精确）：插值字符串整体按普通字符串扫描，插值洞内若含引号
    /// 会提前结束扫描；raw string literal（三引号）完全未识别。当前被测区域不存在
    /// 这些形态——若未来引入，需同步扩展本扫描器，否则括号配对会以「未找到配对」
    /// 的方式响亮失败（不会静默误判）。
    /// </summary>
    private static bool TrySkipNonCode(string s, ref int i)
    {
        if (i >= s.Length) return false;

        // // 行注释：跳到行尾（不含换行符）
        if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
        {
            while (i < s.Length && s[i] != '\n') i++;
            return true;
        }

        // 斜杠星号块注释
        if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
        {
            i += 2;
            while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
            i = Math.Min(i + 2, s.Length);
            return true;
        }

        if (s[i] == '"')
        {
            // 逐字字符串（@ / $@ / @$ 前缀）：无反斜杠转义，仅双引号成对表示引号
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
            return true; // 未闭合：i 落到结尾，外层以「配对失败」响亮报错
        }

        // 字符字面量
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
    /// 括号/花括号配对：openIndex 处必须是 open 字符，返回配对 close 字符的下标。
    /// 扫描跳过注释与字符串/字面量（简化项见 TrySkipNonCode 注释）。
    /// 失配时抛 InvalidOperationException（响亮失败，不静默返回错误范围）。
    /// </summary>
    private static int FindMatchingDelimiter(string s, int openIndex, char open, char close)
    {
        if (openIndex < 0 || openIndex >= s.Length || s[openIndex] != open)
            throw new InvalidOperationException(
                $"FindMatchingDelimiter：下标 {openIndex} 处不是 '{open}'，内部提取逻辑失效。");

        var depth = 0;
        var i = openIndex;
        while (i < s.Length)
        {
            // TrySkipNonCode 为 true 时已把 i 移到构造之后，此时不能额外 +1
            if (TrySkipNonCode(s, ref i)) continue;

            if (s[i] == open) depth++;
            else if (s[i] == close)
            {
                depth--;
                if (depth == 0) return i;
            }
            i++;
        }
        throw new InvalidOperationException(
            $"未找到与下标 {openIndex} 处的 '{open}' 配对的 '{close}'——源码可能被截断，" +
            "或字面量扫描对新引入的写法失配（见 TrySkipNonCode 的已知简化说明）。");
    }

    /// <summary>字符是否是标识符字符（字母/数字/下划线）。</summary>
    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

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

    /// <summary>
    /// 定位方法「声明」而非调用点：找到 methodName( 的代码位置后做圆括号配对，
    /// 跳过其后的空白/注释，若下一个有效字符是 { 即视为声明（合法 C# 中调用点的
    /// 右括号之后不可能直接跟 {，如 if/using/foreach 的外层括号会先闭合），
    /// 否则继续向后搜索下一个出现位置。
    /// 返回：方法名起始下标、方法体开花括号下标。
    /// 已知简化：表达式体方法（箭头体）不被识别，会一路搜到末尾并抛「未找到声明」。
    /// </summary>
    private static (int NameIndex, int BodyOpenBraceIndex) FindMethodDeclaration(
        string source, string methodName)
    {
        var token = methodName + "(";
        var searchFrom = 0;
        while (true)
        {
            var nameIdx = FindTokenInCode(source, token, searchFrom);
            if (nameIdx < 0)
                throw new InvalidOperationException(
                    $"未在源码中找到方法 {methodName} 的声明（或声明后未紧跟方法体）。" +
                    "被测源码可能已重构，守卫测试需同步更新。");

            var openParen = nameIdx + methodName.Length;
            var closeParen = FindMatchingDelimiter(source, openParen, '(', ')');

            var j = closeParen + 1;
            while (j < source.Length)
            {
                if (char.IsWhiteSpace(source[j])) { j++; continue; }
                if (TrySkipNonCode(source, ref j)) continue;
                break;
            }

            if (j < source.Length && source[j] == '{')
                return (nameIdx, j);

            searchFrom = nameIdx + 1; // 调用点：继续找下一个出现位置
        }
    }

    /// <summary>
    /// 提取方法「签名 + 方法体」全文（从方法名到方法体配对的右花括号，含两端）。
    /// 签名部分用于断言参数存在；方法体部分用于断言设置读取形态。
    /// </summary>
    private static string ExtractMethodSpan(string source, string methodName)
    {
        var (nameIndex, openBrace) = FindMethodDeclaration(source, methodName);
        var closeBrace = FindMatchingDelimiter(source, openBrace, '{', '}');
        return source.Substring(nameIndex, closeBrace - nameIndex + 1);
    }

    /// <summary>
    /// 提取 callPrefix（以左括号结尾，如 "ExtractFlow.RunSelectedItemsExtractionAsync("）
    /// 这次调用的实参表文本（不含外层括号本身），基于圆括号配对，嵌套括号/字符串/注释均跳过。
    /// 与实参顺序无关——这是它优于「跳过固定 N 个 token 再匹配」方案的原因。
    /// </summary>
    private static string ExtractCallArguments(string source, string callPrefix)
    {
        var callIndex = FindTokenInCode(source, callPrefix, 0);
        if (callIndex < 0)
            throw new InvalidOperationException(
                $"未在源码中找到调用 {callPrefix}。被测源码可能已重构，守卫测试需同步更新。");

        var openParen = callIndex + callPrefix.Length - 1;
        var closeParen = FindMatchingDelimiter(source, openParen, '(', ')');
        return source.Substring(openParen + 1, closeParen - openParen - 1);
    }

    // ==================================================================
    // 4 个守卫测试
    // ==================================================================

    /// <summary>
    /// 测试 1（fail-fast 前置守卫）：仓库根可定位，且三个被测源码文件都存在于磁盘上。
    ///
    /// 锁定的回归：仓库根发现逻辑（向上找 src 目录）或被测文件路径一旦失效，
    /// 本测试必须最先以「哪个路径缺失」的清晰消息失败，而不是让测试 2-4 报出
    /// 「方法未找到」「调用未找到」这类令人困惑的内部提取错误。
    ///
    /// 为什么不用子串匹配：本测试关心的是「文件在不在」这一事实本身，与文件内容
    /// 无关；对不存在的文件做子串断言只会抛 IOException，无法区分「根定位失败」
    /// 与「源码被移动/删除」两种故障，故必须用显式 Exists 断言。
    /// </summary>
    [Fact]
    public void RepoRoot_IsLocatable()
    {
        var root = GetRepoRoot(); // 失败时抛出带 BaseDirectory 的清晰消息

        var expected = new[]
        {
            MainViewModelRelPath,
            DragDropServiceRelPath,
            ExtractFlowRelPath,
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
    /// 测试 2：核心解压方法 ExtractSelectedEntriesCoreAsync 的签名必须带
    /// 「bool? preserveFullPath = null」可空参数（对话框勾选值的穿透通道）。
    ///
    /// 锁定的回归：若有人把该参数删掉（退回旧的「调用方各自读设置」模式），
    /// 对话框勾选值将无处传入，「预览展示 ≠ 实际解压」缺陷回归。
    ///
    /// 为什么纯子串匹配不足：方法名在文件里出现多次（两次调用点 + 声明），
    /// 若只对全文搜 bool? preserveFullPath，一旦声明被改而调用点恰好残留同名
    /// 文本（或反之）就会误判；必须先花括号/圆括号配对定位到声明本身再断言。
    /// 另外断言前做了空白归一，避免签名被折行成 "bool? preserveFullPath" 之外的
    /// 字节形态造成假失败。
    /// </summary>
    [Fact]
    public void MainWindowViewModel_CoreMethodTakesNullablePreserveFullPathParameter()
    {
        var source = ReadSource(MainViewModelRelPath);
        var methodSpan = ExtractMethodSpan(source, "ExtractSelectedEntriesCoreAsync");
        var normalized = NormalizeWhitespace(methodSpan);

        Assert.Contains("bool? preserveFullPath", normalized);
        Assert.Contains("bool? preserveFullPath = null", normalized);
    }

    /// <summary>
    /// 测试 3：核心方法体内对 settings.ExtractPreserveFullPath 的读取只允许以
    /// 「preserveFullPath ?? settings.ExtractPreserveFullPath」这一处兜底形态出现，
    /// 不允许出现任何绕过参数、直读设置的第二处读取。
    ///
    /// 锁定的回归：缺陷的原始形态正是「核心方法无视传入的对话框值，直接读
    /// settings.ExtractPreserveFullPath」。本测试钉死唯一合法兜底点，防止该直读回来。
    ///
    /// 为什么纯子串匹配不足（两层原因）：
    /// 1. 只断言 "settings.ExtractPreserveFullPath" 不出现是错的——合法兜底表达式
    ///    本身就含该子串，会永远误报；所以先正面断言兜底表达式存在（这比单纯
    ///    缺失断言更强），再把兜底表达式从方法体中剥离后断言残余部分不含它，
    ///    即「除兜底外无第二处读取」。
    /// 2. 必须先花括号配对把作用域限定到该方法体——文件里 ExtractSelectedTo /
    ///    ExtractSelectedHere 也有合法的 settings.ExtractPreserveFullPath 读取
    ///    （用于预填对话框），全文匹配会误报。空白归一防止兜底表达式被折行后
    ///    子串失配造成假通过。
    /// </summary>
    [Fact]
    public void MainWindowViewModel_CoreMethodBodyDoesNotReadSettings()
    {
        var source = ReadSource(MainViewModelRelPath);
        var methodSpan = ExtractMethodSpan(source, "ExtractSelectedEntriesCoreAsync");
        var normalized = NormalizeWhitespace(methodSpan);

        // 唯一合法形态：参数优先，设置仅作兜底
        const string legitimateFallback = "preserveFullPath ?? settings.ExtractPreserveFullPath";
        Assert.Contains(legitimateFallback, normalized);

        // 剥离所有合法兜底后，方法体内不得残留任何对该设置的直读
        var withoutLegitimateFallback = normalized.Replace(legitimateFallback, " ");
        Assert.DoesNotContain("settings.ExtractPreserveFullPath", withoutLegitimateFallback);
    }

    /// <summary>
    /// 测试 4：DragDropService 唯一的 ExtractFlow.RunSelectedItemsExtractionAsync
    /// 调用点必须传局部变量 preserveFullPath，而不是 _settings.ExtractPreserveFullPath。
    ///
    /// 锁定的回归：拖拽路径有两条分支——目标目录检测成功（无对话框，局部变量
    /// 保持设置初值）与检测失败（弹对话框，局部变量被 pick.PreserveFullPath 覆盖），
    /// 两分支必须汇入同一调用点并传同一个局部变量。若有人在调用点改回直读
    /// _settings.ExtractPreserveFullPath，对话框勾选将被忽略、检测成功分支也会
    /// 丢失统一入口，缺陷回归。
    ///
    /// 为什么纯子串/固定 token 匹配不足：
    /// 1. 作用域必须是实参表而非全文——文件上方第 77 行合法存在
    ///    var preserveFullPath = _settings.ExtractPreserveFullPath; 初值赋值与
    ///    第 89 行传给对话框的 _settings.ExtractPreserveFullPath，全文断言必误报；
    /// 2. 早期方案用「跳过固定 3 个 token 再匹配」，只要有人调换实参顺序就会假
    ///    通过——圆括号配对提取的实参表与顺序无关，重排不影响判定；
    /// 3. 空白归一防止实参表折行/重排导致子串失配的假通过。
    /// </summary>
    [Fact]
    public void DragDropService_PassesLocalPreserveFullPathToExtractFlow()
    {
        var source = ReadSource(DragDropServiceRelPath);
        var argumentList = ExtractCallArguments(
            source, "ExtractFlow.RunSelectedItemsExtractionAsync(");
        var normalized = NormalizeWhitespace(argumentList);

        // 裸标识符 preserveFullPath（前后非标识符字符），排除 _settings.xxx 等成员访问形态
        Assert.Matches(@"(?<![\w.])preserveFullPath(?![\w])", normalized);
        Assert.DoesNotContain("_settings.ExtractPreserveFullPath", normalized);
    }
}
