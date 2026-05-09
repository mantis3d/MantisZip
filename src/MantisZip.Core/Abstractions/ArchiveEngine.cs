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
    Rar
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
    Task ExtractAsync(string archivePath, string destinationPath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default);

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
    Task AddToArchiveAsync(string archivePath, string[] sourcePaths, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default);
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
            _ => null
        };
    }
}