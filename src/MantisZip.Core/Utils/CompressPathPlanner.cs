using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 压缩路径规划器（Single Source of Truth）。
/// 输出压缩包路径/文件名的**唯一**实现——预览（ResultPreviewService）与实际压缩
/// （CompressService）均消费本类，杜绝两侧各算一套导致的路径漂移。
///
/// 语义（与历史实现逐字一致，仅收敛为单一实现）：
/// - 目录源：用完整目录名（忽略 keepOriginalExt）
/// - 文件源：keepOriginalExt 为 true 保留扩展名，否则去掉
/// - 扩展名：tar.gz 双段，其余单段
///
/// 文件名模板（2026-11-11 起）：所有命名入口都能带一个可选 <c>template</c>。
/// 模板非空且渲染成功 ⇒ 用它渲染的主名 + 格式扩展名（**忽略 keepOriginalExt**，
/// 要保留原扩展名请写 <c>{ext}</c>）；模板为空或渲染失败 ⇒ 一律回退上面的旧逻辑。
/// 渲染失败静默回退是刻意的：预览树不能因为用户正在敲半截 <c>{na</c> 就崩/空，
/// 错误提示由调用方（VM）单独显示。
/// </summary>
public static class CompressPathPlanner
{
    /// <summary>格式对应的归档扩展名（含点）。<c>tar.gz</c> 是双段。</summary>
    public static string GetFormatExtension(string format)
        => string.Equals(format, "tar.gz", StringComparison.OrdinalIgnoreCase)
            ? ".tar.gz"
            : "." + format;

    /// <summary>
    /// 计算压缩包文件名（不含路径）。
    /// </summary>
    /// <param name="template">文件名模板；null/空白 = 未启用（走旧命名）。</param>
    /// <param name="sequence">1 基序号（<c>{seq}</c> 用）。</param>
    /// <param name="timestamp">批次时间戳（<c>{date}</c>/<c>{datetime}</c> 用）；null = 当前时刻。</param>
    /// <param name="autoName"><c>{name:auto}</c> 的取值；null/空 = 与 <c>{name}</c> 相同。</param>
    public static string ComputeArchiveName(
        string sourcePath, string format, bool keepOriginalExt,
        string? template = null, int sequence = 1, DateTime? timestamp = null,
        string? autoName = null)
    {
        if (TryRenderBaseName(sourcePath, template, sequence, timestamp, autoName, out var baseName))
            return baseName + GetFormatExtension(format);

        // 旧逻辑原样保留（模板为空、或渲染失败时的兜底）
        string legacyBaseName;
        if (Directory.Exists(sourcePath))
            legacyBaseName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        else
            legacyBaseName = keepOriginalExt
                ? Path.GetFileName(sourcePath)
                : Path.GetFileNameWithoutExtension(sourcePath);

        return legacyBaseName + GetFormatExtension(format);
    }

    /// <summary>
    /// 计算输出路径（源 → 目标压缩包绝对路径）。目录源去尾分隔符再取父目录。
    /// </summary>
    public static string ComputeOutputPath(
        string sourcePath, string format, bool keepOriginalExt,
        string? template = null, int sequence = 1, DateTime? timestamp = null,
        string? autoName = null)
    {
        string parent;
        if (Directory.Exists(sourcePath))
        {
            parent = Path.GetDirectoryName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                     ?? ".";
        }
        else
        {
            parent = Path.GetDirectoryName(sourcePath) ?? ".";
        }

        return Path.Combine(
            parent,
            ComputeArchiveName(sourcePath, format, keepOriginalExt, template, sequence, timestamp, autoName));
    }

    /// <summary>
    /// 单包多源（Manual / Combined / --compress-quick）的压缩包名。
    /// <c>{name}</c> 取**第一个**源；<c>{name:auto}</c> 取公共父目录名（取不到 → 第一个源的主名）。
    /// 模板为空或渲染失败 ⇒ 返回 null，调用方仍走各自的旧命名逻辑（**不猜**）。
    /// </summary>
    public static string? ComputeSingleArchiveName(
        IReadOnlyList<string> sourcePaths, string format,
        string? template = null, DateTime? timestamp = null)
    {
        if (sourcePaths.Count == 0) return null;
        if (!TryRenderBaseName(
                sourcePaths[0], template, 1, timestamp, ResolveAutoName(sourcePaths), out var baseName))
        {
            return null;
        }

        return baseName + GetFormatExtension(format);
    }

    /// <summary>
    /// 公共父目录名（<c>{name:auto}</c> 的取值）。
    /// 单源、跨盘、盘符根、取不到 → null（调用方回退第一个源的主名）。
    /// </summary>
    public static string? ResolveAutoName(IReadOnlyList<string> sourcePaths)
    {
        if (sourcePaths.Count < 2) return null;

        var common = PathCommonality.FindCommonParent(sourcePaths);
        if (common == null || PathCommonality.IsDriveRoot(common)) return null;

        var name = Path.GetFileName(common.TrimEnd('\\', '/'));
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    /// 批量规划 Separate 模式：每源一个输出压缩包（Bug 2 语义：目录源用完整目录名）。
    /// IncludedFiles 此时未知（需过滤枚举），由调用方后续填充。
    /// 模板里的 <c>{seq}</c> 取「源在列表中的 1 基下标」；每个包只含 1 个源 ⇒ <c>{name:auto}</c> 恒等于 <c>{name}</c>。
    /// </summary>
    public static IReadOnlyList<CompressPlanItem> PlanSeparate(
        IReadOnlyList<string> sourcePaths, string format, bool keepOriginalExt,
        string? template = null, DateTime? timestamp = null)
    {
        var list = new List<CompressPlanItem>(sourcePaths.Count);
        for (int i = 0; i < sourcePaths.Count; i++)
        {
            var p = sourcePaths[i];
            list.Add(new CompressPlanItem(
                SourcePath: p,
                OutputArchivePath: ComputeOutputPath(
                    p, format, keepOriginalExt, template, sequence: i + 1, timestamp: timestamp),
                IncludedFiles: null));
        }
        return list;
    }

    /// <summary>
    /// 批量规划 Manual/Combined 模式：单输出包 + 全部源（共享同一 OutputArchivePath）。
    /// </summary>
    public static IReadOnlyList<CompressPlanItem> PlanSingle(
        IReadOnlyList<string> sourcePaths, string outputPath, string format)
    {
        return sourcePaths
            .Select(p => new CompressPlanItem(
                SourcePath: p,
                OutputArchivePath: outputPath,
                IncludedFiles: null))
            .ToList();
    }

    /// <summary>
    /// 模板渲染的统一入口：成功 ⇒ <paramref name="baseName"/> 为主名并返回 true；
    /// 模板为空 / 渲染失败 ⇒ 返回 false（调用方回退旧命名）。
    /// </summary>
    private static bool TryRenderBaseName(
        string sourcePath, string? template, int sequence, DateTime? timestamp,
        string? autoName, out string baseName)
    {
        baseName = string.Empty;
        if (string.IsNullOrWhiteSpace(template)) return false;

        var ctx = new FileNameTemplateContext(
            sourcePath, sequence, timestamp ?? DateTime.Now, autoName);
        if (!FileNameTemplate.TryRender(template, ctx, out var rendered, out _)) return false;
        if (string.IsNullOrEmpty(rendered)) return false;

        baseName = rendered;
        return true;
    }
}
