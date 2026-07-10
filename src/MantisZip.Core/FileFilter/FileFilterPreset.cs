using System;
using System.Collections.Generic;
using System.Linq;

namespace MantisZip.Core.FileFilter;

/// <summary>
/// 文件过滤预设。包含预设名称和过滤条件。
/// </summary>
public class FileFilterPreset
{
    /// <summary>预设名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>过滤条件</summary>
    public FileFilterCriteria Criteria { get; set; } = new();

    /// <summary>是否为内置预设（用户不可删除/修改）</summary>
    public bool IsBuiltIn { get; set; }

    public FileFilterPreset() { }

    public FileFilterPreset(string name, FileFilterCriteria criteria, bool isBuiltIn = false)
    {
        Name = name;
        Criteria = criteria ?? new FileFilterCriteria();
        IsBuiltIn = isBuiltIn;
    }

    /// <summary>
    /// 获取 8 个内置预设。
    /// </summary>
    public static List<FileFilterPreset> GetBuiltInPresets()
    {
        var now = DateTime.Now;
        return new List<FileFilterPreset>
        {
            new("📷 仅图片", new FileFilterCriteria
            {
                IncludeExtensions = new List<string> { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg" }
            }, isBuiltIn: true),

            new("🎵 仅音频", new FileFilterCriteria
            {
                IncludeExtensions = new List<string> { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma" }
            }, isBuiltIn: true),

            new("🎬 仅视频", new FileFilterCriteria
            {
                IncludeExtensions = new List<string> { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv" }
            }, isBuiltIn: true),

            new("📄 仅文档", new FileFilterCriteria
            {
                IncludeExtensions = new List<string> { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt" }
            }, isBuiltIn: true),

            new("🗜 仅压缩包", new FileFilterCriteria
            {
                IncludeExtensions = new List<string> { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz" }
            }, isBuiltIn: true),

            new("📦 大文件(>100MB)", new FileFilterCriteria
            {
                MinSize = 100L * 1024 * 1024
            }, isBuiltIn: true),

            new("📅 本月修改", new FileFilterCriteria
            {
                MinDate = new DateTime(now.Year, now.Month, 1)
            }, isBuiltIn: true),

            new("🗑 排除缓存/临时文件", new FileFilterCriteria
            {
                NamePattern = "*.tmp;*.cache;*.log;*.bak"
            }, isBuiltIn: true),
        };
    }
}
