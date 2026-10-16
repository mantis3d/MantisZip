using MantisZip.Core.Models;
using MantisZip.Core.Utils;

namespace MantisZip.Core.Services;

/// <summary>
/// 格式目录 — 管理内置 + 自定义格式定义，提供按 ID/扩展名/魔数查询。
/// 内置格式从 FileFormat 枚举生成，覆盖所有已知格式。
/// </summary>
public static class FormatCatalog
{
    private static readonly List<FormatDefinition> _builtInFormats = new();

    static FormatCatalog()
    {
        InitializeBuiltInFormats();
    }

    private static void InitializeBuiltInFormats()
    {
        // 从 FileFormat 枚举生成内置格式定义
        var formatMap = new Dictionary<string, (string displayName, string[] extensions)>
        {
            // 图像
            ["Jpeg"] = ("JPEG 图片", new[] { ".jpg", ".jpeg", ".jpe", ".jfif" }),
            ["Png"] = ("PNG 图片", new[] { ".png" }),
            ["Gif"] = ("GIF 图片", new[] { ".gif" }),
            ["Bmp"] = ("BMP 图片", new[] { ".bmp" }),
            ["WebP"] = ("WebP 图片", new[] { ".webp" }),
            ["Ico"] = ("ICO 图标", new[] { ".ico" }),
            ["Tga"] = ("TGA 图片", new[] { ".tga" }),
            ["Hdr"] = ("HDR 图片", new[] { ".hdr" }),
            ["Exr"] = ("EXR 图片", new[] { ".exr" }),
            ["Svg"] = ("SVG 矢量图", new[] { ".svg" }),

            // 视频
            ["Mp4"] = ("MP4 视频", new[] { ".mp4", ".m4v" }),
            ["Mkv"] = ("MKV 视频", new[] { ".mkv" }),
            ["WebM"] = ("WebM 视频", new[] { ".webm" }),
            ["Wmv"] = ("WMV 视频", new[] { ".wmv" }),
            ["Mov"] = ("MOV 视频", new[] { ".mov" }),
            ["Avi"] = ("AVI 视频", new[] { ".avi" }),
            ["Flv"] = ("FLV 视频", new[] { ".flv" }),

            // 音频
            ["Mp3"] = ("MP3 音频", new[] { ".mp3" }),
            ["Wav"] = ("WAV 音频", new[] { ".wav" }),
            ["Flac"] = ("FLAC 音频", new[] { ".flac" }),
            ["Ogg"] = ("OGG 音频", new[] { ".ogg" }),

            // 压缩包
            ["Zip"] = ("ZIP 压缩包", new[] { ".zip" }),
            ["SevenZip"] = ("7z 压缩包", new[] { ".7z" }),
            ["Rar"] = ("RAR 压缩包", new[] { ".rar" }),
            ["Tar"] = ("TAR 归档", new[] { ".tar" }),
            ["Gz"] = ("GZip 压缩包", new[] { ".gz", ".tgz" }),
            ["Bz2"] = ("BZip2 压缩包", new[] { ".bz2" }),
            ["Xz"] = ("XZ 压缩包", new[] { ".xz" }),
            ["Zstd"] = ("Zstd 压缩包", new[] { ".zst", ".zstd" }),
            ["Iso"] = ("ISO 映像", new[] { ".iso" }),

            // 文档
            ["Pdf"] = ("PDF 文档", new[] { ".pdf" }),
            ["Docx"] = ("Word 文档", new[] { ".docx" }),
            ["Xlsx"] = ("Excel 表格", new[] { ".xlsx" }),
            ["Pptx"] = ("PowerPoint 演示", new[] { ".pptx" }),
            ["Epub"] = ("EPUB 电子书", new[] { ".epub" }),

            // 可执行
            ["Pe"] = ("PE 可执行文件", new[] { ".exe", ".dll", ".sys" }),
            ["Elf"] = ("ELF 可执行文件", new[] { ".so", ".elf" }),

            // 字体
            ["Ttf"] = ("TrueType 字体", new[] { ".ttf" }),
            ["Otf"] = ("OpenType 字体", new[] { ".otf" }),
            ["Woff"] = ("WOFF 字体", new[] { ".woff" }),
            ["Woff2"] = ("WOFF2 字体", new[] { ".woff2" }),

            // 数据库
            ["Sqlite"] = ("SQLite 数据库", new[] { ".sqlite", ".db" }),

            // 其他
            ["Torrent"] = ("BT 种子", new[] { ".torrent" }),
            ["Stl"] = ("STL 3D 模型", new[] { ".stl" }),
            ["Dxf"] = ("DXF 图纸", new[] { ".dxf" }),
        };

        foreach (var (id, (displayName, extensions)) in formatMap)
        {
            _builtInFormats.Add(new FormatDefinition
            {
                Id = id,
                DisplayName = displayName,
                Extensions = extensions.ToList(),
                IsBuiltIn = true,
            });
        }
    }

    /// <summary>获取所有格式（内置 + 自定义）。</summary>
    /// <param name="customFormats">用户自定义格式列表，可为 null。</param>
    /// <returns>合并后的格式列表。</returns>
    public static IReadOnlyList<FormatDefinition> GetAll(List<FormatDefinition>? customFormats = null)
    {
        var result = new List<FormatDefinition>(_builtInFormats);
        if (customFormats != null)
            result.AddRange(customFormats.Where(f => !f.IsBuiltIn));
        return result;
    }

    /// <summary>按 ID 获取格式。</summary>
    /// <param name="id">格式 ID，如 "Jpeg"。</param>
    /// <param name="customFormats">用户自定义格式列表，可为 null。</param>
    /// <returns>匹配的格式定义，未找到返回 null。</returns>
    public static FormatDefinition? GetById(string id, List<FormatDefinition>? customFormats = null)
    {
        var all = GetAll(customFormats);
        return all.FirstOrDefault(f => f!.Id == id, null);
    }

    /// <summary>按扩展名获取格式。</summary>
    /// <param name="extension">扩展名（如 ".jpg"），大小写不敏感。</param>
    /// <param name="customFormats">用户自定义格式列表，可为 null。</param>
    /// <returns>匹配的格式定义，未找到返回 null。</returns>
    public static FormatDefinition? GetByExtension(string extension, List<FormatDefinition>? customFormats = null)
    {
        var all = GetAll(customFormats);
        return all.FirstOrDefault(f => f!.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase), null);
    }
}
