using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SharpSevenZip;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Readers;
using SharpCompress.Writers.Zip;

namespace MantisZip.Core.Engines;

/// <summary>
/// ZIP 压缩引擎（基于 SharpCompress，加密使用 SharpSevenZip + OutArchiveFormat.Zip）
/// </summary>
public class ZipEngine : IArchiveEngine
{
    private const int CopyBufferSize = 262144;

    /// <summary>
    /// 使用 SharpCompress 打开 ZIP 文件，自动检测编码（UTF-8 → GBK 回退）。
    /// SharpCompress 每实例设置编码，无全局副作用。
    /// 
    /// 回退逻辑：只有 ZIP 条目未设置 UTF-8 标志（bit 11）且内容含高位字符时，
    /// 才尝试 GBK 回退。如果 bit 11 已设置，说明条目名是 UTF-8 编码，不进行回退。
    /// </summary>
    internal static IArchive OpenArchiveWithEncodingFallback(string archivePath, string? password = null)
    {
        // 使用 FileShare.Delete 允许在 archive 仍持有流时删除原文件
        var fs = File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var options = new ReaderOptions { Password = password ?? string.Empty };

        IArchive OpenWithGbk()
        {
            fs.Dispose();
            var fs2 = File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var gbkOptions = new ReaderOptions
            {
                Password = password ?? string.Empty,
                ArchiveEncoding = new SharpCompress.Common.ArchiveEncoding
                {
                    Default = Encoding.GetEncoding("gbk")
                }
            };
            return ArchiveFactory.OpenArchive(fs2, gbkOptions);
        }

        IArchive archive;
        try
        {
            archive = ArchiveFactory.OpenArchive(fs, options);
        }
        catch (Exception ex)
        {
            CoreLog.Trace("OpenArchiveWithEncodingFallback: failed to open archive: {0}", ex.Message);
            fs.Dispose();
            throw;
        }

        try
        {
            var hasHighAscii = archive.Entries.Any(e =>
                !string.IsNullOrEmpty(e.Key) && e.Key.Any(c => c > 127));

            if (hasHighAscii)
            {
                // 检查 ZIP 中央目录中是否有条目设置了 UTF-8 标志（bit 11）。
                // 如果有，说明条目名本就是 UTF-8 编码，不应回退到 GBK。
                if (ZipHasUtf8Flag(archivePath))
                {
                    CoreLog.Info("OpenArchiveWithEncodingFallback: ZIP has UTF-8 flag (bit 11), keeping UTF-8 codec");
                    return archive;
                }

                // 即使没有 bit 11，如果解码后的文件名看起来像合法的 CJK 文本（而非
                // GBK→UTF-8 误解码的拉丁字符），也保留 UTF-8 解码结果。
                if (LooksLikeValidCjk(archive))
                {
                    CoreLog.Info("OpenArchiveWithEncodingFallback: decoded names appear as valid CJK, keeping UTF-8 codec");
                    return archive;
                }

                CoreLog.Info("OpenArchiveWithEncodingFallback: detected high-ASCII entry names without UTF-8 flag, retrying with GBK");
                archive.Dispose();
                return OpenWithGbk();
            }

            CoreLog.Info("OpenArchiveWithEncodingFallback: entries appear ASCII, keeping default codec");
            return archive;
        }
        catch (Exception ex)
        {
            CoreLog.Trace("OpenArchiveWithEncodingFallback: encoding detection failed, falling back to GBK: {0}", ex.Message);
            archive.Dispose();
            return OpenWithGbk();
        }
    }

    /// <summary>
    /// 读取 ZIP 文件的中央目录，检查是否有任何条目设置了 UTF-8 文件名标志（通用位标志 bit 11 = 0x0800）。
    /// </summary>
    private static bool ZipHasUtf8Flag(string archivePath)
    {
        try
        {
            using var fs = File.OpenRead(archivePath);
            if (fs.Length < 22) return false;

            // 在文件末尾搜索 EOCD（End of Central Directory）签名 0x06054b50
            long eocdPos = -1;
            // EOCD 最小固定长度 22，最大长度 65557（含注释）
            long searchStart = Math.Max(0, fs.Length - 65557);
            byte[] sig = [0x50, 0x4b, 0x05, 0x06]; // little-endian 0x06054b50

            fs.Seek(searchStart, SeekOrigin.Begin);
            byte[] buf = new byte[fs.Length - searchStart];
            int read = fs.Read(buf, 0, buf.Length);

            for (int i = read - 22; i >= 0; i--)
            {
                if (buf[i] == sig[0] && buf[i + 1] == sig[1] &&
                    buf[i + 2] == sig[2] && buf[i + 3] == sig[3])
                {
                    eocdPos = searchStart + i;
                    break;
                }
            }

            if (eocdPos < 0) return false;

            // 解析 EOCD：偏移 16 处为中央目录偏移量（4 bytes）
            uint centralDirOffset = BitConverter.ToUInt32(buf, (int)(eocdPos - searchStart + 16));
            uint centralDirSize = BitConverter.ToUInt32(buf, (int)(eocdPos - searchStart + 12));

            // 遍历中央目录条目
            fs.Seek(centralDirOffset, SeekOrigin.Begin);
            var reader = new BinaryReader(fs, Encoding.UTF8);
            long endPos = centralDirOffset + centralDirSize;

            while (fs.Position < endPos)
            {
                uint sig2 = reader.ReadUInt32();
                if (sig2 != 0x02014b50) break;

                // 跳过版本信息（4 bytes）
                reader.ReadBytes(4);
                // 通用位标志（2 bytes），偏移 8
                ushort flags = reader.ReadUInt16();

                if ((flags & 0x0800) != 0)
                    return true;

                // 跳过剩余的固定头部到达可变长度字段
                // 已读：签名(4) + 版本(2+2) + 标志(2) = 10
                // 再跳：压缩方法(2) + 时间(2) + 日期(2) + CRC(4) + 压缩大小(4) + 未压缩大小(4) = 18
                reader.ReadBytes(18);
                ushort nameLen = reader.ReadUInt16();  // 偏移 28
                ushort extraLen = reader.ReadUInt16(); // 偏移 30
                ushort commentLen = reader.ReadUInt16(); // 偏移 32
                reader.ReadBytes(12); // 磁盘号(2) + 内部属性(2) + 外部属性(4) + 本地偏移(4) = 12

                // 跳过可变长度字段
                reader.ReadBytes(nameLen + extraLen + commentLen);
            }

            return false;
        }
        catch (Exception ex)
        {
            CoreLog.Trace("ZipHasUtf8Flag: failed to read central directory: {0}", ex.Message);
            // 出错时回退到旧行为（回退 GBK）
            return false;
        }
    }

