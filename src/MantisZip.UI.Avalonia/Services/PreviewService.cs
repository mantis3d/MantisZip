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
