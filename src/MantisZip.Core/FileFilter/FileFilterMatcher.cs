using System;
using System.IO;
using MantisZip.Core.Abstractions;

namespace MantisZip.Core.FileFilter;

/// <summary>
/// 文件过滤匹配器。纯函数 — 无文件 I/O 副作用（文件系统路径重载除外）。
/// 所有条件 AND 逻辑（全部满足才返回 true）。
/// </summary>
public static class FileFilterMatcher
{
    /// <summary>
    /// 对文件系统路径应用过滤条件。
    /// </summary>
    public static bool IsMatch(FileFilterCriteria filter, string filePath)
    {
        if (filter == null) throw new ArgumentNullException(nameof(filter));

        var fileName = Path.GetFileName(filePath);
        var ext = Path.GetExtension(fileName);

        // IncludeExtensions：命中任意一个才通过（空列表 = 全部通过）
        if (filter.IncludeExtensions.Count > 0)
        {
            if (!filter.IncludeExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        // ExcludeExtensions：命中任意一个即排除
        if (filter.ExcludeExtensions.Count > 0)
        {
            if (filter.ExcludeExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        // 文件名通配符
        if (!string.IsNullOrEmpty(filter.NamePattern))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            if (!MatchWildcard(nameWithoutExt, filter.NamePattern))
                return false;
        }

        // 文件大小
        var fileInfo = new FileInfo(filePath);
        if (filter.MinSize.HasValue && fileInfo.Length < filter.MinSize.Value)
            return false;
        if (filter.MaxSize.HasValue && fileInfo.Length > filter.MaxSize.Value)
            return false;

        // 修改日期
        if (filter.MinDate.HasValue && fileInfo.LastWriteTime < filter.MinDate.Value)
            return false;
        if (filter.MaxDate.HasValue && fileInfo.LastWriteTime > filter.MaxDate.Value)
            return false;

        return true;
    }

    /// <summary>
    /// 对 ArchiveItem 应用过滤条件。
    /// 跳过 IsDirectory 的条目（目录本身不过滤，只过滤文件）。
    /// </summary>
    public static bool IsMatch(FileFilterCriteria filter, ArchiveItem entry)
    {
        if (filter == null) throw new ArgumentNullException(nameof(filter));
        if (entry == null) throw new ArgumentNullException(nameof(entry));

        // 目录本身不过滤
        if (entry.IsDirectory) return true;

        var fileName = Path.GetFileName(entry.Name);
        var ext = Path.GetExtension(fileName);

        // IncludeExtensions
        if (filter.IncludeExtensions.Count > 0)
        {
            if (!filter.IncludeExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        // ExcludeExtensions
        if (filter.ExcludeExtensions.Count > 0)
        {
            if (filter.ExcludeExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        // 文件名通配符
        if (!string.IsNullOrEmpty(filter.NamePattern))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            if (!MatchWildcard(nameWithoutExt, filter.NamePattern))
                return false;
        }

        // 大小
        if (filter.MinSize.HasValue && entry.Size < filter.MinSize.Value)
            return false;
        if (filter.MaxSize.HasValue && entry.Size > filter.MaxSize.Value)
            return false;

        // 日期 — 跳过 MinValue（TarGz 可能缺失时间戳）
        if (entry.LastModified != DateTime.MinValue)
        {
            if (filter.MinDate.HasValue && entry.LastModified < filter.MinDate.Value)
                return false;
            if (filter.MaxDate.HasValue && entry.LastModified > filter.MaxDate.Value)
                return false;
        }

        return true;
    }

    /// <summary>
    /// 简易通配符匹配（支持 * 和 ?）。
    /// * 匹配任意多个字符（含零个），? 匹配单个字符。
    /// </summary>
    internal static bool MatchWildcard(string input, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return true;
        if (string.IsNullOrEmpty(input))
            return pattern == "*" || pattern == string.Empty;

        int pi = 0, si = 0;
        int starIdx = -1, matchIdx = -1;

        while (si < input.Length)
        {
            if (pi < pattern.Length && (pattern[pi] == '?' || pattern[pi] == input[si]))
            {
                pi++;
                si++;
            }
            else if (pi < pattern.Length && pattern[pi] == '*')
            {
                starIdx = pi;
                matchIdx = si;
                pi++;
            }
            else if (starIdx != -1)
            {
                pi = starIdx + 1;
                matchIdx++;
                si = matchIdx;
            }
            else
            {
                return false;
            }
        }

        while (pi < pattern.Length && pattern[pi] == '*')
            pi++;

        return pi == pattern.Length;
    }
}