    /// <summary>
    /// 检查已用 UTF-8 解码的条目名是否看起来像合法的 CJK 文本。
    /// GBK→UTF-8 误解码会产生拉丁字符（如 é ① À 等），而合法 CJK 在 U+4E00+ 范围。
    /// </summary>
    private static bool LooksLikeValidCjk(IArchive archive)
    {
        int totalHigh = 0;
        int cjkCount = 0;

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Key)) continue;
            foreach (char c in entry.Key)
            {
                if (c <= 127) continue;
                totalHigh++;
                // CJK 及相关字符范围
                if ((c >= 0x4E00 && c <= 0x9FFF) ||      // CJK Unified Ideographs
                    (c >= 0x3400 && c <= 0x4DBF) ||      // CJK Extension A
                    (c >= 0x2B740 && c <= 0x2B81F) ||    // CJK Extension C
                    (c >= 0xF900 && c <= 0xFAFF) ||      // CJK Compatibility Ideographs
                    (c >= 0x3000 && c <= 0x303F) ||      // CJK Symbols and Punctuation
                    (c >= 0xFF00 && c <= 0xFFEF))        // Halfwidth and Fullwidth Forms
                {
                    cjkCount++;
                }
            }
        }

        return totalHigh > 0 && cjkCount >= totalHigh * 0.5;
    }

    public bool CanHandle(ArchiveFormat format) => format == ArchiveFormat.Zip;

    public bool CanAdd(ArchiveFormat format) => format == ArchiveFormat.Zip;

    public bool CanDelete(ArchiveFormat format) => format == ArchiveFormat.Zip;

    public async Task<ExtractResult> ExtractAsync(string archivePath, string destinationPath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default, ArchiveOptions? options = null)
    {
        CoreLog.Entry();
        CoreLog.Info($"ExtractAsync: {archivePath} -> {destinationPath}, password={(password != null ? "***" : "null")}");
        var sw = Stopwatch.StartNew();

        var result = await Task.Run(async () =>
        {
            using var archive = OpenArchiveWithEncodingFallback(archivePath, password);

            // 检查是否有加密条目但未提供密码
            var hasEncrypted = archive.Entries.Any(e => e.IsEncrypted);
            if (hasEncrypted && string.IsNullOrEmpty(password))
            {
                CoreLog.Info("ExtractAsync: archive has encrypted entries but no password provided");
                throw new InvalidOperationException("此压缩包已加密，请输入密码 (This archive is encrypted, password required)");
            }

            var allEntries = archive.Entries.ToList();
            var entries = allEntries.Where(e => !e.IsDirectory).ToList();
            var totalBytes = entries.Sum(e => e.Size);
            var processedBytes = 0L;
            var processedFiles = 0;
            int failedEntries = 0;

            CoreLog.Info($"ExtractAsync: {entries.Count} entries, {totalBytes} total bytes");

            foreach (var entry in allEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entryKey = entry.Key ?? string.Empty;

                if (entry.IsDirectory)
                {
                    var dirPath = FileConflictHelper.GetSafePath(destinationPath, entryKey);
                    if (!Directory.Exists(dirPath))
                        Directory.CreateDirectory(dirPath);
                    continue;
                }

                var outputPath = FileConflictHelper.GetSafePath(destinationPath, entryKey);
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                var entryModified = entry.LastModifiedTime ?? DateTime.MinValue;
                var resolvedPath = await FileConflictHelper.ResolvePathAsync(outputPath, options, entryModified, entry.Size);
                if (resolvedPath == null)
                {
                    processedBytes += entry.Size;
                    continue;
                }

                var entrySize = entry.Size;

                try
                {
                    using (var entryStream = entry.OpenEntryStream())
                    using (var outputStream = File.Create(resolvedPath))
                    {
                        var buffer = new byte[CopyBufferSize];
                        var entryProcessed = 0L;
                        var lastReportTime = DateTime.Now;
                        var reportInterval = TimeSpan.FromMilliseconds(100);

                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var read = entryStream.Read(buffer, 0, buffer.Length);
                            if (read <= 0) break;

                            outputStream.Write(buffer, 0, read);
                            entryProcessed += read;

                            var now = DateTime.Now;
                            if (now - lastReportTime >= reportInterval || entryProcessed >= entrySize)
                            {
                                var filePct = entrySize > 0 ? (double)entryProcessed / entrySize * 100 : 100;
                                var overallPct = totalBytes > 0 ? (double)(processedBytes + entryProcessed) / totalBytes * 100 : 0;
                                progress?.Report(new ArchiveProgress
                                {
                                    CurrentFile = entryKey,
                                    TotalFiles = entries.Count,
                                    ProcessedFiles = processedFiles,
                                    TotalBytes = totalBytes,
                                    ProcessedBytes = processedBytes + entryProcessed,
                                    PercentComplete = overallPct,
                                    FilePercentComplete = filePct
                                });
                                lastReportTime = now;
                            }
                        }
                    }
                    // 恢复文件原始修改时间
                    try { File.SetLastWriteTime(resolvedPath, entryModified); } catch (Exception tsEx) { CoreLog.Info($"ExtractAsync: failed to set timestamp on {resolvedPath}: {tsEx.Message}"); }

                    processedBytes += entrySize;
                    processedFiles++;
                }
                catch (UnauthorizedAccessException uax)
                {
                    CoreLog.Info($"ExtractAsync: permission denied for '{entryKey}': {uax.Message}");
                    failedEntries++;
                }
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100
            });

            CoreLog.Info($"ExtractAsync: done, {processedFiles} files, {processedBytes} bytes, {sw.ElapsedMilliseconds}ms, failedEntries={failedEntries}");
            return new ExtractResult { SucceededEntries = processedFiles, FailedEntries = failedEntries };
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
        return result;
    }

    public async Task ExtractEntriesAsync(
        string archivePath,
        IReadOnlyList<string> entryKeys,
        string destinationPath,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default,
        ArchiveOptions? options = null,
        IReadOnlyDictionary<string, string>? outputPathOverrides = null)
    {
        CoreLog.Entry();
        CoreLog.Info($"ExtractEntriesAsync: {archivePath}, {entryKeys.Count} entries -> {destinationPath}");
        var sw = Stopwatch.StartNew();

        await Task.Run(async () =>
        {
            using var archive = OpenArchiveWithEncodingFallback(archivePath, password);
            var entries = archive.Entries.ToList();
            // entryKeys（来自预览树 FilteredEntryKeys）以 '/' 分隔；SharpCompress 在 Windows 下
            // 可能返回 '\' 分隔的 Key，统一归一化后再匹配（预览 = 实际 的保证）
            var totalBytes = entries.Where(e => entryKeys.Contains(ArchivePath.Normalize(e.Key))).Sum(e => e.Size);
            var processedBytes = 0L;
            var processedFiles = 0;
            var filteredEntries = entries.Where(e => entryKeys.Contains(ArchivePath.Normalize(e.Key))).ToList();

            CoreLog.Info($"ExtractEntriesAsync: {filteredEntries.Count} matching entries");

            foreach (var entry in filteredEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entryKey = entry.Key ?? string.Empty;
                var normalizedKey = ArchivePath.Normalize(entryKey);

                if (entry.IsDirectory)
                {
                    var dirPath = FileConflictHelper.GetSafePath(destinationPath, normalizedKey);
                    if (!Directory.Exists(dirPath))
                        Directory.CreateDirectory(dirPath);
                    continue;
                }

                var outputPath = outputPathOverrides?.GetValueOrDefault(normalizedKey)
                    ?? FileConflictHelper.GetSafePath(destinationPath, normalizedKey);
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                var entryModified = entry.LastModifiedTime ?? DateTime.MinValue;
                var resolvedPath = await FileConflictHelper.ResolvePathAsync(outputPath, options, entryModified, entry.Size);
                if (resolvedPath == null)
                {
                    processedBytes += entry.Size;
                    continue;
                }

                var entrySize = entry.Size;
                using (var entryStream = entry.OpenEntryStream())
                using (var outputStream = File.Create(resolvedPath))
                {
                    var buffer = new byte[CopyBufferSize];
                    long entryProcessed = 0;
                    var lastReportTime = DateTime.Now;
                    var reportInterval = TimeSpan.FromMilliseconds(100);

                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var read = entryStream.Read(buffer, 0, buffer.Length);
                        if (read <= 0) break;

                        outputStream.Write(buffer, 0, read);
                        entryProcessed += read;

                        var now = DateTime.Now;
                        if (now - lastReportTime >= reportInterval || entryProcessed >= entrySize)
                        {
                            var filePct = entrySize > 0 ? (double)entryProcessed / entrySize * 100 : 100;
                            var overallPct = totalBytes > 0 ? (double)(processedBytes + entryProcessed) / totalBytes * 100 : 0;
                            progress?.Report(new ArchiveProgress
                            {
                                CurrentFile = entryKey,
                                TotalFiles = filteredEntries.Count,
                                ProcessedFiles = processedFiles,
                                TotalBytes = totalBytes,
                                ProcessedBytes = processedBytes + entryProcessed,
                                PercentComplete = overallPct,
                                FilePercentComplete = filePct
                            });
                            lastReportTime = now;
                        }
                    }
                }

                try { File.SetLastWriteTime(resolvedPath, entryModified); } catch { CoreLog.Trace("ZipEngine.ExtractAsync: failed to set last write time for '{0}'", resolvedPath); }

                processedBytes += entrySize;
                processedFiles++;
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100
            });

            CoreLog.Info($"ExtractEntriesAsync: done, {processedFiles} files, {sw.ElapsedMilliseconds}ms");
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }

    public async Task CompressAsync(string[] sourcePaths, string outputPath, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"CompressAsync: [{string.Join("; ", sourcePaths)}] -> {outputPath}, level={options.CompressionLevel}, split={options.SplitSize}");
        var sw = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            // 收集所有文件（使用 FileScanner 共享工具，边发现边报告进度）
            var (files, totalBytes) = FileScanner.CollectFiles(sourcePaths, progress, cancellationToken, options.FileWhitelist);

            if (files.Count == 0)
            {
                CoreLog.Info("CompressAsync: no files to compress, returning");
                return;
            }

            CoreLog.Info($"CompressAsync: {files.Count} files to compress, {totalBytes} bytes total");

            long processedBytes = 0;
            int totalFiles = files.Count;
            int processedFiles = 0;

            try
            {
                var outputStream = options.SplitSize > 0
                    ? (Stream)new SplitOutputStream(outputPath, options.SplitSize)
                    : File.Create(outputPath);
                using var fsOut = outputStream;

                var lastReportTime = DateTime.Now;
                var reportInterval = TimeSpan.FromMilliseconds(100);
                var isEncrypted = options.Encrypt && !string.IsNullOrEmpty(options.Password);

                if (isEncrypted)
                {
                    // SharpSevenZip 支持 ZIP + AES-256 加密（SharpCompress ZipWriter 不支持加密）
                    fsOut.Dispose();

                    SevenZipEngine.EnsureLibraryPath();

                    var zipMethod = options.ZipCompressionMethod?.ToLowerInvariant() switch
                    {
                        "deflate64" => CompressionMethod.Deflate64,
                        "bzip2" => CompressionMethod.BZip2,
                        "lzma" => CompressionMethod.Lzma,
                        "ppmd" => CompressionMethod.Ppmd,
                        "copy" or "store" => CompressionMethod.Copy,
                        _ => CompressionMethod.Deflate,
                    };
                    var zipEncrypt = options.ZipEncryptionMethod?.ToLowerInvariant() switch
                    {
                        "zipcrypto" => ZipEncryptionMethod.ZipCrypto,
                        "aes128" => ZipEncryptionMethod.Aes128,
                        "aes192" => ZipEncryptionMethod.Aes192,
                        _ => ZipEncryptionMethod.Aes256,
                    };

                    var s7zCompressor = new SharpSevenZipCompressor
                    {
                        ArchiveFormat = OutArchiveFormat.Zip,
                        ZipEncryptionMethod = zipEncrypt,
                        CompressionMethod = zipMethod,
                        CompressionLevel = MapCompressionLevelToS7Z(options.CompressionLevel),
                        IncludeEmptyDirectories = true,
                        DirectoryStructure = true,
                    };

                    if (options.SplitSize > 0)
                        s7zCompressor.VolumeSize = options.SplitSize;

                    if (!string.IsNullOrEmpty(options.Comment))
                    {
                        // SharpSevenZip 无 Comment 属性，注释在压缩后通过 EOCD 写入
                    }

                    var s7zAccumPct = 0.0;
                    var s7zCurrentFile = "";
                    s7zCompressor.FileCompressionStarted += (_, e) =>
                    {
                        s7zCurrentFile = e.FileName ?? "";
                    };
                    s7zCompressor.Compressing += (_, e) =>
                    {
                        s7zAccumPct = Math.Min(100, s7zAccumPct + e.PercentDelta);
                        progress?.Report(new ArchiveProgress
                        {
                            CurrentFile = "正在压缩: " + s7zCurrentFile,
                            PercentComplete = s7zAccumPct,
                            FilePercentComplete = s7zAccumPct,
                            TotalFiles = totalFiles,
                            ProcessedFiles = processedFiles,
                        });
                    };

                    var sourceFilePaths = files.Select(f => f.FullPath).Distinct().ToArray();
                    if (sourceFilePaths.Length > 0)
                    {
                        s7zCompressor.CompressFilesEncrypted(outputPath, options.Password ?? "", sourceFilePaths);
                    }

                    // SharpSevenZip 的 Compressing 事件 delta 累积通常达不到 100，
                    // 压缩完成后必须补发最终报告，否则进度条停在最后一个文件的中间值。
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = string.Empty,
                        PercentComplete = 100,
                        FilePercentComplete = 100,
                        TotalFiles = totalFiles,
                        ProcessedFiles = totalFiles,
                    });

                    processedBytes = totalBytes;
                    processedFiles = totalFiles;

                    // ZIP 注释：SharpSevenZip 不支持压缩时写入，压缩后通过 EOCD 后写
                    if (!string.IsNullOrEmpty(options.Comment))
                    {
                        try { ZipCommentHelper.WriteComment(outputPath, options.Comment); }
                        catch (Exception commentEx) { CoreLog.Error("CompressAsync: failed to write ZIP comment", commentEx); }
                    }
                }
                else
                {
                    var encoding = (options.FileNameEncoding?.ToLowerInvariant()) switch
                    {
                        "gbk" => Encoding.GetEncoding("GBK"),
                        "default" => Encoding.Default,
                        _ => Encoding.UTF8,
                    };
                    var compressionType = options.ZipCompressionMethod?.ToLowerInvariant() switch
                    {
                        "deflate64" => CompressionType.Deflate64,
                        "bzip2" => CompressionType.BZip2,
                        "lzma" => CompressionType.LZMA,
                        "ppmd" => CompressionType.PPMd,
                        "store" => CompressionType.None,
                        _ => CompressionType.Deflate,
                    };
                    var writerOptions = new ZipWriterOptions(compressionType)
                    {
                        CompressionLevel = options.CompressionLevel,
                        ArchiveComment = options.Comment ?? "",
                        ArchiveEncoding = new ArchiveEncoding { Default = encoding },
                    };
                    using var zipWriter = new ZipWriter(fsOut, writerOptions);

                    foreach (var (fullPath, relativePath) in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!ReadFileWithRetry(fullPath, relativePath, options, zipWriter,
                                ref processedBytes, totalBytes, totalFiles, ref processedFiles,
                                cancellationToken, progress, ref lastReportTime))
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            continue;
                        }

                        var now = DateTime.Now;
                        if (now - lastReportTime >= reportInterval)
                        {
                            var pct = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : 0;
                            progress?.Report(new ArchiveProgress
                            {
                                CurrentFile = "正在压缩: " + relativePath,
                                PercentComplete = pct,
                                FilePercentComplete = 100,
                                TotalFiles = totalFiles,
                                ProcessedFiles = processedFiles
                            });
                            lastReportTime = now;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                CoreLog.Info("CompressAsync: cancelled, cleaning up split files");
                if (options.SplitSize > 0)
                {
                    CleanupSplitFiles(outputPath);
                }
                else if (File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); } catch (Exception cleanupEx) { CoreLog.Error("CompressAsync: failed to clean up partial output", cleanupEx); }
                }
                throw;
            }
            catch (Exception ex)
            {
                CoreLog.Error($"CompressAsync failed", ex);
                throw;
            }

            CoreLog.Info($"CompressAsync: done, {processedBytes}/{totalBytes} bytes, {sw.ElapsedMilliseconds}ms");
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }

    public async Task<IReadOnlyList<ArchiveItem>> ListEntriesAsync(string archivePath, string? password = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"ListEntriesAsync: {archivePath}");
        var sw = Stopwatch.StartNew();

        var result = await Task.Run(() =>
        {
            using var archive = OpenArchiveWithEncodingFallback(archivePath, password);

            var items = archive.Entries.Select(entry =>
            {
                var entryKey = ArchivePath.Normalize(entry.Key);
                return new ArchiveItem
                {
                    Name = entryKey,
                    FullPath = entry.IsDirectory ? entryKey.TrimEnd('/') : entryKey,
                    Size = entry.Size,
                    CompressedSize = entry.CompressedSize,
                    LastModified = entry.LastModifiedTime ?? DateTime.MinValue,
                    IsDirectory = entry.IsDirectory,
                    IsEncrypted = entry.IsEncrypted,
                    Crc32 = (int)(entry.Crc & 0xFFFFFFFF)
                };
            }).ToList();

            CoreLog.Info($"ListEntriesAsync: {items.Count} entries, {sw.ElapsedMilliseconds}ms");

            // 交叉校验：SharpCompress 报告了加密条目 → 用二进制解析直接检查中央目录的 flags
            // 这是针对 SharpCompress 在某些环境（如 Win11 日文版）的假阳性 bug
            if (items.Any(i => i.IsEncrypted))
            {
                var actuallyEncrypted = VerifyZipEncryptionFlags(archivePath);
                if (!actuallyEncrypted)
                {
                    CoreLog.Info("WARN: ListEntriesAsync: SharpCompress reported encrypted entries but binary CD flags show none — overriding IsEncrypted to false (possible false positive on {0})", archivePath);
                    foreach (var item in items)
                    {
                        item.IsEncrypted = false;
                    }
                }
            }

            return (IReadOnlyList<ArchiveItem>)items;
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
        return result;
    }

    public async Task<bool> TestArchiveAsync(string archivePath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"TestArchiveAsync: {archivePath}");

        var result = await Task.Run(() =>
        {
            try
            {
                using var archive = OpenArchiveWithEncodingFallback(archivePath, password);

                var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
                int totalFiles = entries.Count;
                int processedFiles = 0;

                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 完全解压每个条目以验证数据完整性
                    // SharpCompress 在读取完整流时内部会检测 CRC 等错误
                    using var stream = entry.OpenEntryStream();

                    long entrySize = entry.Size;
                    long totalRead = 0;
                    var lastReportTime = DateTime.Now;
                    var reportInterval = TimeSpan.FromMilliseconds(100);

                    // 文件开始：文件进度条归零
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = entry.Key ?? "",
                        PercentComplete = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 100,
                        FilePercentComplete = 0,
                        TotalFiles = totalFiles,
                        ProcessedFiles = processedFiles,
                    });

                    // 带 per-file 进度的复制循环（100ms 节流，末尾强制上报 100%）
                    var buffer = new byte[CopyBufferSize];
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var read = stream.Read(buffer, 0, buffer.Length);
                        if (read <= 0) break;
                        totalRead += read;

                        var now = DateTime.Now;
                        if (now - lastReportTime >= reportInterval || totalRead >= entrySize)
                        {
                            var filePct = entrySize > 0 ? (double)totalRead / entrySize * 100 : 100;
                            progress?.Report(new ArchiveProgress
                            {
                                CurrentFile = entry.Key ?? "",
                                PercentComplete = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 100,
                                FilePercentComplete = filePct,
                                TotalFiles = totalFiles,
                                ProcessedFiles = processedFiles,
                            });
                            lastReportTime = now;
                        }
                    }

                    processedFiles++;

                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = entry.Key ?? "",
                        PercentComplete = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 100,
                        FilePercentComplete = 100,
                        TotalFiles = totalFiles,
                        ProcessedFiles = processedFiles,
                    });
                }

                CoreLog.Info($"TestArchiveAsync: passed, {totalFiles} entries verified");
                return true;
            }
            catch (Exception ex)
            {
                CoreLog.Error($"TestArchiveAsync: failed", ex);
                return false;
            }
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
        return result;
    }

    /// <summary>
    /// 删除分卷压缩产生的所有分卷文件。
    /// </summary>
    private static void CleanupSplitFiles(string basePath)
    {
        var dir = Path.GetDirectoryName(basePath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(basePath);
        var ext = Path.GetExtension(basePath);
        for (int i = 1; i < 1000; i++)
        {
            var partPath = Path.Combine(dir, $"{name}{ext}.{i:D3}");
            if (File.Exists(partPath))
            {
                try { File.Delete(partPath); } catch (Exception cleanupEx) { CoreLog.Error("CleanupSplitFiles: failed to delete", cleanupEx); }
            }
            else
            {
                break; // 遇到断号即停止
            }
        }
    }

    /// <summary>
    /// 将 0–9 压缩级别映射到 SharpSevenZip.CompressionLevel 枚举。
    /// </summary>
    private static SharpSevenZip.CompressionLevel MapCompressionLevelToS7Z(int level) => level switch
    {
        0 => SharpSevenZip.CompressionLevel.None,
        1 or 2 => SharpSevenZip.CompressionLevel.Fast,
        3 or 4 => SharpSevenZip.CompressionLevel.Low,
        5 or 6 => SharpSevenZip.CompressionLevel.Normal,
        7 or 8 => SharpSevenZip.CompressionLevel.High,
        9 => SharpSevenZip.CompressionLevel.Ultra,
        _ => SharpSevenZip.CompressionLevel.Normal,
    };

    public async Task AddToArchiveAsync(string archivePath, string[] sourcePaths, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default, string? entryBasePath = null)
    {
        CoreLog.Entry();
        CoreLog.Info($"AddToArchiveAsync: {archivePath}, sources=[{string.Join("; ", sourcePaths)}]");
        var sw = Stopwatch.StartNew();

        await Task.Run(async () =>
        {
            // 收集需要添加的新文件
            var newFiles = new List<(string FullPath, string EntryName)>();
            foreach (var sourcePath in sourcePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Directory.Exists(sourcePath))
                {
                    var dirName = ArchivePath.GetFileName(sourcePath);
                    foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
                    {
                        // FileWhitelist（来自压缩预览过滤 B）命中时只添加匹配文件，保证 预览=实际。
                        // 白名单值为预览收集的原始绝对路径（\ 分隔），与 FileScanner 匹配方式一致，勿 Normalize。
                        if (options.FileWhitelist != null && !options.FileWhitelist.Contains(file))
                            continue;
                        var relativePath = Path.Combine(dirName, Path.GetRelativePath(sourcePath, file));
                        var entryName = string.IsNullOrEmpty(entryBasePath) ? relativePath : entryBasePath + "/" + relativePath;
                        newFiles.Add((file, entryName));
                    }
                }
                else if (File.Exists(sourcePath))
                {
                    var entryName = string.IsNullOrEmpty(entryBasePath) ? Path.GetFileName(sourcePath) : entryBasePath + "/" + Path.GetFileName(sourcePath);
                    newFiles.Add((sourcePath, entryName));
                }
            }

            if (newFiles.Count == 0)
            {
                CoreLog.Info("AddToArchiveAsync: no files to add");
                return;
            }

            // 计算旧条目信息（使用 SharpCompress IArchive 读取）——同时收集条目名/大小/时间供冲突处理
            int oldEntryCount = 0;
            long oldTotalBytes = 0;
            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingRawNames = new List<string>();
            var existingEntryInfo = new Dictionary<string, (long Size, DateTime? Modified)>(StringComparer.OrdinalIgnoreCase);
            using (var archive = OpenArchiveWithEncodingFallback(archivePath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;
                    oldTotalBytes += entry.Size;
                    oldEntryCount++;
                    var rawName = entry.Key ?? string.Empty;
                    var normalized = ArchivePath.Normalize(rawName);
                    existingNames.Add(normalized);
                    existingRawNames.Add(rawName);
                    existingEntryInfo[normalized] = (entry.Size, entry.LastModifiedTime);
                }
            }

            // 解析条目名冲突（复用解压冲突策略；语义方向反转见 AddConflictHelper）
            var occupiedNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
            var resolvedFiles = new List<(string FullPath, string EntryName)>();
            var overwrittenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (fullPath, entryName) in newFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalized = ArchivePath.Normalize(entryName);
                existingEntryInfo.TryGetValue(normalized, out var existing);
                var fi = new FileInfo(fullPath);
                var finalName = await AddConflictHelper.ResolveEntryNameAsync(
                    normalized, options, existing.Modified, existing.Size, fi.LastWriteTime, fi.Length, occupiedNames);
                if (finalName == null)
                {
                    CoreLog.Info($"AddToArchiveAsync: skipped '{entryName}' (conflict action)");
                    continue;
                }
                if (existingNames.Contains(normalized) && finalName == normalized)
                    overwrittenNames.Add(normalized); // 覆盖：copy-mode 需从 keepEntryNames 排除旧条目
                resolvedFiles.Add((fullPath, finalName));
            }

            if (resolvedFiles.Count == 0)
            {
                CoreLog.Info("AddToArchiveAsync: all files skipped by conflict handling");
                return;
            }

            long newTotalBytes = resolvedFiles.Sum(f => new FileInfo(f.FullPath).Length);
            // 总工作量 = 提取旧条目字节 + 压缩全部字节
            long workTotal = oldTotalBytes + oldTotalBytes + newTotalBytes;
            if (workTotal == 0) workTotal = 1;

            // ── Optimized copy-mode path (binary rewrite, no decompress-recompress) ──
            if (!(options.Encrypt && !string.IsNullOrEmpty(options.Password)))
            {
                string? tempArchiveFast = null;
                try
                {
                    CoreLog.Info("AddToArchiveAsync: attempting copy-mode fast path");
                    tempArchiveFast = Path.GetTempFileName() + ".zip";

                    // Detect encoding: check UTF-8 flag to choose between UTF-8 and GBK
                    var encoding = ZipHasUtf8Flag(archivePath) ? Encoding.UTF8 : Encoding.GetEncoding("gbk");

                    // Build NewEntry list from source paths with auto-cleanup
                    var newEntries = new List<NewEntry>();
                    var streamsToDispose = new List<Stream>();
                    try
                    {
                        foreach (var (fullPath, entryName) in resolvedFiles)
                        {
                            var fileStream = File.OpenRead(fullPath);
                            streamsToDispose.Add(fileStream);
                            var fi = new FileInfo(fullPath);
                            newEntries.Add(new NewEntry(
                                EntryName: ArchivePath.Normalize(entryName),
                                Data: fileStream,
                                LastModified: fi.LastWriteTime,
                                Size: fi.Length));
                        }

                        // 覆盖重名条目时排除旧条目（keepSet 存原始名 + OrdinalIgnoreCase，与 DeleteEntriesAsync 一致）
                        HashSet<string>? keepEntryNames = null;
                        if (overwrittenNames.Count > 0)
                        {
                            keepEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var raw in existingRawNames)
                                if (!overwrittenNames.Contains(ArchivePath.Normalize(raw)))
                                    keepEntryNames.Add(raw);
                        }

                        var result = ZipBinaryRewriter.RewriteAsync(
                            sourcePath: archivePath,
                            destPath: tempArchiveFast,
                            keepEntryNames: keepEntryNames,
                            addEntries: newEntries,
                            encoding: encoding,
                            comment: options.Comment,  // null = preserve original comment
                            progress: progress,
                            cancellationToken: cancellationToken).GetAwaiter().GetResult();

                        // Atomic replace (same retry pattern as legacy path)
                        for (int retry = 0; ; retry++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            try
                            {
                                File.Delete(archivePath);
                                File.Move(tempArchiveFast, archivePath);
                                break;
                            }
                            catch (IOException) when (retry < 5)
                            {
                                Thread.Sleep(100);
                            }
                        }

                        progress?.Report(new ArchiveProgress
                        {
                            CurrentFile = string.Empty,
                            PercentComplete = 100,
                            FilePercentComplete = 100
                        });

                        CoreLog.Info($"AddToArchiveAsync: copy-mode fast path done, {result.EntriesCopied} entries copied, {result.EntriesAdded} entries added");
                        return;
                    }
                    finally
                    {
                        foreach (var s in streamsToDispose)
                            s.Dispose();
                    }
                }
                catch (ZipCopyModeException)
                {
                    CoreLog.Info("AddToArchiveAsync: copy-mode not available, falling back to legacy path");
                    if (tempArchiveFast != null)
                    {
                        try { if (File.Exists(tempArchiveFast)) File.Delete(tempArchiveFast); } catch { }
                    }
                }
            }

            // 创建临时目录
            var tempDir = Path.Combine(Path.GetTempPath(), "MantisZip", "Rebuild", Guid.NewGuid().ToString());
            var tempArchive = tempDir + ".new.zip";
            try
            {
                Directory.CreateDirectory(tempDir);

                // === Phase 1: 提取旧条目到临时目录（逐文件，字节加权进度） ===
                long processedBytes = 0;
                var lastReportTime = DateTime.Now;
                var reportInterval = TimeSpan.FromMilliseconds(100);

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = "正在提取旧条目...",
                    PercentComplete = 0,
                    FilePercentComplete = 0
                });
                CoreLog.Trace("[TRACE] ZipEngine.AddToArchiveAsync: Phase 1 — extracting old entries");

                using (var archive = OpenArchiveWithEncodingFallback(archivePath, options.Password))
                {
                    // Check for encrypted entries before starting extraction.
                    // If any non-directory entry is encrypted and no password is
                    // provided, fail early with a clear message instead of relying
                    // on SharpCompress to throw CryptographiceException (which is
                    // environment/version dependent).
                    if (string.IsNullOrEmpty(options.Password))
                    {
                        var hasEncryptedEntry = archive.Entries.Any(e => !e.IsDirectory && e.IsEncrypted);
                        if (hasEncryptedEntry)
                        {
                            CoreLog.Info("AddToArchiveAsync: archive has encrypted entries but no password provided");
                            throw new InvalidOperationException(
                                "此压缩包包含加密条目，需要密码才能添加文件。 (Archive contains encrypted entries, password required to add files.)");
                        }
                    }

                    foreach (var entry in archive.Entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entryName = entry.Key ?? string.Empty;

                        if (entry.IsDirectory)
                        {
                            var dirPath = Path.Combine(tempDir, entryName);
                            if (!Directory.Exists(dirPath))
                                Directory.CreateDirectory(dirPath);
                            continue;
                        }

                        var outPath = Path.Combine(tempDir, entryName);
                        var outDir = Path.GetDirectoryName(outPath);
                        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                            Directory.CreateDirectory(outDir);

                        var entrySize = entry.Size;
                        using (var entryStream = entry.OpenEntryStream())
                        using (var outStream = File.Create(outPath))
                        {
                            var buffer = new byte[CopyBufferSize];
                            long entryProcessed = 0;
                            while (true)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var read = entryStream.Read(buffer, 0, buffer.Length);
                                if (read <= 0) break;
                                outStream.Write(buffer, 0, read);
                                entryProcessed += read;

                                var now = DateTime.Now;
                                if (now - lastReportTime >= reportInterval || entryProcessed >= entrySize)
                                {
                                    var pct = (double)(processedBytes + entryProcessed) / workTotal * 100;
                                    var filePct = entrySize > 0 ? (double)entryProcessed / entrySize * 100 : 100;
                                    progress?.Report(new ArchiveProgress
                                    {
                                        CurrentFile = "提取: " + entryName,
                                        PercentComplete = Math.Min(pct, 100),
                                        FilePercentComplete = filePct
                                    });
                                    lastReportTime = now;
                                }
                            }
                        }

                        processedBytes += entrySize;
                        try { File.SetLastWriteTime(outPath, entry.LastModifiedTime ?? DateTime.MinValue); } catch { CoreLog.Trace("ZipEngine: failed to set last write time for '{0}'", outPath); }
                    }
                }

                CoreLog.Trace($"[TRACE] ZipEngine.AddToArchiveAsync: Phase 1 done, extracted {processedBytes} bytes");

                // === Phase 2: 复制新文件到临时目录 ===
                foreach (var (fullPath, entryName) in resolvedFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var outPath = Path.Combine(tempDir, entryName);
                    var outDir = Path.GetDirectoryName(outPath);
                    if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                        Directory.CreateDirectory(outDir);
                    File.Copy(fullPath, outPath, overwrite: true);
                }

                // 扫描临时目录用于压缩
                var compressFiles = new List<(string FullPath, string RelativePath)>();
                long compressTotalBytes = 0;
                foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                {
                    var relPath = Path.GetRelativePath(tempDir, file);
                    compressFiles.Add((file, relPath));
                    compressTotalBytes += new FileInfo(file).Length;
                }
                if (compressTotalBytes == 0) compressTotalBytes = 1;
                long compressProcessed = 0;

                // === Phase 3: 重压缩（字节加权平滑进度） ===
                CoreLog.Trace($"[TRACE] ZipEngine.AddToArchiveAsync: Phase 3 — recompressing {compressFiles.Count} files, {compressTotalBytes} bytes");
                using (var fsOut = File.Create(tempArchive))
                {
                    var isEncrypted = options.Encrypt && !string.IsNullOrEmpty(options.Password);

                    if (isEncrypted)
                    {
                        // SharpSevenZip 支持 ZIP + AES-256 加密
                        fsOut.Dispose();

                        SevenZipEngine.EnsureLibraryPath();

                        var s7zCompressor = new SharpSevenZipCompressor
                        {
                            ArchiveFormat = OutArchiveFormat.Zip,
                            ZipEncryptionMethod = ZipEncryptionMethod.Aes256,
                            CompressionMethod = CompressionMethod.Deflate,
                            CompressionLevel = MapCompressionLevelToS7Z(options.CompressionLevel),
                            IncludeEmptyDirectories = true,
                            DirectoryStructure = true,
                        };

                        // 所有文件在同⼀临时目录下，计算 commonRoot 以保留相对路径结构
                        var commonRoot = tempDir.Length;
                        if (!tempDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                            commonRoot++; // 包含分隔符

                        var s7zAccumPct = 0.0;
                        var s7zCurrentFile = "";
                        s7zCompressor.FileCompressionStarted += (_, e) =>
                        {
                            s7zCurrentFile = e.FileName ?? "";
                        };
                        s7zCompressor.Compressing += (_, e) =>
                        {
                            s7zAccumPct = Math.Min(100, s7zAccumPct + e.PercentDelta);
                            var cumProcessed = processedBytes + (long)(compressTotalBytes * s7zAccumPct / 100);
                            var pct = (double)cumProcessed / workTotal * 100;
                            progress?.Report(new ArchiveProgress
                            {
                                CurrentFile = "正在压缩: " + s7zCurrentFile,
                                PercentComplete = Math.Min(pct, 100),
                                FilePercentComplete = s7zAccumPct,
                            });
                        };

                        var allFilePaths = compressFiles.Select(f => f.FullPath).ToArray();
                        if (allFilePaths.Length > 0)
                        {
                            s7zCompressor.CompressFilesEncrypted(
                                tempArchive, commonRoot,
                                options.Password ?? "", allFilePaths);
                        }

                        processedBytes += compressTotalBytes;

                        // ZIP 注释：SharpSevenZip 不支持压缩时写入，压缩后通过 EOCD 后写
                        if (!string.IsNullOrEmpty(options.Comment))
                        {
                            try { ZipCommentHelper.WriteComment(tempArchive, options.Comment); }
                            catch (Exception commentEx) { CoreLog.Error("AddToArchiveAsync: failed to write ZIP comment", commentEx); }
                        }
                    }
                    else
                    {
                        var zipEncoding = (options.FileNameEncoding?.ToLowerInvariant()) switch
                        {
                            "gbk" => Encoding.GetEncoding("GBK"),
                            "default" => Encoding.Default,
                            _ => Encoding.UTF8,
                        };
                        var writerOptions = new ZipWriterOptions(CompressionType.Deflate)
                        {
                            CompressionLevel = options.CompressionLevel,
                            ArchiveComment = options.Comment ?? "",
                            ArchiveEncoding = new ArchiveEncoding { Default = zipEncoding },
                        };
                        using var zipWriter = new ZipWriter(fsOut, writerOptions);

                        foreach (var (fullPath, relPath) in compressFiles)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var fi = new FileInfo(fullPath);
                            var entryPath = ArchivePath.Normalize(relPath);
                            var entryOptions = new ZipWriterEntryOptions
                            {
                                ModificationDateTime = fi.LastWriteTime,
                            };

                            using (var entryStream = zipWriter.WriteToStream(entryPath, entryOptions))
                            using (var fsInput = File.OpenRead(fullPath))
                            {
                                var buffer = new byte[CopyBufferSize];
                                long totalRead = 0;
                                var fiLen = fi.Length;

                                while (totalRead < fiLen)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    var read = fsInput.Read(buffer, 0, buffer.Length);
                                    if (read <= 0) break;
                                    entryStream.Write(buffer, 0, read);
                                    totalRead += read;
                                    compressProcessed += read;

                                    var now = DateTime.Now;
                                    if (now - lastReportTime >= reportInterval || totalRead >= fiLen)
                                    {
                                        var cumProcessed = processedBytes + compressProcessed;
                                        var pct = (double)cumProcessed / workTotal * 100;
                                        var filePct = fiLen > 0 ? (double)totalRead / fiLen * 100 : 100;
                                        progress?.Report(new ArchiveProgress
                                        {
                                            CurrentFile = "正在压缩: " + relPath,
                                            PercentComplete = Math.Min(pct, 100),
                                            FilePercentComplete = filePct
                                        });
                                        lastReportTime = now;
                                    }
                                }
                            }
                        }
                    }
                }

                // === Phase 4: 原子替换（带重试，应对 SharpCompress 文件句柄释放延迟） ===
                for (int retry = 0; ; retry++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        File.Delete(archivePath);
                        File.Move(tempArchive, archivePath);
                        break;
                    }
                    catch (IOException) when (retry < 5)
                    {
                        Thread.Sleep(100);
                    }
                }

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = string.Empty,
                    PercentComplete = 100,
                    FilePercentComplete = 100
                });

                CoreLog.Info($"AddToArchiveAsync: done, {newFiles.Count} files added ({oldEntryCount} old entries kept), {sw.ElapsedMilliseconds}ms");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    try { Directory.Delete(tempDir, recursive: true); } catch (Exception ex) { CoreLog.Error("AddToArchiveAsync: failed to clean up temp dir", ex); }
                if (File.Exists(tempArchive))
                    try { File.Delete(tempArchive); } catch { CoreLog.Trace("ZipEngine: failed to delete temp archive '{0}'", tempArchive); }
            }
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }

    public async Task DeleteEntriesAsync(string archivePath, string[] entryPaths, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"DeleteEntriesAsync: {archivePath}, entries=[{string.Join("; ", entryPaths)}]");
        var sw = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            var deletedSet = new HashSet<string>(entryPaths.Select(p => ArchivePath.Normalize(p)), StringComparer.OrdinalIgnoreCase);
            if (entryPaths.Length == 0)
            {
                CoreLog.Info("DeleteEntriesAsync: no entries to delete");
                return;
            }

            // ── Optimized copy-mode path (binary rewrite, no decompress-recompress) ──
            {
                string? tempArchiveFast = null;
                try
                {
                    CoreLog.Info("DeleteEntriesAsync: attempting copy-mode fast path");
                    tempArchiveFast = Path.GetTempFileName() + ".zip";

                    // Detect encoding
                    var encoding = ZipHasUtf8Flag(archivePath) ? Encoding.UTF8 : Encoding.GetEncoding("gbk");

                    // Build keep set: all entries NOT in entryPaths
                    var deletedNormalized = new HashSet<string>(entryPaths.Select(p => ArchivePath.Normalize(p)), StringComparer.OrdinalIgnoreCase);
                    var keepSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    using (var archive = OpenArchiveWithEncodingFallback(archivePath, password))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            var name = entry.Key ?? string.Empty;
                            if (!deletedNormalized.Contains(ArchivePath.Normalize(name)))
                                keepSet.Add(name);
                        }
                    }

                    if (keepSet.Count == 0)
                    {
                        // All entries to be deleted — just delete the archive
                        try { File.Delete(archivePath); } catch { }
                        CoreLog.Info("DeleteEntriesAsync: all entries deleted via copy-mode, removed archive");
                        return;
                    }

                    var result = ZipBinaryRewriter.RewriteAsync(
                        sourcePath: archivePath,
                        destPath: tempArchiveFast,
                        keepEntryNames: keepSet,
                        addEntries: null,
                        encoding: encoding,
                        comment: null,  // preserve original comment
                        progress: progress,
                        cancellationToken: cancellationToken).GetAwaiter().GetResult();

                    // Atomic replace
                    for (int retry = 0; ; retry++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            File.Delete(archivePath);
                            File.Move(tempArchiveFast, archivePath);
                            break;
                        }
                        catch (IOException) when (retry < 5)
                        {
                            Thread.Sleep(100);
                        }
                    }

                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = string.Empty,
                        PercentComplete = 100,
                        FilePercentComplete = 100
                    });

                    CoreLog.Info($"DeleteEntriesAsync: copy-mode fast path done, {result.EntriesCopied} entries kept");
                    return;
                }
                catch (ZipCopyModeException)
                {
                    CoreLog.Info("DeleteEntriesAsync: copy-mode not available, falling back to legacy path");
                    if (tempArchiveFast != null)
                    {
                        try { if (File.Exists(tempArchiveFast)) File.Delete(tempArchiveFast); } catch { }
                    }
                }
            }

            // 创建临时目录和工作文件
            var tempDir = Path.Combine(Path.GetTempPath(), "MantisZip", "DeleteTemp", Guid.NewGuid().ToString());
            var tempArchive = tempDir + ".new.zip";

            // 使用 SharpCompress IArchive 完成验证 + 确定保留项 + 提取
            long totalKeepBytes = 0;
            int keepEntryCount = 0;
            long workTotal = 1;
            long processedBytes = 0;
            var lastReportTime = DateTime.Now;
            var reportInterval = TimeSpan.FromMilliseconds(100);

            try
            {
                Directory.CreateDirectory(tempDir);

                // Pass 1: 验证 + 确定保留项
                var keepNames = new List<string>();
                using (var archive = OpenArchiveWithEncodingFallback(archivePath, password))
                {
                    var allNames = new List<string>();
                    foreach (var entry in archive.Entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var name = entry.Key ?? string.Empty;
                        allNames.Add(name);
                    }

                    // 验证要删除的条目都存在
                    var entryNameSet = new HashSet<string>(allNames);
                    foreach (var entryPath in entryPaths)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var normalized = ArchivePath.Normalize(entryPath);
                        if (!entryNameSet.Contains(normalized))
                        {
                            CoreLog.Error($"DeleteEntriesAsync: entry not found: {entryPath}");
                            throw new FileNotFoundException($"压缩包中不存在条目: {entryPath}", entryPath);
                        }
                    }

                    foreach (var name in allNames)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var normalized = ArchivePath.Normalize(name);
                        if (!deletedSet.Contains(normalized))
                        {
                            keepNames.Add(name);
                        }
                    }
                }

                keepEntryCount = keepNames.Count;
                if (keepEntryCount == 0)
                {
                    // 所有条目都被删除 — 删除原文件后返回
                    try { File.Delete(archivePath); } catch { CoreLog.Trace("ZipEngine: failed to delete empty archive '{0}'", archivePath); }
                    CoreLog.Info("DeleteEntriesAsync: all entries deleted, removed archive");
                    return;
                }

                // ── Check if source archive is encrypted ──
                bool sourceIsEncrypted = false;
                using (var checkArchive = OpenArchiveWithEncodingFallback(archivePath, password))
                {
                    sourceIsEncrypted = checkArchive.Entries.Any(e => e.IsEncrypted);
                }

                // Pass 2: 提取保留条目到临时目录（带进度）
                using (var archive = OpenArchiveWithEncodingFallback(archivePath, password))
                {
                    // 先算 totalKeepBytes
                    foreach (var entry in archive.Entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (entry.IsDirectory) continue;
                        var name = entry.Key ?? string.Empty;
                        if (keepNames.Contains(name))
                            totalKeepBytes += entry.Size;
                    }
                }

                workTotal = totalKeepBytes + totalKeepBytes;
                if (workTotal == 0) workTotal = 1;

                // Pass 3: 实际提取
                using (var archive = OpenArchiveWithEncodingFallback(archivePath, password))
                {
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = "正在提取保留条目...",
                        PercentComplete = 0
                    });

                    foreach (var entry in archive.Entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entryName = entry.Key ?? string.Empty;

                        if (!keepNames.Contains(entryName))
                            continue;

                        if (entry.IsDirectory)
                        {
                            var dirPath = Path.Combine(tempDir, entryName);
                            if (!Directory.Exists(dirPath))
                                Directory.CreateDirectory(dirPath);
                            continue;
                        }

                        var outPath = Path.Combine(tempDir, entryName);
                        var outDir = Path.GetDirectoryName(outPath);
                        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                            Directory.CreateDirectory(outDir);

                        var entrySize = entry.Size;
                        using (var entryStream = entry.OpenEntryStream())
                        using (var outStream = File.Create(outPath))
                        {
                            var buffer = new byte[CopyBufferSize];
                            long entryProcessed = 0;
                            while (true)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var read = entryStream.Read(buffer, 0, buffer.Length);
                                if (read <= 0) break;
                                outStream.Write(buffer, 0, read);
                                entryProcessed += read;

                                var now = DateTime.Now;
                                if (now - lastReportTime >= reportInterval || entryProcessed >= entrySize)
                                {
                                    var pct = (double)(processedBytes + entryProcessed) / workTotal * 100;
                                    var filePct = entrySize > 0 ? (double)entryProcessed / entrySize * 100 : 100;
                                    progress?.Report(new ArchiveProgress
                                    {
                                        CurrentFile = "提取: " + entryName,
                                        PercentComplete = Math.Min(pct, 100),
                                        FilePercentComplete = filePct
                                    });
                                    lastReportTime = now;
                                }
                            }
                        }

                        processedBytes += entrySize;
                        try { File.SetLastWriteTime(outPath, entry.LastModifiedTime ?? DateTime.MinValue); } catch { CoreLog.Trace("ZipEngine: failed to set last write time for '{0}'", outPath); }
                    }
                }

                // === Phase 2: 重压缩保留条目 ===
                var compressFiles = new List<(string FullPath, string RelativePath)>();
                long compressTotalBytes = 0;
                foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                {
                    var relPath = Path.GetRelativePath(tempDir, file);
                    compressFiles.Add((file, relPath));
                    compressTotalBytes += new FileInfo(file).Length;
                }
                if (compressTotalBytes == 0) compressTotalBytes = 1;
                long compressProcessed = 0;

                CoreLog.Trace($"[TRACE] ZipEngine.DeleteEntriesAsync: Phase 2 — recompressing {compressFiles.Count} files, {compressTotalBytes} bytes");

                if (sourceIsEncrypted && !string.IsNullOrEmpty(password))
                {
                    // SharpSevenZip 加密路径
                    using (var fsOut = File.Create(tempArchive))
                    {
                        fsOut.Dispose();

                        SevenZipEngine.EnsureLibraryPath();

                        var s7zCompressor = new SharpSevenZipCompressor
                        {
                            ArchiveFormat = OutArchiveFormat.Zip,
                            ZipEncryptionMethod = ZipEncryptionMethod.Aes256,
                            CompressionMethod = CompressionMethod.Deflate,
                            CompressionLevel = SharpSevenZip.CompressionLevel.Normal,
                            IncludeEmptyDirectories = true,
                            DirectoryStructure = true,
                        };

                        var s7zAccumPct = 0.0;
                        var s7zCurrentFile = "";
                        s7zCompressor.FileCompressionStarted += (_, e) =>
                        {
                            s7zCurrentFile = e.FileName ?? "";
                        };
                        s7zCompressor.Compressing += (_, e) =>
                        {
                            s7zAccumPct = Math.Min(100, s7zAccumPct + e.PercentDelta);
                            var cumProcessed = processedBytes + (long)(compressTotalBytes * s7zAccumPct / 100);
                            var pct = (double)cumProcessed / workTotal * 100;
                            progress?.Report(new ArchiveProgress
                            {
                                CurrentFile = "正在压缩: " + s7zCurrentFile,
                                PercentComplete = Math.Min(pct, 100),
                                FilePercentComplete = s7zAccumPct,
                            });
                        };

                        var commonRoot = tempDir.Length;
                        if (!tempDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                            commonRoot++;

                        var allFilePaths = compressFiles.Select(f => f.FullPath).ToArray();
                        if (allFilePaths.Length > 0)
                        {
                            s7zCompressor.CompressFilesEncrypted(
                                tempArchive, commonRoot,
                                password ?? "", allFilePaths);
                        }
                    }
                }
                else
                {
                    // 非加密路径：SharpCompress ZipWriter（原实现）
                    using (var fsOut = File.Create(tempArchive))
                    {
                        var writerOptions = new ZipWriterOptions(CompressionType.Deflate)
                        {
                            CompressionLevel = 6,
                            ArchiveEncoding = new ArchiveEncoding { Default = Encoding.UTF8 },
                        };
                        using var zipWriter = new ZipWriter(fsOut, writerOptions);

                        foreach (var (fullPath, relPath) in compressFiles)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var fi = new FileInfo(fullPath);
                            var entryPath = ArchivePath.Normalize(relPath);
                            var entryOptions = new ZipWriterEntryOptions
                            {
                                ModificationDateTime = fi.LastWriteTime,
                            };

                            using (var entryStream = zipWriter.WriteToStream(entryPath, entryOptions))
                            using (var fsInput = File.OpenRead(fullPath))
                            {
                                var buffer = new byte[CopyBufferSize];
                                long totalRead = 0;
                                var fiLen = fi.Length;

                                while (totalRead < fiLen)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    var read = fsInput.Read(buffer, 0, buffer.Length);
                                    if (read <= 0) break;
                                    entryStream.Write(buffer, 0, read);
                                    totalRead += read;
                                    compressProcessed += read;

                                    var now = DateTime.Now;
                                    if (now - lastReportTime >= reportInterval || totalRead >= fiLen)
                                    {
                                        var cumProcessed = processedBytes + compressProcessed;
                                        var pct = (double)cumProcessed / workTotal * 100;
                                        var filePct = fiLen > 0 ? (double)totalRead / fiLen * 100 : 100;
                                        progress?.Report(new ArchiveProgress
                                        {
                                            CurrentFile = "正在压缩: " + relPath,
                                            PercentComplete = Math.Min(pct, 100),
                                            FilePercentComplete = filePct
                                        });
                                        lastReportTime = now;
                                    }
                                }
                            }
                        }
                    }
                }

                // === Phase 3: 原子替换（带重试，应对 SharpCompress 文件句柄释放延迟） ===
                for (int retry = 0; ; retry++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        File.Delete(archivePath);
                        File.Move(tempArchive, archivePath);
                        break;
                    }
                    catch (IOException) when (retry < 5)
                    {
                        Thread.Sleep(100);
                    }
                }

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = string.Empty,
                    PercentComplete = 100,
                    FilePercentComplete = 100
                });

                CoreLog.Info($"DeleteEntriesAsync: done, {entryPaths.Length} entries deleted ({keepEntryCount} kept), {sw.ElapsedMilliseconds}ms");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    try { Directory.Delete(tempDir, recursive: true); } catch { CoreLog.Trace("ZipEngine: failed to delete temp dir '{0}'", tempDir); }
                if (File.Exists(tempArchive))
                    try { File.Delete(tempArchive); } catch { CoreLog.Trace("ZipEngine: failed to delete temp archive '{0}'", tempArchive); }
            }
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }

    // ReadFileWithRetryZipOutputStream 已删除（SharpZipLib 加密回退已被 SharpSevenZip 替代）

    /// <summary>
    /// 带重试/跳过/中止的文件压缩读取（SharpCompress ZipWriter 路径）。
    /// 使用 WriteToStream 获取可写流以支持字节加权进度报告。
    /// 返回 false 表示跳过此文件。
    /// </summary>
    private bool ReadFileWithRetry(string fullPath, string relativePath,
        ArchiveOptions options, ZipWriter zipWriter, ref long processedBytes, long totalBytes,
        int totalFiles, ref int processedFiles,
        CancellationToken ct, IProgress<ArchiveProgress>? progress, ref DateTime lastReportTime)
    {
        int retries = 3;
        while (retries > 0)
        {
            try
            {
                var fi = new FileInfo(fullPath);
                var entryOptions = new ZipWriterEntryOptions
                {
                    ModificationDateTime = fi.LastWriteTime,
                };

                var entryPath = ArchivePath.Normalize(relativePath);
                using (var entryStream = zipWriter.WriteToStream(entryPath, entryOptions))
                using (var fsInput = File.OpenRead(fullPath))
                {
                    var buffer = new byte[CopyBufferSize];
                    long totalRead = 0;
                    var fiLen = fi.Length;

                    while (totalRead < fiLen)
                    {
                        ct.ThrowIfCancellationRequested();
                        var read = fsInput.Read(buffer, 0, buffer.Length);
                        if (read <= 0) break;
                        entryStream.Write(buffer, 0, read);
                        totalRead += read;
                        processedBytes += read;

                        var now = DateTime.Now;
                        if (now - lastReportTime >= TimeSpan.FromMilliseconds(100) || totalRead >= fiLen)
                        {
                            var pct = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : 0;
                            var filePct = fiLen > 0 ? (double)totalRead / fiLen * 100 : 100;
                            progress?.Report(new ArchiveProgress
                            {
                                CurrentFile = "正在压缩: " + relativePath,
                                PercentComplete = pct,
                                FilePercentComplete = filePct,
                                TotalFiles = totalFiles,
                                ProcessedFiles = processedFiles
                            });
                            lastReportTime = now;
                        }
                    }
                }
                processedFiles++;
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                retries--;
                if (options?.ErrorResolver == null)
                {
                    if (retries <= 0) throw;
                    continue;
                }

                var action = options.ErrorResolver(new FileErrorInfo
                {
                    FilePath = fullPath,
                    ErrorMessage = ex.Message,
                    RetriesRemaining = retries
                });

                if (action == FileErrorAction.Retry)
                {
                    continue;
                }
                if (action == FileErrorAction.Skip)
                {
                    return false;
                }
                throw;
            }
        }
        return false;
    }

    /// <summary>
    /// 使用二进制解析直接读取 ZIP 中央目录的通用位标记，
    /// 验证是否有任何条目的加密位（bit 0）被设置。
    /// 用于交叉校验 SharpCompress 报告的 IsEncrypted，防范假阳性。
    /// </summary>
    internal static bool VerifyZipEncryptionFlags(string archivePath)
    {
        try
        {
            using var fs = File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var (cdOffset, entryCount, _) = ZipBinaryRewriter.ReadEocd(fs);
            var entries = ZipBinaryRewriter.ReadCentralDirectory(fs, cdOffset, entryCount);
            bool encrypted = entries.Any(e => (e.Flags & 0x0001) != 0);
            CoreLog.Trace("VerifyZipEncryptionFlags: {0} entries, encrypted={1}", entryCount, encrypted);
            return encrypted;
        }
        catch (Exception ex)
        {
            // Zip64 或其它无法解析的情况 → 保守信任 SharpCompress
            CoreLog.Trace("VerifyZipEncryptionFlags: failed to verify (will trust SharpCompress): {0}", ex.Message);
            return true;
        }
    }
}
