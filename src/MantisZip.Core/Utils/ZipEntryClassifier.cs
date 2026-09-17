using System;
using System.Collections.Generic;

namespace MantisZip.Core.Utils;

/// <summary>
/// 按文件扩展名判断是否为「已压缩」文件，用于自适应压缩时自动 Store。
/// 已压缩文件即使再用 Deflate 压缩，体积也几乎不变（甚至变大），却要消耗 CPU 时间。
/// </summary>
internal static class ZipEntryClassifier
{
    /// <summary>
    /// 已压缩文件扩展名集合（不含点号，小写）。
    /// 覆盖：图片/音视频/字体/归档/压缩/已压缩文档格式。
    /// </summary>
    private static readonly HashSet<string> CompressedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── 图片 ──
        "jpg", "jpeg", "png", "gif", "bmp", "ico", "webp", "tiff", "tif",
        "heic", "heif", "avif", "jxl", "svg",
        // ── 音频 ──
        "mp3", "flac", "ogg", "opus", "aac", "m4a", "wma", "ape",
        // ── 视频 ──
        "mp4", "avi", "mkv", "mov", "wmv", "flv", "webm", "m4v", "mpg",
        "mpeg", "ts", "3gp",
        // ── 归档/压缩 ──
        "zip", "7z", "rar", "tar", "gz", "bz2", "xz", "lzma", "zst",
        "iso", "dmg", "cab",
        // ── 字体 ──
        "woff", "woff2", "ttf", "otf",
        // ── 文档（已压缩的 Office 格式）──
        "pdf", "docx", "xlsx", "pptx", "epub",
        // ── 其他 ──
        "jar", "war", "apk", "ipa",
    };

    /// <summary>
    /// 判断给定文件扩展名是否属于已压缩格式。
    /// </summary>
    /// <param name="extension">文件扩展名（含或不含点号均可）</param>
    /// <returns>true = 应 Store；false = 应压缩</returns>
    public static bool IsCompressed(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return false;
        // 统一去掉前导点号，转小写
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return CompressedExtensions.Contains(ext);
    }

    /// <summary>
    /// 根据自适应设置计算条目压缩级别。
    /// </summary>
    /// <param name="filePath">文件完整路径</param>
    /// <param name="userLevel">用户选定的压缩级别</param>
    /// <param name="adaptive">是否启用自适应</param>
    /// <returns>实际应使用的压缩级别（0 = Store）</returns>
    public static int GetAdaptiveLevel(string filePath, int userLevel, bool adaptive)
    {
        if (!adaptive) return userLevel;
        var ext = System.IO.Path.GetExtension(filePath);
        return IsCompressed(ext) ? 0 : userLevel;
    }
}
