using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;

namespace MantisZip.UI.Avalonia.Services;

/// <summary>
/// 预览格式分类。
/// </summary>
public enum PreviewType
{
    None,
    Text,
    Csv,
    Pe,
    Image,
    Gif,
    Svg,
    Font,
    Audio,
    Sqlite,
    Iso,
    Torrent,
    Office,
    Docx,
    Xlsx,
    Pptx,
    Video,
    Html,
    Markdown,
    Pdf,
    IcoGallery,
    Unsupported
}

/// <summary>
/// 预览服务：临时文件提取 + 格式分类。
/// </summary>
public class PreviewService
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".ini", ".cfg", ".conf", ".xml", ".json",
        ".cs", ".csproj", ".yaml", ".yml", ".toml",
        ".sh", ".bat", ".cmd", ".ps1", ".py", ".js", ".ts", ".tsx",
        ".css", ".scss", ".less",
        ".sql", ".gitignore", ".editorconfig", ".sln", ".props", ".targets",
        ".ruleset", ".rc", ".resx", ".nuspec", ".gradle", ".dockerfile",
        ".env", ".h", ".c", ".cpp", ".hpp",
        ".swift", ".kt", ".java", ".rb", ".go", ".rs", ".php", ".vue"
    };

    private static readonly HashSet<string> CsvExtensions = new(StringComparer.OrdinalIgnoreCase) { ".csv" };
    private static readonly HashSet<string> PeExtensions = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".dll", ".sys", ".ocx" };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".ico", ".webp"
    };

    private static readonly HashSet<string> GifExtensions = new(StringComparer.OrdinalIgnoreCase) { ".gif" };
    private static readonly HashSet<string> SvgExtensions = new(StringComparer.OrdinalIgnoreCase) { ".svg" };
    private static readonly HashSet<string> FontExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ttf", ".otf", ".woff", ".woff2", ".eot"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".flac", ".mp3", ".ogg", ".aac", ".wma", ".m4a"
    };

    private static readonly HashSet<string> SqliteExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sqlite", ".sqlite3", ".db", ".db3"
    };

    private static readonly HashSet<string> IsoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".iso"
    };

    private static readonly HashSet<string> TorrentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".torrent"
    };

    private static readonly HashSet<string> OfficeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".xlsx", ".pptx"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm"
    };

    private static readonly HashSet<string> HtmlExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm"
    };

    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".mdown"
    };

    private const long MaxPreviewFileSize = 50 * 1024 * 1024; // 50 MB
    private const long MaxTextPreviewBytes = 1 * 1024 * 1024;  // 1 MB

    /// <summary>
    /// 是否启用格式检测（魔数识别）。由 App.axaml.cs 在启动时从 AppSettings 设置。
    /// 启用后，ClassifyPreviewByMagicAsync 会通过魔数判断文件真实格式。
    /// </summary>
    public static bool EnableFormatDetection { get; set; } = true;

    /// <summary>
    /// 格式检测时读取的文件头部字节数。由 App.axaml.cs 在启动时从 AppSettings 设置。
    /// </summary>
    public static int PreviewHeadSize { get; set; } = 4096;

    /// <summary>
    /// 根据文件扩展名判断预览类型。
    /// 魔数检测由 <see cref="ClassifyPreviewByMagicAsync"/> 另行处理。
    /// </summary>
    public static PreviewType ClassifyPreview(string ext)
    {
        if (TextExtensions.Contains(ext)) return PreviewType.Text;
        if (CsvExtensions.Contains(ext)) return PreviewType.Csv;
        if (PeExtensions.Contains(ext)) return PreviewType.Pe;
        if (ImageExtensions.Contains(ext)) return PreviewType.Image;
        if (GifExtensions.Contains(ext)) return PreviewType.Gif;
        if (SvgExtensions.Contains(ext)) return PreviewType.Svg;
        if (FontExtensions.Contains(ext)) return PreviewType.Font;
        if (AudioExtensions.Contains(ext)) return PreviewType.Audio;
        if (SqliteExtensions.Contains(ext)) return PreviewType.Sqlite;
        if (IsoExtensions.Contains(ext)) return PreviewType.Iso;
        if (TorrentExtensions.Contains(ext)) return PreviewType.Torrent;
        if (ext == ".docx") return PreviewType.Docx;
        if (ext == ".xlsx") return PreviewType.Xlsx;
        if (ext == ".pptx") return PreviewType.Pptx;
        if (VideoExtensions.Contains(ext)) return PreviewType.Video;
        if (HtmlExtensions.Contains(ext)) return PreviewType.Html;
        if (MarkdownExtensions.Contains(ext)) return PreviewType.Markdown;
        return PreviewType.Unsupported;
    }

    /// <summary>
    /// 通过魔数检测文件真实格式。读取文件头部字节，调用 FileFormatDetector.Detect。
    /// 返回 (PreviewType, FileFormat, string? displayName)。
    /// 如果魔数无法识别，返回 (PreviewType.Unsupported, FileFormat.Unknown, null)。
    /// </summary>
    public static async Task<(PreviewType type, FileFormat format, string? displayName)> ClassifyPreviewByMagicAsync(
        string archivePath,
        ArchiveItemModel entry,
        ArchiveFormat archiveFormat,
        int headSize = 4096,
        string? password = null,
        CancellationToken ct = default)
    {
        try
        {
            // 1. 读取文件头部
            var head = await ArchiveEntryExtractor.ExtractHeadAsync(
                archivePath, entry.FullPath, headSize, archiveFormat, password, ct);

            if (head == null || head.Length == 0)
                return (PreviewType.Unsupported, FileFormat.Unknown, null);

            // 2. 调用魔数检测
            var detectedFormat = FileFormatDetector.Detect(head, head.Length);

            // 3. 魔数无法识别时，尝试用扩展名兜底
            if (detectedFormat == FileFormat.Unknown)
            {
                var ext = Path.GetExtension(entry.Name);
                if (!string.IsNullOrEmpty(ext))
                    detectedFormat = FileFormatDetector.DetectByExtension(ext);
            }

            // 4. 对某些格式使用扩展名兜底以避免误分类
            if (detectedFormat is FileFormat.Text or FileFormat.Svg or FileFormat.Html or FileFormat.Xml)
            {
                var ext = Path.GetExtension(entry.Name);
                var extBased = FileFormatDetector.DetectByExtension(ext);
                if (extBased != FileFormat.Unknown &&
                    extBased != detectedFormat &&
                    (extBased is FileFormat.Svg or FileFormat.Html or FileFormat.Xml or FileFormat.Markdown
                     or FileFormat.Json or FileFormat.Ini or FileFormat.Csv))
                {
                    detectedFormat = extBased;
                }
            }

            // 5. 映射到 PreviewType
            var previewType = MapFileFormatToPreviewType(detectedFormat);
            var displayName = FileFormatHelper.GetDisplayName(detectedFormat);

            return (previewType, detectedFormat, displayName);
        }
        catch (Exception ex)
        {
            // 提取头部失败时的兜底
            System.Diagnostics.Debug.WriteLine($"ClassifyPreviewByMagicAsync error: {ex.Message}");
            return (PreviewType.Unsupported, FileFormat.Unknown, null);
        }
    }

    /// <summary>
    /// 将 FileFormat 枚举映射到 PreviewType 枚举。
    /// </summary>
    private static PreviewType MapFileFormatToPreviewType(FileFormat format)
    {
        return format switch
        {
            // 图像 (GIF 单独处理，走动画预览路径)
            FileFormat.Gif => PreviewType.Gif,

            FileFormat.Png or FileFormat.Jpeg or FileFormat.Bmp
                or FileFormat.WebP or FileFormat.Ico
                or FileFormat.Tga or FileFormat.Hdr or FileFormat.Exr
                => PreviewType.Image,

            // SVG
            FileFormat.Svg => PreviewType.Svg,

            // 文本类
            FileFormat.Text or FileFormat.Csv or FileFormat.Json
                or FileFormat.Xml or FileFormat.Ini
                => PreviewType.Text,

            // HTML / Markdown
            FileFormat.Html => PreviewType.Html,
            FileFormat.Markdown => PreviewType.Markdown,

            // 字体
            FileFormat.Ttf or FileFormat.Otf or FileFormat.Woff
                or FileFormat.Woff2
                => PreviewType.Font,

            // 音频
            FileFormat.Wav or FileFormat.Flac or FileFormat.Mp3
                or FileFormat.Ogg
                => PreviewType.Audio,

            // 视频
            FileFormat.Mp4 or FileFormat.Mkv or FileFormat.WebM
                or FileFormat.Wmv or FileFormat.Mov or FileFormat.Avi
                or FileFormat.Flv
                => PreviewType.Video,

            // 可执行文件
            FileFormat.Pe => PreviewType.Pe,

            // PDF
            FileFormat.Pdf => PreviewType.Pdf,

            // 数据库
            FileFormat.Sqlite => PreviewType.Sqlite,

            // Office 文档
            FileFormat.Docx => PreviewType.Docx,
            FileFormat.Xlsx => PreviewType.Xlsx,
            FileFormat.Pptx => PreviewType.Pptx,

            // 其他文档格式
            FileFormat.Epub or FileFormat.Odt or FileFormat.Ods
                or FileFormat.Odp or FileFormat.OfficeOpenXml
                => PreviewType.Office,

            // BT 种子
            FileFormat.Torrent => PreviewType.Torrent,

            // ISO 映像
            FileFormat.Iso or FileFormat.Iso9660 or FileFormat.Udf
                => PreviewType.Iso,

            // 压缩包格式（压缩包内的压缩包无法预览）
            FileFormat.Zip or FileFormat.SevenZip or FileFormat.Rar
                or FileFormat.Gz or FileFormat.Bz2 or FileFormat.Xz
                or FileFormat.Zstd or FileFormat.Tar
                => PreviewType.Unsupported,

            // 其他不支持的类型
            _ => PreviewType.Unsupported,
        };
    }

    /// <summary>
    /// 提取压缩包中的条目到临时目录。
    /// </summary>
    public static async Task<string?> ExtractToTempAsync(
        string archivePath,
        ArchiveItemModel entry,
        ArchiveFormat format,
        CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "MantisZip", "Preview");
        Directory.CreateDirectory(tempDir);

        // 清理旧临时文件
        foreach (var f in Directory.GetFiles(tempDir))
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }

        var ext = Path.GetExtension(entry.Name);
        var tempFile = Path.Combine(tempDir, $"preview{ext}");

        await ArchiveEntryExtractor.ExtractEntryAsync(
            archivePath,
            entry.FullPath,
            tempFile,
            format,
            password: null,
            ct);

        return tempFile;
    }
}
