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
    Video,
    Html,
    Markdown,
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

    private const long MaxPreviewFileSize = 50 * 1024 * 1024; // 50 MB
    private const long MaxTextPreviewBytes = 1 * 1024 * 1024;  // 1 MB

    /// <summary>
    /// 根据文件扩展名判断预览类型。
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
        if (OfficeExtensions.Contains(ext)) return PreviewType.Office;
        if (VideoExtensions.Contains(ext)) return PreviewType.Video;
        return PreviewType.Unsupported;
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
