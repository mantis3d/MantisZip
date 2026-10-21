using System;
using System.Collections.Generic;
using System.IO;

namespace MantisZip.Core.Utils;

/// <summary>
/// 「已压缩文件」判定的**唯一事实源（SSOT）**（adaptive-compress-ui-convergence Phase 2 / Task 9-10）。
///
/// 判定口径（2026-10-20 用户拍板）：
/// ① **扩展名集合**（<see cref="IsCompressed"/>）——内置「内容本身已是压缩格式」的扩展名；
/// ② **魔数检测**（<see cref="IsCompressedByMagic"/>）——扩展名骗人 / 无扩展名时的兜底判定。
///
/// 边界约定（与默认自适应规则集保持一致）：
/// - **不算已压缩**：<c>svg</c>（XML 文本）、<c>tar</c>（未压缩归档）、<c>wav</c>（PCM 未压缩）、
///   <c>bmp/tga/hdr/exr</c>（未压缩位图）——这些用 Deflate 仍有可观收益；
/// - **算已压缩**：有损/无损压缩图像、音视频压缩流、压缩包、字体（表已压缩）、Office/PDF（内部即 zip 流）。
///
/// 决策入口统一走 <see cref="AdaptiveRuleMatcher.ResolveLevel"/>，本类只提供判定原语，
/// 避免「分拣路径」与「写入路径」出现两套不同判定（历史事故：分拣说 Store、写入却 Deflate）。
/// </summary>
internal static class ZipEntryClassifier
{
    /// <summary>
    /// 已压缩文件扩展名集合（不含点号，小写）——SSOT 扩展名部分。
    /// </summary>
    private static readonly HashSet<string> CompressedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── 图片（内容已压缩）──
        "jpg", "jpeg", "png", "gif", "webp", "ico", "heic", "heif", "avif", "jxl",
        // ── 音频（压缩流）──
        "mp3", "flac", "ogg", "opus", "aac", "m4a", "wma", "ape",
        // ── 视频（压缩流）──
        "mp4", "avi", "mkv", "mov", "wmv", "flv", "webm", "m4v", "mpg", "mpeg", "ts", "3gp",
        // ── 归档/压缩 ──
        "zip", "7z", "rar", "gz", "bz2", "xz", "lzma", "zst", "iso", "dmg", "cab",
        // ── 字体（表数据通常已压缩）──
        "woff", "woff2", "ttf", "otf",
        // ── 文档（Office/PDF/EPUB 内部即压缩流）──
        "pdf", "docx", "xlsx", "pptx", "epub",
        // ── 其他（已压缩容器）──
        "jar", "war", "apk", "ipa",
    };

    /// <summary>
    /// 魔数检测的文件大小阈值（字节）——小于该值的文件不读头（收益低、IO 成本相对高）。
    /// </summary>
    internal const long MagicDetectMinBytes = 64L * 1024;

    /// <summary>读头长度：满足 <see cref="FileFormatDetector.Detect"/> 最低要求（≥64B）且覆盖常见魔数。</summary>
    private const int MagicHeadBytes = 4096;

    /// <summary>
    /// 判断给定文件扩展名是否属于已压缩格式（SSOT 扩展名部分）。
    /// </summary>
    /// <param name="extension">文件扩展名（含或不含点号均可）</param>
    /// <returns>true = 建议 Store（内容已压缩）；false = 应压缩</returns>
    public static bool IsCompressed(string? extension)
    {
        if (string.IsNullOrEmpty(extension)) return false;
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return CompressedExtensions.Contains(ext);
    }

    /// <summary>
    /// 判断魔数检测到的格式是否为「内容已压缩」格式（SSOT 魔数部分）。
    /// </summary>
    public static bool IsCompressedFormat(FileFormat format) => format switch
    {
        // 图像（压缩位图）
        FileFormat.Jpeg or FileFormat.Png or FileFormat.Apng or FileFormat.Gif
            or FileFormat.WebP or FileFormat.Avif or FileFormat.Ico => true,
        // 音视频（压缩流）
        FileFormat.Mp3 or FileFormat.Flac or FileFormat.Ogg
            or FileFormat.Mp4 or FileFormat.Mkv or FileFormat.WebM
            or FileFormat.Wmv or FileFormat.Mov or FileFormat.Avi or FileFormat.Flv => true,
        // 压缩包
        FileFormat.Zip or FileFormat.SevenZip or FileFormat.Rar
            or FileFormat.Gz or FileFormat.Bz2 or FileFormat.Xz or FileFormat.Zstd or FileFormat.Iso => true,
        // 字体（表已压缩）
        FileFormat.Ttf or FileFormat.Otf or FileFormat.Woff or FileFormat.Woff2 => true,
        // 文档（内部即压缩流）
        FileFormat.Pdf or FileFormat.Docx or FileFormat.Xlsx or FileFormat.Pptx or FileFormat.Epub => true,
        _ => false,
    };

    /// <summary>
    /// 魔数检测：读取文件头判断内容是否已是压缩格式（扩展名骗人 / 无扩展名时的兜底）。
    ///
    /// 约束（Task 9）：
    /// - 仅在**文件大小 ≥ <see cref="MagicDetectMinBytes"/>** 时读头（分拣是单线程预扫，万级文件串行读头会明显拖慢）；
    /// - 任何 IO 异常（被占用/权限不足/已删除）→ 返回 false 回落扩展名规则，**不中断压缩**；
    /// - 只读前 4KB，不读全文。
    /// </summary>
    public static bool IsCompressedByMagic(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length < MagicDetectMinBytes) return false;

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var head = new byte[MagicHeadBytes];
            int read = fs.Read(head, 0, head.Length);
            if (read < 64) return false; // 太短，无法可靠判定

            var format = FileFormatDetector.Detect(head, read);
            return IsCompressedFormat(format);
        }
        catch
        {
            // IO 异常 → 视为未命中（回落扩展名规则）
            return false;
        }
    }

    /// <summary>
    /// 判断扩展名是否命中「用户自定义仅存储格式」列表（经 <see cref="Services.FormatCatalog"/> 查扩展名）。
    /// </summary>
    public static bool IsCustomStoreFormat(string? extension, IEnumerable<string>? formatIds,
        List<Models.FormatDefinition>? customFormats)
    {
        if (string.IsNullOrEmpty(extension) || formatIds == null) return false;
        var ids = formatIds as IList<string> ?? new List<string>(formatIds);
        if (ids.Count == 0) return false;

        var allFormats = Services.FormatCatalog.GetAll(customFormats);
        foreach (var formatId in ids)
        {
            var format = allFormats.FirstOrDefault(f => f.Id == formatId);
            if (format != null && format.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
