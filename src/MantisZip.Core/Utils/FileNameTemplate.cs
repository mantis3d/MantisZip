using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MantisZip.Core.Utils;

/// <summary>
/// 渲染一次文件名模板所需的上下文（与「批」绑定：同一批共用一个 <see cref="Timestamp"/>）。
///
/// <see cref="AutoName"/> 是 <c>{name:auto}</c> 的取值，由调用方按「**该压缩包包含的源数**」
/// 算好传进来：单源包 / 分别压缩传 <c>null</c>（= 与 <c>{name}</c> 相同），多源单包传公共父目录名。
/// </summary>
public sealed record FileNameTemplateContext(
    string SourcePath,
    int Sequence,
    DateTime Timestamp,
    string? AutoName = null);

/// <summary>模板校验/渲染失败的原因分类（供 UI 选文案，不在 Core 里拼中文）。</summary>
public enum FileNameTemplateErrorKind
{
    /// <summary>未知占位符，如 <c>{foo}</c> / <c>{seq:abc}</c> / <c>{name:xxx}</c>。</summary>
    UnknownPlaceholder,
    /// <summary>花括号未闭合，如 <c>report_{name</c>。</summary>
    Unterminated,
    /// <summary>渲染结果为空或纯空白。</summary>
    EmptyResult,
    /// <summary>渲染结果含文件名非法字符。</summary>
    InvalidChar,
    /// <summary>渲染结果结尾是点/空格，或命中 Windows 保留设备名。</summary>
    InvalidName,
    /// <summary>渲染结果超过 <see cref="FileNameTemplate.MaxBaseNameLength"/>。</summary>
    TooLong,
}

/// <summary>
/// 模板错误。<see cref="Token"/> 是出问题的原始占位符（如 <c>foo</c> 或 <c>name:xxx</c>），
/// 无则 null；<see cref="Rendered"/> 仅结果类错误（空/非法字符/超长）才有值。
/// </summary>
public sealed record FileNameTemplateError(
    FileNameTemplateErrorKind Kind, string? Token = null, string? Rendered = null);

/// <summary>
/// 压缩包文件名模板（占位符渲染 + 校验）。
///
/// 语义要点：
/// - 模板渲染出的是**主名**，格式扩展名（.zip / .7z / .tar.gz）由
///   <see cref="CompressPathPlanner"/> 自动补，用户不写扩展名。
/// - 模板为空 / 纯空白 = **未启用**，<see cref="TryRender"/> 返回 false 且
///   <paramref name="baseName"/> 为 null，调用方必须回退旧命名（零行为变更）。
/// - 占位符大小写不敏感；未知占位符与未闭合一律失败，**不猜测、不忽略**。
/// - 单花括号 <c>}</c> 视为字面量（Windows 文件名允许它），只有 <c>{</c> 未闭合才算错。
/// </summary>
public static class FileNameTemplate
{
    /// <summary>示例行与帮助文档共用的推荐模板。</summary>
    public const string DefaultExample = "{name}_{date}";

    /// <summary>主名长度上限（不含格式扩展名）。</summary>
    public const int MaxBaseNameLength = 200;

    /// <summary>{seq:N} 允许的最大补零宽度。</summary>
    public const int MaxSequenceWidth = 9;

    /// <summary>{seq} 的默认补零宽度。</summary>
    public const int DefaultSequenceWidth = 3;

    /// <summary>
    /// 支持的占位符清单 —— **唯一事实源**：帮助文档的占位符表由它生成/校对，
    /// 新增占位符必须同时改这里、<see cref="TryTokenize"/> 与帮助文案。
    /// </summary>
    public static IReadOnlyList<string> Placeholders { get; } = new[]
    {
        "{name}",
        "{name:auto}",
        "{ext}",
        "{parent}",
        "{date}",
        "{datetime}",
        "{seq}",
        "{seq:0000}",
    };

    /// <summary>只做语法校验（不渲染）：未知占位符 / 未闭合。</summary>
    public static bool TryValidateSyntax(string? template, out FileNameTemplateError? error)
    {
        // 空模板不算语法错误：它表示「未启用」
        if (string.IsNullOrWhiteSpace(template))
        {
            error = null;
            return true;
        }
        return TryTokenize(template, out _, out error);
    }

