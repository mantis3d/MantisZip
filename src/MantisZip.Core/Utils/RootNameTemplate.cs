using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MantisZip.Core.Utils;

/// <summary>
/// 渲染一次根目录名模板所需的上下文（与「批」绑定：同一批共用一个 <see cref="Timestamp"/>）。
///
/// <para>
/// <see cref="SourcePaths"/> 是这一批的源列表（单源压缩时只有一项）。多源时
/// <c>{name}</c> 取**公共父目录名**而不是「第一个源的名字」——否则名字会随
/// 「用户先选谁」漂移，同名不同选中顺序会产出不同根目录。
/// </para>
/// </summary>
public sealed record RootNameTemplateContext(
    IReadOnlyList<string> SourcePaths,
    DateTime Timestamp,
    string? ArchiveName = null);

/// <summary>
/// 「包内根目录」加根时使用的根目录名模板。
///
/// 语义要点（与 <see cref="FileNameTemplate"/> 一致的部分不重复，只列差异）：
/// - 占位符只有 5 个：<c>{name}</c> <c>{parent}</c> <c>{date}</c> <c>{datetime}</c> <c>{archive}</c>。
///   没有 <c>{ext}</c>（目录源恒为空）与 <c>{seq}</c>（加根只加一层，不存在序号）。
/// - 渲染出的是**单段目录名**，含 <c>/</c> <c>\</c> 一律判非法字符，不猜测用户想建多级目录。
/// - 模板为空 / 纯空白 = **未启用**，<see cref="TryRender"/> 返回 false 且
///   <paramref name="rootName"/> 为 null，调用方必须回退「压缩包文件名」。
/// - 时间戳与文件名模板共用同一个批级时间戳，避免「预览 23:59、执行 00:01」。
/// </summary>
public static class RootNameTemplate
{
    /// <summary>示例行与帮助文档共用的推荐模板。</summary>
    public const string DefaultExample = "{name}_{date}";

    /// <summary>根目录名长度上限。</summary>
    public const int MaxNameLength = 200;

    /// <summary>
    /// 支持的占位符清单 —— **唯一事实源**：插入菜单、帮助文档与测试都由它取。
    /// 新增占位符必须同时改这里、<see cref="IsKnownToken"/> 与帮助文案。
    /// </summary>
    public static IReadOnlyList<string> Placeholders { get; } = new[]
    {
        "{name}",
        "{parent}",
        "{date}",
        "{datetime}",
        "{archive}",
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
        return TemplateLexing.TryTokenize(template, IsKnownToken, out _, out error);
    }

    /// <summary>
    /// 渲染根目录名。
    /// 模板为空/纯空白 → 返回 false、<paramref name="rootName"/> = null（未启用，调用方回落压缩包名）。
    /// 语法或结果校验失败 → 返回 false 并给出 <paramref name="error"/>。
    /// </summary>
    public static bool TryRender(
        string? template,
        RootNameTemplateContext ctx,
        out string? rootName,
        out FileNameTemplateError? error)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        rootName = null;

        if (string.IsNullOrWhiteSpace(template))
        {
            error = null;
            return false;
        }

        if (!TemplateLexing.TryTokenize(template, IsKnownToken, out var segments, out error)) return false;

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
        if (!TemplateLexing.TryValidateResult(rendered, MaxNameLength, out error)) return false;

        rootName = rendered;
        return true;
    }

    /// <summary>token 已转小写后传入。</summary>
    private static bool IsKnownToken(string token)
    {
        switch (token.ToLowerInvariant())
        {
            case "name":
            case "parent":
            case "date":
            case "datetime":
            case "archive":
                return true;
            default:
                return false;
        }
    }

    private static string Resolve(string token, RootNameTemplateContext ctx)
    {
        switch (token)
        {
            case "name":
                return ResolveSourceName(ctx);
            case "parent":
                return ctx.SourcePaths.Count == 0
                    ? string.Empty
                    : FileNameTemplate.GetParentName(ctx.SourcePaths[0]);
            case "date":
                return ctx.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            case "datetime":
                return ctx.Timestamp.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
            case "archive":
                return ctx.ArchiveName?.Trim() ?? string.Empty;
            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// <c>{name}</c>：单源 = 源主名；多源 = 公共父目录名；取不到才回落首源主名。
    /// </summary>
    private static string ResolveSourceName(RootNameTemplateContext ctx)
    {
        if (ctx.SourcePaths.Count == 0) return string.Empty;

        if (ctx.SourcePaths.Count > 1)
        {
            var auto = CompressPathPlanner.ResolveAutoName(ctx.SourcePaths);
            if (!string.IsNullOrWhiteSpace(auto)) return auto!;
        }

        return FileNameTemplate.GetPrimaryName(ctx.SourcePaths[0]);
    }
}
