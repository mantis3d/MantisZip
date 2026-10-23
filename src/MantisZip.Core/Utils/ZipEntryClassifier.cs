using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MantisZip.Core.Models;

namespace MantisZip.Core.Utils;

/// <summary>
/// 「已压缩文件」判定的**唯一事实源（SSOT）**（adaptive-compress-ui-convergence Phase 2 / Task 9-10）。
///
/// 判定口径（2026-10-20 用户拍板）：
/// ① **扩展名集合**（<see cref="IsCompressed"/>）——内置「内容本身已是压缩格式」的扩展名；
/// ② **魔数检测**（<see cref="IsCompressedByMagic"/>）——扩展名骗人 / 无扩展名时的兜底判定。
///
/// 边界约定：
/// - **不算已压缩**（SSOT 判定）：<c>svg</c>（XML 文本）、<c>tar</c>（未压缩归档）、<c>wav</c>（PCM 未压缩）、
///   <c>bmp/tga/hdr/exr</c>（未压缩位图）——这些用 Deflate 仍有可观收益；
/// - **算已压缩**：有损/无损压缩图像、音视频压缩流、压缩包、字体（表已压缩）、Office/PDF（内部即 zip 流）。
///
/// ⚠ 例外（规则层覆盖 SSOT）：默认「图片类」自适应规则仍包含 <c>Bmp</c>（2026-10-21 合并时保留旧默认行为），
/// 因此默认配置下 BMP 实际走 Store。这是**规则层的历史兼容选择**，不是 SSOT 判定本身；
/// 用户可在设置 → 加速 → 自适应规则中移除 Bmp 以恢复压缩。
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
    /// 读取文件头用于魔数判定（一次读头供「自定义格式魔数」与「内置已压缩格式」共用，
    /// 避免同一文件读两次头）。
    ///
    /// 约束（Task 9）：
    /// - 仅在**文件大小 ≥ <see cref="MagicDetectMinBytes"/>** 时读头（分拣是单线程预扫，万级文件串行读头会明显拖慢）；
    /// - 任何 IO 异常（被占用/权限不足/已删除）→ 返回 false 回落扩展名规则，**不中断压缩**；
    /// - 只读前 4KB，不读全文。
    /// </summary>
    /// <param name="filePath">源文件路径。</param>
    /// <param name="head">读到的头部字节（长度固定为 <see cref="MagicHeadBytes"/> 的缓冲区，实际有效长度为 <paramref name="length"/>）。</param>
    /// <param name="length">实际读到的有效字节数。</param>
    /// <returns>true = 读到可用头部；false = 文件不存在/过小/读失败。</returns>
    public static bool TryReadHead(string filePath, out byte[] head, out int length)
    {
        head = Array.Empty<byte>();
        length = 0;
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length < MagicDetectMinBytes) return false;

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[MagicHeadBytes];
            int read = fs.Read(buffer, 0, buffer.Length);
            if (read < 64) return false; // 太短，无法可靠判定

            head = buffer;
            length = read;
            return true;
        }
        catch
        {
            // IO 异常 → 视为未命中（回落扩展名规则）
            return false;
        }
    }

    /// <summary>
    /// 魔数检测：读取文件头判断内容是否已是压缩格式（扩展名骗人 / 无扩展名时的兜底）。
    /// 只判定内置「已压缩格式」集合；自定义格式魔数请用
    /// <see cref="MatchCustomFormatByMagic"/> 结合用户规则判定。
    /// </summary>
    public static bool IsCompressedByMagic(string filePath)
    {
        if (!TryReadHead(filePath, out var head, out var length)) return false;
        return IsCompressedFormat(FileFormatDetector.Detect(head, length));
    }

    /// <summary>
    /// 用「格式目录」里自定义格式的 <see cref="FormatDefinition.MagicHex"/> 匹配文件头，
    /// 返回命中的格式 ID（供 <see cref="AdaptiveRuleMatcher.ResolveLevel"/> 继续查用户规则）；
    /// 未命中返回 null。魔数 hex 支持空格 / 中划线 / 冒号分隔与 <c>0x</c> 前缀，非法值按未命中处理。
    /// </summary>
    /// <param name="head">文件头有效字节。</param>
    /// <param name="customFormats">用户自定义格式列表（内置格式没有 MagicHex，会被自然跳过）。</param>
    public static string? MatchCustomFormatByMagic(ReadOnlySpan<byte> head, List<FormatDefinition>? customFormats)
    {
        if (customFormats == null || head.IsEmpty) return null;

        foreach (var format in customFormats)
        {
            if (string.IsNullOrWhiteSpace(format.MagicHex)) continue;
            if (HeadStartsWithMagic(head, format.MagicHex!)) return format.Id;
        }
        return null;
    }

    /// <summary>判断文件头是否以指定魔数 hex 开头（前缀匹配）。</summary>
    internal static bool HeadStartsWithMagic(ReadOnlySpan<byte> head, string magicHex)
    {
        var cleaned = magicHex.Trim()
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace(":", "", StringComparison.Ordinal);
        if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[2..];

        if (cleaned.Length == 0 || cleaned.Length % 2 != 0) return false;
        int needed = cleaned.Length / 2;
        if (head.Length < needed) return false;

        for (int i = 0; i < needed; i++)
        {
            if (!byte.TryParse(cleaned.AsSpan(i * 2, 2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var b))
                return false;
            if (head[i] != b) return false;
        }
        return true;
    }
}