    /// <summary>
    /// 渲染主名（不含格式扩展名）。
    /// 模板为空/纯空白 → 返回 false、<paramref name="baseName"/> = null（未启用，调用方走旧命名）。
    /// 语法或结果校验失败 → 返回 false 并给出 <paramref name="error"/>。
    /// </summary>
    public static bool TryRender(
        string? template,
        FileNameTemplateContext ctx,
        out string? baseName,
        out FileNameTemplateError? error)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        baseName = null;

        if (string.IsNullOrWhiteSpace(template))
        {
            error = null;
            return false;
        }

        if (!TryTokenize(template, out var segments, out error)) return false;

        var sb = new StringBuilder(template.Length + 16);
        foreach (var segment in segments!)
        {
            if (segment.IsLiteral)
            {
                sb.Append(segment.Text);
                continue;
            }

            sb.Append(Resolve(segment.Text, ctx));
        }

        var rendered = sb.ToString();
        if (!TryValidateResult(rendered, out error)) return false;

        baseName = rendered;
        return true;
    }

    // ════════════════════════════════════════════════════════════════
    //  词法
    // ════════════════════════════════════════════════════════════════

    private readonly record struct Segment(bool IsLiteral, string Text);

    private static bool TryTokenize(
        string template, out List<Segment>? segments, out FileNameTemplateError? error)
    {
        segments = new List<Segment>(8);
        error = null;

        var literal = new StringBuilder();
        for (int i = 0; i < template.Length; i++)
        {
            char ch = template[i];

            if (ch == '{')
            {
                // {{ → 字面量 {
                if (i + 1 < template.Length && template[i + 1] == '{')
                {
                    literal.Append('{');
                    i++;
                    continue;
                }

                int close = template.IndexOf('}', i + 1);
                if (close < 0)
                {
                    error = new FileNameTemplateError(
                        FileNameTemplateErrorKind.Unterminated, template[i..]);
                    segments = null;
                    return false;
                }

                // 未闭合嵌套 { 视为未闭合（如 "{a{b}"）
                int nested = template.IndexOf('{', i + 1);
                if (nested >= 0 && nested < close)
                {
                    error = new FileNameTemplateError(
                        FileNameTemplateErrorKind.Unterminated, template[i..close]);
                    segments = null;
                    return false;
                }

                var token = template[(i + 1)..close];
                if (!IsKnownToken(token))
                {
                    error = new FileNameTemplateError(
                        FileNameTemplateErrorKind.UnknownPlaceholder, token);
                    segments = null;
                    return false;
                }

                if (literal.Length > 0)
                {
                    segments.Add(new Segment(true, literal.ToString()));
                    literal.Clear();
                }
                segments.Add(new Segment(false, token.ToLowerInvariant()));
                i = close;
                continue;
            }

            // }} → 字面量 }；单个 } 也按字面量处理
            if (ch == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                literal.Append('}');
                i++;
                continue;
            }

            literal.Append(ch);
        }

        if (literal.Length > 0) segments.Add(new Segment(true, literal.ToString()));
        return true;
    }

    /// <summary>token 已转小写后传入（校验时也先转小写）。</summary>
    private static bool IsKnownToken(string token)
    {
        switch (token.ToLowerInvariant())
        {
            case "name":
            case "name:auto":
            case "ext":
            case "parent":
            case "date":
            case "datetime":
            case "seq":
                return true;
        }

        return TryParseSequenceWidth(token, out _);
    }

    private static bool TryParseSequenceWidth(string token, out int width)
    {
        width = 0;
        if (!token.StartsWith("seq:", StringComparison.OrdinalIgnoreCase)) return false;

        var digits = token[4..];
        if (digits.Length is < 1 or > MaxSequenceWidth) return false;
        foreach (var c in digits)
        {
            if (c is < '0' or > '9') return false;
        }

        // 宽度 = 位数本身，不是数字的字面值：{seq:0000} 是 4 位补零，{seq:4} 也是 4 位。
        // （早先把 "0000" 当整数解析会得到 0 ⇒ 误判为未知占位符，这是本项目的真实踩坑。）
        width = digits.Length;
        return true;
    }

    // ════════════════════════════════════════════════════════════════
    //  渲染
    // ════════════════════════════════════════════════════════════════

    private static string Resolve(string token, FileNameTemplateContext ctx)
    {
        switch (token)
        {
            case "name":
                return GetPrimaryName(ctx.SourcePath);
            case "name:auto":
                return string.IsNullOrEmpty(ctx.AutoName)
                    ? GetPrimaryName(ctx.SourcePath)
                    : ctx.AutoName!;
            case "ext":
                return GetPrimaryExtension(ctx.SourcePath);
            case "parent":
                return GetParentName(ctx.SourcePath);
            case "date":
                return ctx.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            case "datetime":
                return ctx.Timestamp.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
            case "seq":
                return ctx.Sequence.ToString(
                    "D" + DefaultSequenceWidth, CultureInfo.InvariantCulture);
            default:
                TryParseSequenceWidth(token, out var width);
                return ctx.Sequence.ToString("D" + width, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>源主名：文件 = 不含扩展名的文件名；目录 = 目录名；尾分隔符按目录处理。</summary>
    internal static string GetPrimaryName(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath)) return string.Empty;

        var trimmed = sourcePath.TrimEnd('\\', '/');
        if (trimmed.Length == 0) return string.Empty; // 纯根，如 "C:\"

        if (LooksLikeDirectory(sourcePath, trimmed))
            return Path.GetFileName(trimmed);

        return Path.GetFileNameWithoutExtension(trimmed);
    }

    /// <summary>源扩展名（含点）：文件才有，目录为空。语义等同 <see cref="Path.GetExtension"/>。</summary>
    internal static string GetPrimaryExtension(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath)) return string.Empty;

        var trimmed = sourcePath.TrimEnd('\\', '/');
        if (trimmed.Length == 0) return string.Empty;

        if (LooksLikeDirectory(sourcePath, trimmed)) return string.Empty;
        return Path.GetExtension(trimmed);
    }

    /// <summary>父目录名：<c>D:\工作\报告\a.txt</c> → <c>报告</c>；盘符根 / 无父目录 → 空串。</summary>
    internal static string GetParentName(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath)) return string.Empty;

        var trimmed = sourcePath.TrimEnd('\\', '/');
        if (trimmed.Length == 0) return string.Empty;

        var parent = Path.GetDirectoryName(trimmed);
        if (string.IsNullOrEmpty(parent)) return string.Empty;
        if (PathCommonality.IsDriveRoot(parent)) return string.Empty;

        return Path.GetFileName(parent.TrimEnd('\\', '/'));
    }

    private static bool LooksLikeDirectory(string original, string trimmed)
    {
        if (original.Length > trimmed.Length) return true; // 带尾分隔符
        try
        {
            return Directory.Exists(trimmed);
        }
        catch
        {
            return false;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  结果校验
    // ════════════════════════════════════════════════════════════════

    private static bool TryValidateResult(string rendered, out FileNameTemplateError? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(rendered))
        {
            error = new FileNameTemplateError(FileNameTemplateErrorKind.EmptyResult, Rendered: rendered);
            return false;
        }

        if (HasInvalidChar(rendered))
        {
            error = new FileNameTemplateError(FileNameTemplateErrorKind.InvalidChar, Rendered: rendered);
            return false;
        }

        if (rendered[^1] is '.' or ' ')
        {
            error = new FileNameTemplateError(FileNameTemplateErrorKind.InvalidName, Rendered: rendered);
            return false;
        }

        if (IsReservedDeviceName(rendered))
        {
            error = new FileNameTemplateError(FileNameTemplateErrorKind.InvalidName, Rendered: rendered);
            return false;
        }

        if (rendered.Length > MaxBaseNameLength)
        {
            error = new FileNameTemplateError(FileNameTemplateErrorKind.TooLong, Rendered: rendered);
            return false;
        }

        return true;
    }

    private static bool HasInvalidChar(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c)) return true;
            if (c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|') return true;
            if (Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0) return true;
        }
        return false;
    }

    /// <summary>命中 Windows 保留设备名即不可用（<c>CON.txt</c> 也算）。</summary>
    private static bool IsReservedDeviceName(string rendered)
    {
        int dot = rendered.IndexOf('.');
        var stem = (dot >= 0 ? rendered[..dot] : rendered).Trim().ToUpperInvariant();
        if (stem.Length == 0) return false;

        switch (stem)
        {
            case "CON":
            case "PRN":
            case "AUX":
            case "NUL":
                return true;
        }

        if (stem.Length == 4)
        {
            var prefix = stem[..3];
            char digit = stem[3];
            if ((prefix == "COM" || prefix == "LPT") && digit is >= '1' and <= '9') return true;
        }

        return false;
    }
}
