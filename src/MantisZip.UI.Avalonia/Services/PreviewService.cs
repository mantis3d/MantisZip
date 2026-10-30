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
    Gif,            // ⚠️ 已废弃：由 AnimatedImage 取代（保留枚举值避免破坏性变更，不再被任何映射产出）
    AnimatedImage,  // 动画图像预览（GIF / Animated WebP 共用）
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
    Unsupported,
    /// <summary>加密文件名压缩包（EncryptHeaders=true）需输入密码才能识别格式</summary>
    NeedsPassword = 99
}

/// <summary>
/// 预览取数结果：<see cref="Bytes"/> 与 <see cref="TempPath"/> 二选一（不会同时有值）。
///
/// <para>
/// 文本类（<see cref="PreviewType.Text"/> / <see cref="PreviewType.Csv"/> /
/// <see cref="PreviewType.Markdown"/>）、<see cref="PreviewType.Image"/> 与
/// <see cref="PreviewType.Pdf"/> 走内存，只填 <see cref="Bytes"/>，不产生磁盘写；
/// 必须真实文件路径的格式（HTML→<c>NativeWebView</c>、字体、ICO 画廊与动画
/// GIF/APNG/SVG/TGA/AVIF 图片、Office、音视频、SQLite、ISO、「打开方式」等）走磁盘，
/// 只填 <see cref="TempPath"/>（图片 / PDF 的内存路径失败时也会降级到这里）。
/// </para>
/// <para>
/// <see cref="IsPartial"/> / <see cref="TotalSize"/> 是部分预览（切片 4）的截断标记：
/// 文本类条目大于 <see cref="PreviewService.MaxTextPreviewBytes"/> 时只取 head，
/// 此时 <see cref="IsPartial"/> 为 <c>true</c>、<see cref="TotalSize"/> 为条目原始大小，
/// UI 据此提示「文件较大，仅显示前 N MB」。因此消费方不要假设
/// <see cref="Bytes"/>.Length == <see cref="TotalSize"/>。
/// </para>
/// </summary>
/// <param name="Bytes">内存中的内容（文本类路径）；落盘路径为 <c>null</c>。</param>
/// <param name="TempPath">临时文件绝对路径（落盘路径）；内存路径为 <c>null</c>。</param>
/// <param name="IsPartial">内容是否被截断（只拿到了条目的前 N 字节）。</param>
/// <param name="TotalSize">条目原始大小（字节），用于展示「全文 N MB」。</param>
public sealed record PreviewContent(
    byte[]? Bytes,
    string? TempPath,
    bool IsPartial = false,
    long TotalSize = 0)
{
    /// <summary>
    /// 条目超过 256MB 硬上限（<see cref="PreviewService.MaxPartialPreviewBytes"/>）——
    /// 连部分预览都不做：<see cref="Bytes"/> 与 <see cref="TempPath"/> 均为 <c>null</c>，
    /// 由调用方提示「文件过大」。
    /// </summary>
    /// <remarks>
    /// 用 <c>init</c> 属性而不是新增位置参数：既有 <c>new PreviewContent(Bytes: …, TempPath: …)</c>
    /// 调用点全部不受影响，且这个信号只在「拒绝预览」这一条路径上出现。
    /// </remarks>
    public bool ExceedsPartialLimit { get; init; }
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
        ".jpg", ".jpeg", ".png", ".bmp", ".ico", ".webp", ".avif", ".tga"
    };

    /// <summary>
    /// 必须落盘的图片扩展名（**故意不走内存**）：
    /// ICO 画廊（<c>IcoParser</c>）、动画 GIF/APNG（<c>GifDecoder</c>/<c>ApngDecoder</c> 只有 path 版）、
    /// SVG（<c>SKSvg.Load(string)</c>）、TGA（ImageSharp path 版）、AVIF（当前解码器不支持）。
    /// 判断用 <see cref="Path.GetExtension(string)"/> 的小写结果（集合本身也忽略大小写）。
    /// </summary>
    private static readonly HashSet<string> RequiresRealPathImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ico", ".gif", ".apng", ".svg", ".tga", ".avif"
    };

    private static readonly HashSet<string> GifExtensions = new(StringComparer.OrdinalIgnoreCase) { ".gif" };
    private static readonly HashSet<string> ApngExtensions = new(StringComparer.OrdinalIgnoreCase) { ".apng" };
    private static readonly HashSet<string> AvifExtensions = new(StringComparer.OrdinalIgnoreCase) { ".avif" };
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

    /// <summary>
    /// 只需读取文件头的格式，不受 MaxPreviewFileSize 限制。
    /// </summary>
    private static readonly HashSet<string> MetadataOnlyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".sys", ".ocx",
        ".pdf", ".docx", ".xlsx", ".pptx",
        ".wav", ".flac", ".mp3",
        ".sqlite", ".sqlite3", ".db", ".db3",
        ".iso",
        ".torrent",
        ".mp4", ".mkv", ".avi",
    };

    /// <summary>
    /// 是否启用格式检测（魔数识别）。由 App.axaml.cs 在启动时、SettingsWindowViewModel.Save 在保存时同步。
    /// 启用后，ClassifyPreviewByMagicAsync 会通过魔数判断文件真实格式。
    /// </summary>
    public static bool EnableFormatDetection { get; set; } = true;

    /// <summary>
    /// 格式检测时读取的文件头部字节数。由 App.axaml.cs 在启动时、SettingsWindowViewModel.Save 在保存时同步。
    /// </summary>
    public static int PreviewHeadSize { get; set; } = 4096;

    /// <summary>
    /// 是否启用图片类预览（Image/Gif/Svg/Ico）。由 App.axaml.cs 在启动时、SettingsWindowViewModel.Save 在保存时同步。
    /// </summary>
    public static bool EnableImagePreview { get; set; } = true;

    /// <summary>
    /// 是否启用文本类预览（Text/Csv/Html/Markdown）。由 App.axaml.cs 在启动时、SettingsWindowViewModel.Save 在保存时同步。
    /// </summary>
    public static bool EnableTextPreview { get; set; } = true;

    /// <summary>
    /// 预览文件大小上限（字节）。只读头的格式（见 MetadataOnlyExtensions）不受限制。
    /// 由 App.axaml.cs 在启动时、SettingsWindowViewModel.Save 在保存时同步。
    /// </summary>
    public static long MaxPreviewFileSize { get; set; } = 15 * 1024 * 1024;

    /// <summary>
    /// 文本类预览大小上限（字节）。由 App.axaml.cs 在启动时、SettingsWindowViewModel.Save 在保存时同步。
    /// </summary>
    public static long MaxTextPreviewBytes { get; set; } = 1 * 1024 * 1024;

    /// <summary>
    /// 部分预览的硬上限（字节，默认 256MB）：条目超过它就连「只显示前 N 字节」也不做。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 文本类条目大于 <see cref="MaxTextPreviewBytes"/> 时不再一律拒绝，而是只显示前 N MB 并提示已截断
    /// （见 <see cref="CanPreviewPartially"/>）。但这条路的取数成本因包类型而异：Zip（非固实）流式读到
    /// N 字节即停；7z / RAR / ISO 要解压完整个条目（只省内存不省 CPU）；而 <b>TarGz / 固实 7z</b> 的
    /// 「head」本质是<b>整条目落盘</b>后再读前 N 字节（<c>ArchiveEntryExtractor.ExtractHeadViaFullExtractAsync</c>）。
    /// </para>
    /// <para>
    /// 所以必须有这条硬上限：否则一个 2GB 的 <c>app.log</c> 在 tar.gz 里会把用户磁盘写满——
    /// 比「看不了」更糟。超过上限时直接不预览、也不落盘（<see cref="PreviewContent.ExceedsPartialLimit"/>）。
    /// 本上限<b>不进设置 UI</b>（切片 4 不新增设置项）。
    /// </para>
    /// </remarks>
    public static long MaxPartialPreviewBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>
    /// CSV 表格预览最大行数。由 App.axaml.cs 在启动时、SettingsWindowViewModel.Save 在保存时同步。
    /// </summary>
    public static int MaxTablePreviewRows { get; set; } = 100;

    /// <summary>
    /// CSV 表格预览最大列数。由 App.axaml.cs 在启动时、SettingsWindowViewModel.Save 在保存时同步。
    /// </summary>
    public static int MaxTablePreviewCols { get; set; } = 100;

    // ===== head LRU 缓存（T5）=====
    // 只缓存文本类预览的 head：LRU 6 条，单条上限 = 当前 MaxTextPreviewBytes。
    // 缓存对象是「已经解出来的小字节数组」，不含完整文件（大文件缓存会吃内存并引入失效面）。
    private const int HeadCacheCapacity = 6;
    private static readonly object HeadCacheGate = new();
    private static readonly LinkedList<string> _headCacheOrder = new();
    private static readonly Dictionary<string, byte[]> _headCache = new(StringComparer.Ordinal);

    // ===== 临时文件生命周期（T4）=====
    // 记录「本次会话上一次预览写出的临时文件」，写入新文件前删它（A→B→A 命中同一路径时不删当前要返回的那个）。
    private static readonly object TempFileGate = new();
    private static string? _lastPreviewTempFile;

    /// <summary>
    /// 判断扩展名是否属于只读头的格式（不受 MaxPreviewFileSize 限制）。
    /// </summary>
    public static bool IsMetadataOnlyExtension(string ext) => MetadataOnlyExtensions.Contains(ext);

    /// <summary>
    /// 该预览类型是否允许「部分预览」——条目超过上限时只显示前 N 字节，而不是一律拒绝。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 目前只有文本三类（<see cref="PreviewType.Text"/> / <see cref="PreviewType.Csv"/> /
    /// <see cref="PreviewType.Markdown"/>）返回 <c>true</c>。
    /// <see cref="PreviewType.Html"/> <b>有意</b>排除：它走 <c>NativeWebView</c>，只接受真实文件路径，
    /// 无法只渲染前 N 字节（截断的 HTML 也会渲染成残缺页面）。
    /// 图片 / PDF 同理：解码器与 PdfPig 都要完整字节（PDF 的 trailer 在文件尾部）。
    /// </para>
    /// <para>
    /// Office 三族（<see cref="PreviewType.Docx"/> / <see cref="PreviewType.Xlsx"/> /
    /// <see cref="PreviewType.Pptx"/> 以及 <see cref="PreviewType.Office"/>）将来由切片 3
    /// （OpenXml / ClosedXML 的 Stream 能力）重新评估：这里返回 <c>false</c> 是<b>有意留的接口位</b>，
    /// 切片 3 做完取数后只翻这一个开关，不需要在调用方散落判断。
    /// </para>
    /// <para>
    /// 与私有方法 <see cref="IsTextLikePreview"/> 今天集合相同但<b>语义不同</b>：那个决定「取数走 head 还是落盘」，
    /// 这个决定「超限时是否允许截断预览」。切片 3 把 Office 纳入部分预览时只改本方法。
    /// </para>
    /// </remarks>
    public static bool CanPreviewPartially(PreviewType previewType) =>
        previewType is PreviewType.Text or PreviewType.Csv or PreviewType.Markdown;

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
        if (GifExtensions.Contains(ext)) return PreviewType.AnimatedImage;
        if (ApngExtensions.Contains(ext)) return PreviewType.AnimatedImage;
        if (AvifExtensions.Contains(ext)) return PreviewType.Image;
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
    /// <remarks>
    /// 仅为兼容既有调用方（<c>MainWindowViewModel.Preview.cs</c> 与既有测试）而保留；
    /// 需要复用已读 head 的调用方请改用 <see cref="ClassifyPreviewByMagicWithHeadAsync"/>。
    /// </remarks>
    public static async Task<(PreviewType type, FileFormat format, string? displayName)> ClassifyPreviewByMagicAsync(
        string archivePath,
        ArchiveItemModel entry,
        ArchiveFormat archiveFormat,
        int headSize = 4096,
        string? password = null,
        CancellationToken ct = default)
    {
        var (type, format, displayName, _) = await ClassifyPreviewByMagicWithHeadAsync(
            archivePath, entry, archiveFormat, headSize, password, ct);
        return (type, format, displayName);
    }

    /// <summary>
    /// 与 <see cref="ClassifyPreviewByMagicAsync"/> 相同，但把已读到的 head 一并交出来，
    /// 避免文本预览为同一份数据再读一次。
    /// </summary>
    /// <remarks>
    /// ⚠️ 返回的 head 长度最多 <paramref name="headSize"/>（默认 4096），
    /// <b>可能小于文本预览需要的 <see cref="MaxTextPreviewBytes"/>（默认 1MB）</b>；
    /// 文本预览仍需二次取（<see cref="LoadPreviewContentAsync"/> 会先查 LRU 缓存，
    /// 同一 (包, 条目, size, mtime) 的重复预览不会重新解压）。
    /// head 读不到（空条目 / 异常 / 需要密码）时为 <c>null</c>。
    /// </remarks>
    public static async Task<(PreviewType type, FileFormat format, string? displayName, byte[]? head)>
        ClassifyPreviewByMagicWithHeadAsync(
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
                return (PreviewType.Unsupported, FileFormat.Unknown, null, head);

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

            return (previewType, detectedFormat, displayName, head);
        }
        catch (ArchiveEntryExtractor.PasswordRequiredException)
        {
            // 加密文件名压缩包：无密码无法读取条目数据
            return (PreviewType.NeedsPassword, FileFormat.Encrypted, "加密文件名", null);
        }
        catch (Exception ex)
        {
            // 提取头部失败时的兜底
            System.Diagnostics.Debug.WriteLine($"ClassifyPreviewByMagicAsync error: {ex.Message}");
            return (PreviewType.Unsupported, FileFormat.Unknown, null, null);
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
            FileFormat.Gif => PreviewType.AnimatedImage,
            FileFormat.Apng => PreviewType.AnimatedImage,
            FileFormat.Avif => PreviewType.Image,

            FileFormat.Png or FileFormat.Jpeg or FileFormat.Bmp
                or FileFormat.WebP or FileFormat.Ico
                or FileFormat.Tga or FileFormat.Hdr or FileFormat.Exr
                => PreviewType.Image,

            // SVG
            FileFormat.Svg => PreviewType.Svg,

            // 文本类
            FileFormat.Text or FileFormat.Json
                or FileFormat.Xml or FileFormat.Ini
                => PreviewType.Text,

            // CSV（独立于文本：魔数检测经扩展名兜底识别后走表格预览）
            FileFormat.Csv => PreviewType.Csv,

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
    /// 该文件名 / 扩展名是否**只能**按真实文件路径预览（ICO / GIF / APNG / SVG / TGA / AVIF）。
    ///
    /// <para>
    /// 内存版 <c>PreviewViewModel.ShowImage(byte[], string)</c> 用它在解码前快速拒绝：
    /// 这些格式的解码器只有 path 版，硬解字节要么失败要么退化（动画变单帧），
    /// 一律交给调用方回落落盘。与 <see cref="RequiresRealPathImageExtensions"/> 同源。
    /// </para>
    /// </summary>
    /// <param name="fileNameOrExtension">含扩展名的文件名，或纯扩展名（.png / .PNG 均可）。</param>
    public static bool RequiresRealPathForImage(string fileNameOrExtension)
        => RequiresRealPathImageExtensions.Contains(Path.GetExtension(fileNameOrExtension).ToLowerInvariant());

    /// <summary>
    /// 按预览类型分流取数：文本类（Text / Csv / Markdown）走内存 head，
    /// <see cref="PreviewType.Image"/> / <see cref="PreviewType.Pdf"/> 走内存整条目，
    /// 其余落盘为临时文件。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 降级链（Must Have「所有路径都有降级」）：文本类先查 head LRU → miss 才
    /// <see cref="ArchiveEntryExtractor.ExtractHeadAsync"/> → head 为空或抛异常 →
    /// 回落到 <see cref="ExtractToTempAsync"/>；图片 / PDF 先
    /// <see cref="ArchiveEntryExtractor.ExtractEntryToMemoryAsync"/>（上限取运行时
    /// <see cref="MaxPreviewFileSize"/> + 1，超限抛 <c>EntryTooLargeException</c>）→
    /// 超限或任何异常 → 回落到 <see cref="ExtractToTempAsync"/>；落盘也失败时返回 <c>null</c>。
    /// </para>
    /// <para>
    /// <see cref="RequiresRealPathImageExtensions"/> 里的图片扩展名（ICO / GIF / APNG / SVG / TGA / AVIF）
    /// **故意**不走内存：解码器只有 path 版或本就不支持，直接落盘。
    /// </para>
    /// <para>
    /// 文本类超过 <see cref="MaxPartialPreviewBytes"/>（256MB 硬上限）时<b>不预览也不落盘</b>，
    /// 返回只带 <see cref="PreviewContent.ExceedsPartialLimit"/> 标记的空结果；
    /// 介于 <see cref="MaxTextPreviewBytes"/> 与硬上限之间时只返回 head，
    /// 并用 <see cref="PreviewContent.IsPartial"/> 标记已截断。
    /// </para>
    /// <para>
    /// <see cref="PreviewType.Html"/> 故意<b>不</b>走内存分支：<c>NativeWebView</c> 只接受
    /// 真实文件路径（见 <c>PreviewViewModel.ShowHtmlPreview</c> 的注释），落盘是硬约束。
    /// </para>
    /// </remarks>
    /// <returns>取数结果；内存与落盘两条路都失败时为 <c>null</c>。</returns>
    public static async Task<PreviewContent?> LoadPreviewContentAsync(
        string archivePath,
        ArchiveItemModel entry,
        ArchiveFormat format,
        PreviewType previewType,
        string? password = null,
        CancellationToken ct = default)
    {
        if (IsTextLikePreview(previewType))
        {
            // ① 硬上限（256MB）：TarGz / 固实 7z 的「部分预览」本质是整条目落盘后取前 N 字节，
            //    超过上限时连 head 都不取、更不落盘，只把「过大」信号交给调用方提示。
            if (entry.Size > MaxPartialPreviewBytes)
            {
                App.DebugLog(
                    $"[PRV] '{entry.FullPath}' ({entry.Size} bytes) exceeds partial preview limit " +
                    $"({MaxPartialPreviewBytes} bytes), skipping without extracting");
                return new PreviewContent(Bytes: null, TempPath: null, TotalSize: entry.Size)
                {
                    ExceedsPartialLimit = true,
                };
            }

            try
            {
                // ② 取 head（沿用 head LRU，上限 = 运行时 MaxTextPreviewBytes）
                var head = await LoadTextHeadAsync(archivePath, entry, format, password, ct);
                if (head.Length > 0)
                {
                    // ③ 条目比 head 大 ⇒ 只拿到了一部分，标记部分预览供 UI 提示
                    return new PreviewContent(
                        Bytes: head,
                        TempPath: null,
                        IsPartial: head.LongLength < entry.Size,
                        TotalSize: entry.Size);
                }

                App.DebugLog($"[PRV] head empty for '{entry.FullPath}', falling back to temp extract");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Must Have：取不到内存内容时必须回落到原磁盘路径
                App.DebugLog($"[PRV] head load failed, falling back to temp extract: {ex.Message}");
            }
        }

        // 图片 / PDF：整条目读进内存（SkiaSharp 解码、PdfPig 打开都吃完整字节）。
        // 上限读运行时属性（设置页可改），不能用编译期常量。
        if (previewType is PreviewType.Image or PreviewType.Pdf)
        {
            var requiresRealPath = previewType == PreviewType.Image
                && RequiresRealPathForImage(entry.Name);

            if (requiresRealPath)
            {
                App.DebugLog($"[PRV] '{entry.Name}' requires a real path, extracting to temp");
            }
            else
            {
                try
                {
                    // +1 是为了让「刚好等于上限」通过、超出 1 字节即由提取器判定超限
                    var bytes = await ArchiveEntryExtractor.ExtractEntryToMemoryAsync(
                        archivePath, entry.FullPath, format, MaxPreviewFileSize + 1, password, ct);
                    if (bytes.Length > 0)
                        return new PreviewContent(Bytes: bytes, TempPath: null, TotalSize: entry.Size);

                    App.DebugLog($"[PRV] memory load returned empty for '{entry.FullPath}', falling back to temp extract");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // EntryTooLargeException / ArchiveEntryNotFoundException / 加密 / 格式不支持……
                    // 一律按降级处理：能预览的走落盘，不能预览的由上层显示取数失败
                    App.DebugLog($"[PRV] {previewType} memory load failed ({ex.GetType().Name}), falling back to temp extract: {ex.Message}");
                }
            }
        }

        try
        {
            var tempPath = await ExtractToTempAsync(archivePath, entry, format, password, ct);
            return tempPath == null
                ? null
                : new PreviewContent(Bytes: null, TempPath: tempPath, TotalSize: entry.Size);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            App.DebugLog($"[PRV] temp extract failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>文本类预览（走内存 head）的类型集合。HTML 故意排除（<c>NativeWebView</c> 要真实路径）。</summary>
    private static bool IsTextLikePreview(PreviewType previewType) =>
        previewType is PreviewType.Text or PreviewType.Csv or PreviewType.Markdown;

    /// <summary>
    /// 取文本类预览的 head：先查 LRU，miss 才解压并写回。
    /// 单条上限取当前 <see cref="MaxTextPreviewBytes"/>（设置页会回写，不能按固定默认值判断）。
    /// </summary>
    private static async Task<byte[]> LoadTextHeadAsync(
        string archivePath,
        ArchiveItemModel entry,
        ArchiveFormat format,
        string? password,
        CancellationToken ct)
    {
        var maxBytes = (int)Math.Min(int.MaxValue, Math.Max(0, MaxTextPreviewBytes));
        var cacheKey = BuildHeadCacheKey(archivePath, entry);

        if (TryGetCachedHead(cacheKey, maxBytes, out var cached))
        {
            App.DebugLog($"[PRV] head cache hit: {cacheKey}");
            return cached;
        }

        var head = await ArchiveEntryExtractor.ExtractHeadAsync(
            archivePath, entry.FullPath, maxBytes, format, password, ct);

        if (head.Length > 0)
            TryCacheHead(cacheKey, head);

        return head;
    }

    /// <summary>缓存 key = 包路径 + 条目 FullPath + 条目大小 + 压缩包 mtime（UTC ticks，取不到时 0）。</summary>
    private static string BuildHeadCacheKey(string archivePath, ArchiveItemModel entry)
    {
        long archiveMtimeTicks = 0;
        try
        {
            var info = new FileInfo(archivePath);
            if (info.Exists)
                archiveMtimeTicks = info.LastWriteTimeUtc.Ticks;
        }
        catch
        {
            // 取不到 mtime 时退化为 0：只影响缓存失效判定，不应让预览失败
        }

        return $"{archivePath}|{entry.FullPath}|{entry.Size}|{archiveMtimeTicks}";
    }

    private static bool TryGetCachedHead(string key, int maxBytes, out byte[] bytes)
    {
        lock (HeadCacheGate)
        {
            if (!_headCache.TryGetValue(key, out var cached))
            {
                bytes = Array.Empty<byte>();
                return false;
            }

            // 设置在会话中被改小 → 旧条目已超上限，按 miss 处理并清掉，不把超限内容交出去
            if (cached.Length > maxBytes)
            {
                _headCache.Remove(key);
                var stale = _headCacheOrder.Find(key);
                if (stale != null)
                    _headCacheOrder.Remove(stale);
                bytes = Array.Empty<byte>();
                return false;
            }

            // 命中即提升到队首（LRU）
            var node = _headCacheOrder.Find(key);
            if (node != null)
            {
                _headCacheOrder.Remove(node);
                _headCacheOrder.AddFirst(node);
            }

            bytes = cached;
            return true;
        }
    }

    private static void TryCacheHead(string key, byte[] bytes)
    {
        // 超过当前上限的内容不入缓存（直接返回给调用方即可）
        var limit = MaxTextPreviewBytes;
        if (limit <= 0 || bytes.LongLength > limit)
            return;

        lock (HeadCacheGate)
        {
            if (_headCache.ContainsKey(key))
            {
                var existing = _headCacheOrder.Find(key);
                if (existing != null)
                    _headCacheOrder.Remove(existing);
            }

            _headCache[key] = bytes;
            _headCacheOrder.AddFirst(key);

            while (_headCacheOrder.Count > HeadCacheCapacity)
            {
                var last = _headCacheOrder.Last;
                if (last == null)
                    break;
                _headCacheOrder.RemoveLast();
                _headCache.Remove(last.Value);
            }
        }
    }

    /// <summary>测试用：清空 head LRU 缓存（生产代码无调用点）。</summary>
    internal static void ResetHeadCacheForTests()
    {
        lock (HeadCacheGate)
        {
            _headCache.Clear();
            _headCacheOrder.Clear();
        }
    }

    /// <summary>测试用：当前 head 缓存条目数。</summary>
    internal static int HeadCacheCountForTests
    {
        get
        {
            lock (HeadCacheGate)
                return _headCache.Count;
        }
    }

    /// <summary>
    /// 提取压缩包中的条目到临时目录，返回临时文件路径；失败返回 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 文件名 = <c>preview_{entry.FullPath 的稳定哈希}{ext}</c>（FNV-1a 64，跨会话稳定；
    /// 不能用 <see cref="string.GetHashCode()"/>——.NET 的字符串哈希进程内随机化，
    /// 每次运行文件名都会变，A→B→A 就命不中同一路径）。
    /// </para>
    /// <para>
    /// 不再「每次预览前清空整个 Preview 目录」：只删本次会话上一条产生的文件。
    /// A→B→A 时上一条与当前路径相同，因此绝不删除当前要返回的那个文件；
    /// 全目录清理交给 <see cref="CleanupPreviewTempDir"/> / <see cref="CleanupPreviewTempOnStartup"/>。
    /// </para>
    /// </remarks>
    public static async Task<string?> ExtractToTempAsync(
        string archivePath,
        ArchiveItemModel entry,
        ArchiveFormat format,
        string? password = null,
        CancellationToken ct = default)
    {
        var tempDir = GetPreviewTempDir();
        var ext = Path.GetExtension(entry.Name);
        var tempFile = Path.Combine(tempDir, $"preview_{StableHash(entry.FullPath)}{ext}");

        try
        {
            Directory.CreateDirectory(tempDir);

            // 写入新文件前删掉本次会话的上一条；同路径（A→B→A）说明是要复用的当前条目，不删
            string? previous;
            lock (TempFileGate)
            {
                previous = _lastPreviewTempFile;
                _lastPreviewTempFile = tempFile;
            }

            if (previous != null && !PathsEqual(previous, tempFile))
                TryDeletePreviewTempFile(previous);

            await ArchiveEntryExtractor.ExtractEntryAsync(
                archivePath,
                entry.FullPath,
                tempFile,
                format,
                password: password,
                ct);

            return tempFile;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            App.DebugLog($"[PRV] ExtractToTempAsync failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Preview 临时目录的绝对路径（便携模式跟随 <see cref="AppSettings.GetTempDir"/>）。</summary>
    private static string GetPreviewTempDir() => Path.Combine(AppSettings.GetTempDir(), "Preview");

    /// <summary>
    /// 递归删除整个 Preview 临时目录：应用退出钩子 / 设置页「清理预览缓存」的兜底入口。
    /// （<c>SettingsWindowViewModel.CleanPreviewTemp</c> 目前自己删目录、不调本方法；
    /// 这里提供统一入口供新的调用点使用。）
    /// </summary>
    public static void CleanupPreviewTempDir()
    {
        lock (TempFileGate)
            _lastPreviewTempFile = null;

        try
        {
            var tempDir = GetPreviewTempDir();
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        catch (Exception ex)
        {
            App.DebugLog($"[PRV] CleanupPreviewTempDir failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 应用启动时清一次 Preview 临时目录内的残留文件（上一会话遗留）。
    /// </summary>
    /// <remarks>
    /// 调用点应在应用启动阶段（<c>App.OnFrameworkInitializationCompleted</c> 或主窗口初始化），
    /// 本任务不负责接线。刻意只删文件、不删目录本身（目录/句柄可能正被预览或 WebView 占用）。
    /// </remarks>
    public static void CleanupPreviewTempOnStartup()
    {
        lock (TempFileGate)
            _lastPreviewTempFile = null;

        try
        {
            var tempDir = GetPreviewTempDir();
            if (!Directory.Exists(tempDir))
                return;

            foreach (var file in Directory.GetFiles(tempDir))
                TryDeletePreviewTempFile(file);
        }
        catch (Exception ex)
        {
            App.DebugLog($"[PRV] CleanupPreviewTempOnStartup failed: {ex.Message}");
        }
    }

    private static bool PathsEqual(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void TryDeletePreviewTempFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best effort：文件可能仍被预览句柄占用，留到下次启动清理
        }
    }

    /// <summary>
    /// 条目路径的稳定哈希（FNV-1a 64），用于临时文件名。
    /// </summary>
    /// <remarks>
    /// 不用 <see cref="string.GetHashCode()"/>：.NET 的字符串哈希在进程内随机化，
    /// 跨会话不稳定 → 临时文件名漂移，A→B→A 无法命中同一路径（也就白丢一次解压）。
    /// </remarks>
    private static string StableHash(string value)
    {
        const ulong FnvOffsetBasis = 14695981039346656037UL;
        const ulong FnvPrime = 1099511628211UL;

        var hash = FnvOffsetBasis;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= FnvPrime;
        }

        return hash.ToString("x16");
    }
}
