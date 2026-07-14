using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MantisZip.Core.FileFilter;

/// <summary>
/// 文件过滤条件。四种维度（扩展名/文件名/大小/日期）均为可选，
/// 全部为空时 IsActive = false（不过滤）。
/// 多条件之间为 AND 逻辑。
/// </summary>
public class FileFilterCriteria
{
    /// <summary>包含的扩展名列表（如 ".mp3", ".wav"），空列表 = 包含全部。</summary>
    public List<string> IncludeExtensions { get; set; } = new();

    /// <summary>排除的扩展名列表，命中任意一个即排除。</summary>
    public List<string> ExcludeExtensions { get; set; } = new();

    /// <summary>
    /// 文件名通配符模式（如 "*报告*"），null 或空 = 不限制。
    /// 支持 *（任意多个字符）和 ?（单个字符）。
    /// </summary>
    public string? NamePattern { get; set; }

    /// <summary>最小文件大小（字节），null = 不限制。</summary>
    public long? MinSize { get; set; }

    /// <summary>最大文件大小（字节），null = 不限制。</summary>
    public long? MaxSize { get; set; }

    /// <summary>最小修改日期，null = 不限制。</summary>
    public DateTime? MinDate { get; set; }

    /// <summary>最大修改日期，null = 不限制。</summary>
    public DateTime? MaxDate { get; set; }

    /// <summary>
    /// 是否至少有一个过滤条件被设置。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsActive =>
        (IncludeExtensions.Count > 0) ||
        (ExcludeExtensions.Count > 0) ||
        !string.IsNullOrEmpty(NamePattern) ||
        MinSize.HasValue ||
        MaxSize.HasValue ||
        MinDate.HasValue ||
        MaxDate.HasValue;

    /// <summary>
    /// 人类可读的过滤条件摘要（用于 UI 显示）。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplaySummary
    {
        get
        {
            if (!IsActive) return "(无过滤)";

            var parts = new List<string>();

            if (IncludeExtensions.Count > 0)
                parts.Add($"扩展名: {string.Join(", ", IncludeExtensions)}");

            if (ExcludeExtensions.Count > 0)
                parts.Add($"排除: {string.Join(", ", ExcludeExtensions)}");

            if (!string.IsNullOrEmpty(NamePattern))
                parts.Add($"文件名: {NamePattern}");

            if (MinSize.HasValue && MaxSize.HasValue)
                parts.Add($"大小: {FormatSize(MinSize.Value)} ~ {FormatSize(MaxSize.Value)}");
            else if (MinSize.HasValue)
                parts.Add($"大小 ≥ {FormatSize(MinSize.Value)}");
            else if (MaxSize.HasValue)
                parts.Add($"大小 ≤ {FormatSize(MaxSize.Value)}");

            if (MinDate.HasValue && MaxDate.HasValue)
                parts.Add($"日期: {MinDate.Value:yyyy-MM-dd} ~ {MaxDate.Value:yyyy-MM-dd}");
            else if (MinDate.HasValue)
                parts.Add($"日期 ≥ {MinDate.Value:yyyy-MM-dd}");
            else if (MaxDate.HasValue)
                parts.Add($"日期 ≤ {MaxDate.Value:yyyy-MM-dd}");

            return string.Join(" | ", parts);
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1073741824L) return $"{bytes / 1073741824.0:F1} GB";
        if (bytes >= 1048576L) return $"{bytes / 1048576.0:F1} MB";
        if (bytes >= 1024L) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
}
