using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MantisZip.Core.Abstractions;

namespace MantisZip.Core.Utils;

/// <summary>
/// 文字匹配模式
/// </summary>
public enum FilterMatchMode
{
    /// <summary>子串匹配（默认，当前行为）</summary>
    Substring,
    /// <summary>通配符（* = 任意字符序列，? = 单个字符）</summary>
    Wildcard,
}

/// <summary>
/// 文件列表多维度过滤条件
/// </summary>
public record SearchFilters
{
    /// <summary>文字搜索词（大小写不敏感子串匹配）</summary>
    public string? Text { get; init; }

    /// <summary>排除文字</summary>
    public string? ExcludeText { get; init; }

    /// <summary>匹配模式</summary>
    public FilterMatchMode MatchMode { get; init; }

    /// <summary>日期范围开始（含）</summary>
    public DateTime? DateFrom { get; init; }

    /// <summary>日期范围结束（含）</summary>
    public DateTime? DateTo { get; init; }

    /// <summary>大小下限（字节，含）</summary>
    public long? SizeMin { get; init; }

    /// <summary>大小上限（字节，含）</summary>
    public long? SizeMax { get; init; }
}

/// <summary>
/// 文件列表过滤引擎。提供静态方法用于对 ArchiveItem 列表应用组合过滤。
/// 纯数据层逻辑，不依赖 WPF UI 线程，可直接单元测试。
/// </summary>
public static class ArchiveFilter
{
    private static readonly ConcurrentDictionary<string, Regex> _regexCache = new();

    /// <summary>
    /// 对条目列表应用组合过滤（文字 + 日期 + 大小，AND 逻辑）。
    /// 所有过滤条件均为可选——为 null/空时跳过该维度。
    /// </summary>
    public static List<ArchiveItem> ApplyFilters(
        IReadOnlyList<ArchiveItem> items, SearchFilters filters)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        if (filters == null) throw new ArgumentNullException(nameof(filters));

        // 无过滤条件时快速返回
        if (string.IsNullOrEmpty(filters.Text)
            && string.IsNullOrEmpty(filters.ExcludeText)
            && filters.DateFrom == null
            && filters.DateTo == null
            && filters.SizeMin == null
            && filters.SizeMax == null)
        {
            return new List<ArchiveItem>(items);
        }

        return items.Where(item =>
        {
            // 文字过滤：根据匹配模式（子串/通配符）匹配
            if (!string.IsNullOrEmpty(filters.Text))
            {
                if (!MatchItem(item, filters.Text, filters.MatchMode))
                    return false;
            }

            // 排除过滤：使用相同匹配模式
            if (!string.IsNullOrEmpty(filters.ExcludeText))
            {
                if (MatchItem(item, filters.ExcludeText, filters.MatchMode))
                    return false;
            }

            // 日期过滤：比较 LastModified
            if (filters.DateFrom.HasValue && item.LastModified < filters.DateFrom.Value)
                return false;
            if (filters.DateTo.HasValue && item.LastModified > filters.DateTo.Value)
                return false;

            // 大小过滤：比较 Size
            if (filters.SizeMin.HasValue && item.Size < filters.SizeMin.Value)
                return false;
            if (filters.SizeMax.HasValue && item.Size > filters.SizeMax.Value)
                return false;

            return true;
        }).ToList();
    }

    private static bool MatchItem(ArchiveItem item, string pattern, FilterMatchMode mode)
    {
        if (mode == FilterMatchMode.Wildcard)
        {
            var regex = _regexCache.GetOrAdd(pattern, p =>
            {
                string regexPattern = "^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                return new Regex(regexPattern, RegexOptions.IgnoreCase);
            });
            return regex.IsMatch(item.Name) || regex.IsMatch(item.FullPath);
        }
        else // Substring
        {
            return item.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                || item.FullPath.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 将带单位的文本解析为字节数。输入错误时返回 null（不抛异常）。
    /// 支持的单位：B, KB, MB, GB（大小写不敏感）。
    /// </summary>
    public static long? ParseSizeWithUnit(string? text, string? unit)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (!double.TryParse(text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            return null;

        if (value < 0) return null;

        long multiplier = unit?.ToUpperInvariant() switch
        {
            "KB" => 1024L,
            "MB" => 1024L * 1024,
            "GB" => 1024L * 1024 * 1024,
            _ => 1L, // "B" 或未知单位均视为字节
        };

        return (long)(value * multiplier);
    }
}
