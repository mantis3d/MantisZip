using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MantisZip.Core.Utils;

/// <summary>
/// 占位符模板的**公共词法与结果校验**（文件名模板与根目录名模板共用一份实现）。
///
/// <para>
/// 抽出来的理由：两套模板的差别只有「哪些 token 算已知」与「渲染结果上限」，
/// 词法（<c>{{</c>/<c>}}</c> 转义、未闭合判定、嵌套 <c>{</c>、大小写不敏感）与结果校验
/// （非法字符 / 尾点空格 / 保留设备名）逐字相同。各写一份必然漂移成
/// 「文件名允许、根目录名不允许」这种难以解释的差异。错误类型继续复用
/// <see cref="FileNameTemplateError"/>：错误分类与文案对两套模板是同一套语义。
/// </para>
/// </summary>
internal static class TemplateLexing
{
    /// <summary>模板被拆成的一段：字面量或占位符（占位符已转小写）。</summary>
    internal readonly record struct Segment(bool IsLiteral, string Text);

    /// <summary>
    /// 把模板拆成字面量与占位符段。
    /// </summary>
    /// <param name="template">已确认非空的模板。</param>
    /// <param name="isKnownToken">该 token（原样，未转小写）是否属于此模板支持的范围。</param>
    /// <param name="segments">成功时的分段结果；失败为 null。</param>
    /// <param name="error">失败原因（未闭合 / 未知占位符）；成功为 null。</param>
    internal static bool TryTokenize(
        string template,
        Func<string, bool> isKnownToken,
        out List<Segment>? segments,
        out FileNameTemplateError? error)
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
                if (!isKnownToken(token))
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

    /// <summary>
    /// 渲染结果校验：非空、无非法字符、不以点/空格结尾、不是保留设备名、不超长。
    /// </summary>
    /// <param name="rendered">渲染出来的字符串。</param>
    /// <param name="maxLength">该模板允许的最大长度。</param>
    /// <param name="error">失败原因；成功为 null。</param>
    internal static bool TryValidateResult(string rendered, int maxLength, out FileNameTemplateError? error)
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

        if (rendered.Length > maxLength)
        {
            error = new FileNameTemplateError(FileNameTemplateErrorKind.TooLong, Rendered: rendered);
            return false;
        }

        return true;
    }

    /// <summary>含路径分隔符、Windows 非法字符或控制字符即不可用。</summary>
    internal static bool HasInvalidChar(string value)
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
    internal static bool IsReservedDeviceName(string rendered)
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
