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
/// </summary>
public static class CompressPathPlanner
{
    /// <summary>
    /// 计算压缩包文件名（不含路径）。
    /// </summary>
    public static string ComputeArchiveName(string sourcePath, string format, bool keepOriginalExt)
    {
        string baseName;
        if (Directory.Exists(sourcePath))
            baseName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        else
            baseName = keepOriginalExt
                ? Path.GetFileName(sourcePath)
                : Path.GetFileNameWithoutExtension(sourcePath);

        string ext = string.Equals(format, "tar.gz", StringComparison.OrdinalIgnoreCase)
            ? ".tar.gz"
            : "." + format;
        return baseName + ext;
    }

    /// <summary>
    /// 计算输出路径（源 → 目标压缩包绝对路径）。目录源去尾分隔符再取父目录。
    /// </summary>
    public static string ComputeOutputPath(string sourcePath, string format, bool keepOriginalExt)
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

        return Path.Combine(parent, ComputeArchiveName(sourcePath, format, keepOriginalExt));
    }

    /// <summary>
    /// 批量规划 Separate 模式：每源一个输出压缩包（Bug 2 语义：目录源用完整目录名）。
    /// IncludedFiles 此时未知（需过滤枚举），由调用方后续填充。
    /// </summary>
    public static IReadOnlyList<CompressPlanItem> PlanSeparate(
        IReadOnlyList<string> sourcePaths, string format, bool keepOriginalExt)
    {
        return sourcePaths
            .Select(p => new CompressPlanItem(
                SourcePath: p,
                OutputArchivePath: ComputeOutputPath(p, format, keepOriginalExt),
                IncludedFiles: null))
            .ToList();
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
}
