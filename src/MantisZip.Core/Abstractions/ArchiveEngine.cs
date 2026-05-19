namespace MantisZip.Core.Abstractions;

/// <summary>
/// 压缩包项条目
/// </summary>
public class ArchiveItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long Size { get; set; }
    public long CompressedSize { get; set; }
    public DateTime LastModified { get; set; }
    public bool IsDirectory { get; set; }
    public bool IsEncrypted { get; set; }
    public string? Password { get; set; }
    public int Crc32 { get; set; }
    public byte Attributes { get; set; }
}

/// <summary>
/// 压缩选项
/// </summary>
public class ArchiveOptions
{
    /// <summary>
    /// 压缩级别 1-9，默认 5
    /// </summary>
    public int CompressionLevel { get; set; } = 5;

    /// <summary>
    /// 是否加密
    /// </summary>
    public bool Encrypt { get; set; }

    /// <summary>
    /// 密码
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// 分卷大小（字节），0 表示不分卷
    /// </summary>
    public long SplitSize { get; set; }

    /// <summary>
    /// 压缩格式
    /// </summary>
    public ArchiveFormat Format { get; set; } = ArchiveFormat.Zip;

    /// <summary>
    /// 解压时文件已存在的处理方式
    /// </summary>
    public FileConflictAction ConflictAction { get; set; } = FileConflictAction.Overwrite;

    /// <summary>
    /// 文件冲突时的回调。参数为冲突信息，返回处理方式。
    /// 当 <see cref="ConflictAction"/> 为 <see cref="FileConflictAction.Ask"/> 时调用。
    /// 可在后台线程调用，回调需自行处理 UI 线程问题。
    /// </summary>
    public Func<FileConflictInfo, FileConflictAction>? ConflictResolver { get; set; }

    /// <summary>
    /// 文件读取错误时的回调（如文件被占用无法读取）。
    /// 参数为错误信息，返回处理方式。返回 <see cref="FileErrorAction.Retry"/> 可重试。
    /// 可在后台线程调用，回调需自行处理 UI 线程问题。
    /// </summary>
    public Func<FileErrorInfo, FileErrorAction>? ErrorResolver { get; set; }
}

/// <summary>
/// 文件操作错误的处理方式
/// </summary>
public enum FileErrorAction
{
    /// <summary>重试</summary>
    Retry,
    /// <summary>跳过此文件，继续压缩</summary>
    Skip,
    /// <summary>中止整个操作</summary>
    Abort
}

/// <summary>
/// 文件操作错误时传递给回调的信息
/// </summary>
public class FileErrorInfo
{
    /// <summary>出问题的文件路径</summary>
    public string FilePath { get; set; } = string.Empty;
    /// <summary>异常信息</summary>
    public string ErrorMessage { get; set; } = string.Empty;
    /// <summary>剩余的尝试次数（超过后自动跳过）</summary>
    public int RetriesRemaining { get; set; } = 3;
}

    /// <summary>
    /// 文件冲突时传递给回调的信息
    /// </summary>
    public class FileConflictInfo
    {
        /// <summary>目标文件路径</summary>
        public string FilePath { get; set; } = string.Empty;
        /// <summary>压缩包内条目的大小（字节）</summary>
        public long? EntrySize { get; set; }
        /// <summary>压缩包内条目的修改时间</summary>
        public DateTime? EntryModified { get; set; }
        /// <summary>磁盘上已有文件的大小</summary>
        public long? ExistingSize { get; set; }
        /// <summary>磁盘上已有文件的修改时间</summary>
        public DateTime? ExistingModified { get; set; }
        /// <summary>
        /// 自动重命名的建议文件名（不含路径），由调用方在弹窗前预计算。
        /// 对话框用此值预填文本框。
        /// </summary>
        public string? SuggestedName { get; set; }
        /// <summary>
        /// 用户在对话框中输入的自定义文件名（不含路径）。
        /// null 或空字符串表示用户未修改，使用自动生成逻辑。
        /// </summary>
        public string? CustomName { get; set; }
    }

