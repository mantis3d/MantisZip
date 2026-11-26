using System;
using System.IO;
using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 解压侧「条目名 → 目标绝对路径」的唯一换算入口：先按 <see cref="ArchiveOptions.StripRootDirectory"/> /
/// <see cref="ArchiveOptions.AddRootDirectory"/> 改写条目名，再交给
/// <see cref="FileConflictHelper.GetSafePath"/> 做防 Zip Slip 的落点校验。
/// <para>
/// <b>为什么必须只有这一处</b>：三个引擎各有 2–4 处「算落点」的代码（文件、目录、并行批、预览），
/// 每处都手写一次 <c>GetSafePath(destinationPath, key)</c>。剥根/加根如果散在十几处各改一遍，
/// 一定会漏掉其中一两处 —— 表现为「大部分文件少了一层，个别还是老路径」，比不实现还糟。
/// </para>
/// <para>
/// 返回 <c>null</c> 表示该条目**应当被丢弃**：典型是「剥根」时那个根目录条目本身
/// （<c>7zip/</c> 剥完什么都不剩），不能再在目标目录里建出一个空壳目录。
/// </para>
/// </summary>
public static class EntryOutputPath
{
    /// <summary>
    /// 计算条目的目标绝对路径。
    /// </summary>
    /// <param name="destinationPath">解压目标目录。</param>
    /// <param name="entryName">包内条目名（<c>/</c> 或 <c>\</c> 分隔均可）。</param>
    /// <param name="options">解压选项；为 null 时等同「不剥不加」（与引入本功能前逐字节一致）。</param>
    /// <returns>目标绝对路径；该条目应被丢弃时返回 null。</returns>
    public static string? Resolve(string destinationPath, string entryName, ArchiveOptions? options)
    {
        var adjusted = ArchiveRootPolicy.Apply(
            entryName,
            options?.StripRootDirectory,
            options?.AddRootDirectory);

        if (adjusted == null)
            return null;

        return FileConflictHelper.GetSafePath(destinationPath, adjusted);
    }

    /// <summary>
    /// 只算条目在目标目录下的**相对**路径（改写 + 净化，不含目标目录前缀）。
    /// 供预览树与 <see cref="ExtractPathResolver"/> 使用，保证「预览 = 实际」。
    /// </summary>
    public static string? ResolveRelative(string entryName, ArchiveOptions? options)
    {
        var adjusted = ArchiveRootPolicy.Apply(
            entryName,
            options?.StripRootDirectory,
            options?.AddRootDirectory);

        return adjusted == null ? null : FileConflictHelper.SanitizeEntryPath(adjusted);
    }

    /// <summary>
    /// 判断某个条目在剥根后是否还有内容（用于「目录条目要不要建」）。
    /// 与 <see cref="Resolve"/> 返回 null 判据完全一致，这里单独暴露只为读起来清楚。
    /// </summary>
    public static bool IsDropped(string entryName, ArchiveOptions? options)
        => Resolve(destinationPath: Path.GetTempPath(), entryName, options) == null;

    /// <summary>
    /// 取本次解压的「有效剥根段」：<see cref="ArchiveOptions.StripRootDirectory"/> 去空白后的值，
    /// 空/空白返回 null。集中一处，避免各引擎对「空字符串算不算启用」判断不一致。
    /// </summary>
    public static string? EffectiveStripRoot(ArchiveOptions? options)
    {
        var value = options?.StripRootDirectory;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// 取本次解压的「有效加根段」，语义同 <see cref="EffectiveStripRoot"/>。
    /// </summary>
    public static string? EffectiveAddRoot(ArchiveOptions? options)
    {
        var value = options?.AddRootDirectory;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