/// <summary>
/// 解压时文件已存在的处理方式
/// </summary>
public enum FileConflictAction
{
    /// <summary>覆盖已有文件（默认）</summary>
    Overwrite,
    /// <summary>自动重命名新文件（如 file (1).txt）</summary>
    Rename,
    /// <summary>跳过不解压</summary>
    Skip,
    /// <summary>每次询问用户</summary>
    Ask,
    /// <summary>仅当压缩包内的文件比磁盘上的文件更新时覆盖</summary>
    OverwriteIfOlder,
    /// <summary>仅当压缩包内的文件比磁盘上的文件更小时覆盖</summary>
    OverwriteIfSmaller
}

/// <summary>
/// 支持的压缩格式
/// </summary>
public enum ArchiveFormat
{
    Zip,
    SevenZip,
    Tar,
    GZip,
    Rar,
    Iso
}

/// <summary>
/// 压缩/解压进度报告
/// </summary>
public class ArchiveProgress
{
    public string CurrentFile { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long ProcessedBytes { get; set; }
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public double PercentComplete { get; set; }

    /// <summary>当前文件的解压/压缩进度 (0–100)，null 表示无此信息。</summary>
    public double? FilePercentComplete { get; set; }
}

    /// <summary>
    /// 压缩引擎接口
    /// </summary>
    public interface IArchiveEngine
    {
        /// <summary>
        /// 是否支持此格式
        /// </summary>
        bool CanHandle(ArchiveFormat format);

        /// <summary>
        /// 解压到指定目录
        /// </summary>
        Task ExtractAsync(string archivePath, string destinationPath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default, ArchiveOptions? options = null);

        /// <summary>
        /// 压缩指定目录/文件
        /// </summary>
        Task CompressAsync(string[] sourcePaths, string outputPath, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 列出压缩包内容
        /// </summary>
        Task<IReadOnlyList<ArchiveItem>> ListEntriesAsync(string archivePath, string? password = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 测试压缩包完整性
        /// </summary>
        Task<bool> TestArchiveAsync(string archivePath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 向已存在的压缩包中添加文件（原地更新）
        /// </summary>
        /// <param name="entryBasePath">压缩包内的目标路径，例如 "subdir/" 表示添加到 subdir 目录下，null 或 "" 表示根目录。</param>
        Task AddToArchiveAsync(string archivePath, string[] sourcePaths, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default, string? entryBasePath = null);

        /// <summary>
        /// 从压缩包中删除指定条目（原地更新）
        /// </summary>
        /// <param name="archivePath">压缩包路径</param>
        /// <param name="entryPaths">要删除的条目路径列表（如 ["file.txt", "subdir/nested.txt"]）</param>
        /// <param name="password">密码（可选）</param>
        /// <param name="progress">进度报告</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <exception cref="FileNotFoundException">条目在压缩包中不存在</exception>
        /// <exception cref="NotSupportedException">此格式不支持删除操作</exception>
        Task DeleteEntriesAsync(string archivePath, string[] entryPaths, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 此引擎是否支持向压缩包添加文件。
        /// </summary>
        bool CanAdd(ArchiveFormat format) => false;

        /// <summary>
        /// 此引擎是否支持从压缩包删除文件。
        /// </summary>
        bool CanDelete(ArchiveFormat format) => false;
    }

/// <summary>
/// 引擎工厂
/// </summary>
public static class ArchiveEngineFactory
{
    private static readonly List<IArchiveEngine> _engines = new();

    static ArchiveEngineFactory()
    {
        // 注册引擎
        _engines.Add(new MantisZip.Core.Engines.ZipEngine());
        _engines.Add(new MantisZip.Core.Engines.SevenZipEngine());
        _engines.Add(new MantisZip.Core.Engines.TarGzEngine());
    }

    public static IArchiveEngine? GetEngine(ArchiveFormat format)
    {
        return _engines.FirstOrDefault(e => e.CanHandle(format));
    }

    public static IArchiveEngine? GetEngineByExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".zip" => GetEngine(ArchiveFormat.Zip),
            ".7z" => GetEngine(ArchiveFormat.SevenZip),
            ".tar" or ".tgz" or ".tar.gz" or ".gz" => GetEngine(ArchiveFormat.Tar),
            ".rar" => GetEngine(ArchiveFormat.Rar),
            ".iso" => GetEngine(ArchiveFormat.Iso),
            _ => null
        };
    }
}