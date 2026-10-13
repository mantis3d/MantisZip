using System.Diagnostics;
using System.Collections.Generic;
using SharpCompress.Common;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Readers;
using SharpCompress.Readers.Tar;
using SharpCompress.Writers.Tar;
using SharpCompress.Writers.GZip;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using System.IO;
using System.Text;

namespace MantisZip.Core.Engines;

/// <summary>
/// TAR/GZ 压缩引擎
/// </summary>
public class TarGzEngine : IArchiveEngine
{
    private const int CopyBufferSize = 4194304;
    public bool CanHandle(ArchiveFormat format) => format == ArchiveFormat.Tar || format == ArchiveFormat.GZip;

    public async Task<ExtractResult> ExtractAsync(string archivePath, string destinationPath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default, ArchiveOptions? options = null)
    {
        CoreLog.Entry();
        if (!string.IsNullOrEmpty(password))
            CoreLog.Info($"WARN: TarGzEngine.ExtractAsync: password provided but TAR/GZ format does not support encryption (archive: {archivePath})");
        CoreLog.Info($"ExtractAsync: {archivePath} -> {destinationPath}");
        var sw = Stopwatch.StartNew();

        var result = await Task.Run(async () =>
        {
            var ext = Path.GetExtension(archivePath).ToLowerInvariant();
            var isTarGz = ext == ".tgz" || archivePath.EndsWith(".tar.gz");
            CoreLog.Info($"ExtractAsync: format=tar.gz={isTarGz}, ext={ext}");

            int successCount = 0, failedEntries = 0;
            var conflictStats = new ConflictStatsCounter();
            long bytesTotal = 0; // 链尾最终报告使用；tar 分支=压缩流长度，gz 分支=gz 流长度

            if (isTarGz || ext == ".tar")
            {
                // TAR 或 TAR.GZ 解压 - 使用 SharpCompress TarReader
                // 注意：不手动解压 GZip，直接传入原始压缩流让 TarReader 自动检测 gzip 头
                using var inputStream = File.OpenRead(archivePath);
                var totalCompressedBytes = inputStream.Length;
                bytesTotal = totalCompressedBytes;

                using var reader = TarReader.OpenReader(inputStream, new ReaderOptions { LookForHeader = true });

                int fileIndex = 0;
                var lastReportTime = DateTime.Now;
                var reportInterval = TimeSpan.FromMilliseconds(100);

                while (reader.MoveToNextEntry())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entry = reader.Entry;
                    var entryKey = entry.Key ?? string.Empty;
                    if (entry.IsDirectory)
                    {
                        var dirPath = FileConflictHelper.GetSafePath(destinationPath, entryKey);
                        if (!Directory.Exists(dirPath))
                            Directory.CreateDirectory(dirPath);
                        continue;
                    }

                    fileIndex++;
                    var compressedProgress = totalCompressedBytes > 0
                        ? (double)inputStream.Position / totalCompressedBytes * 100
                        : 0;

                    var outputFilePath = FileConflictHelper.GetSafePath(destinationPath, entryKey);
                    var outDir = Path.GetDirectoryName(outputFilePath);
                    if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                        Directory.CreateDirectory(outDir);

                    var entryModified = entry.LastModifiedTime ?? DateTime.MinValue;

                    // 逐文件报告
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        PercentComplete = Math.Min(compressedProgress, 99.9),
                        FilePercentComplete = 0,
                        TotalBytes = totalCompressedBytes,
                        ProcessedBytes = inputStream.Position
                    }));

                    // 冲突处理
                    var existedBefore = File.Exists(outputFilePath);
                    var resolved = await FileConflictHelper.ResolvePathAsync(outputFilePath, options, entryModified, entry.Size);
                    if (resolved == null)
                    {
                        conflictStats.RecordSkipped();
                        // 逐条目状态（D2）：冲突跳过 → Skipped
                        progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Skipped });
                        // 跳过文件，TarReader.MoveToNextEntry 自动处理流推进
                        continue;
                    }
                    if (existedBefore && resolved == outputFilePath)
                    {
                        conflictStats.RecordOverwritten();
                        // 逐条目状态（D2）：覆盖旧文件 → Overwritten
                        progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Overwritten });
                    }

                    try
                    {
                        // 带 per-file 进度的复制
                        using (var entryStream = reader.OpenEntryStream())
                        using (var outStream = File.Create(resolved))
                        {
                            var buffer = new byte[CopyBufferSize];
                            long totalRead = 0;
                            long entrySize = entry.Size;

                            while (true)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var read = entryStream.Read(buffer, 0, buffer.Length);
                                if (read <= 0) break;
                                outStream.Write(buffer, 0, read);
                                totalRead += read;

                                var now = DateTime.Now;
                                if (now - lastReportTime >= reportInterval || totalRead >= entrySize)
                                {
                                    var filePct = entrySize > 0 ? (double)totalRead / entrySize * 100 : 100;
                                    compressedProgress = totalCompressedBytes > 0
                                        ? (double)inputStream.Position / totalCompressedBytes * 100
                                        : 0;
                                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                                    {
                                        CurrentFile = entryKey,
                                        PercentComplete = Math.Min(compressedProgress, 99.9),
                                        FilePercentComplete = filePct,
                                        TotalBytes = totalCompressedBytes,
                                        ProcessedBytes = inputStream.Position
                                    }));
                                    lastReportTime = now;
                                }
                            }
                        }
                        // 恢复文件原始修改时间
                        try { File.SetLastWriteTime(resolved, entryModified); } catch (Exception tsEx) { CoreLog.Info($"ExtractAsync: failed to set timestamp on {resolved}: {tsEx.Message}"); }
                        successCount++;
                        // 逐条目状态（D2）：写盘成功 → Completed（Overwritten 已在覆盖判定处上报，不重复覆盖）
                        if (!(existedBefore && resolved == outputFilePath))
                            progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Completed });
                    }
                    catch (UnauthorizedAccessException uax)
                    {
                        CoreLog.Info($"ExtractAsync: permission denied for '{entryKey}': {uax.Message}");
                        conflictStats.RecordFailed();
                        // 逐条目状态（D2）：权限失败 → Failed
                        progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed });
                        failedEntries++;
                    }
                    catch (IOException iox)
                    {
                        // 目标文件被其他进程占用等 IO 失败：跳过该条目继续，避免单个文件中止整个解压
                        CoreLog.Info($"ExtractAsync: write failed for '{entryKey}': {iox.Message}");
                        conflictStats.RecordFailed();
                        // 逐条目状态（D2）：IO 写入失败 → Failed
                        progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed });
                        failedEntries++;
                    }
                }
            }
            else if (ext == ".gz")
            {
                // 单纯 GZip 解压单个文件
                var gzEntryKey = Path.GetFileNameWithoutExtension(archivePath);
                using var inputStream = File.OpenRead(archivePath);
                using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
                var outputPath = Path.Combine(destinationPath, gzEntryKey);
                var existedBefore = File.Exists(outputPath);
                var resolved = await FileConflictHelper.ResolvePathAsync(outputPath, options);
                if (resolved == null)
                {
                    conflictStats.RecordSkipped();
                    // 逐条目状态（D2）：冲突跳过 → Skipped
                    progress?.Report(new ArchiveProgress { EntryKey = gzEntryKey, EntryStatus = ArchiveEntryStatus.Skipped });
                }
                else
                {
                    if (existedBefore && resolved == outputPath)
                    {
                        conflictStats.RecordOverwritten();
                        // 逐条目状态（D2）：覆盖旧文件 → Overwritten
                        progress?.Report(new ArchiveProgress { EntryKey = gzEntryKey, EntryStatus = ArchiveEntryStatus.Overwritten });
                    }
                    try
                    {
                        using var output = File.Create(resolved);
                        // 手动缓冲循环：以压缩流位置（inputStream.Position）为字节进度；单文件 gz 无文件计数语义
                        long gzLen = inputStream.Length;
                        bytesTotal = gzLen;
                        long gzLastReport = Environment.TickCount64;
                        progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                        {
                            CurrentFile = gzEntryKey,
                            PercentComplete = 0,
                            FilePercentComplete = 0,
                            TotalBytes = gzLen,
                            ProcessedBytes = 0,
                        }));
                        var gzBuffer = new byte[CopyBufferSize];
                        int gzRead;
                        while ((gzRead = gzipStream.Read(gzBuffer, 0, gzBuffer.Length)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            output.Write(gzBuffer, 0, gzRead);
                            if (Environment.TickCount64 - gzLastReport >= 100)
                            {
                                gzLastReport = Environment.TickCount64;
                                double gzPct = gzLen > 0 ? Math.Min(inputStream.Position * 100.0 / gzLen, 99.9) : 0;
                                progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                                {
                                    CurrentFile = gzEntryKey,
                                    PercentComplete = gzPct,
                                    TotalBytes = gzLen,
                                    ProcessedBytes = inputStream.Position,
                                }));
                            }
                        }
                        successCount = 1;
                        // 逐条目状态（D2）：写盘成功 → Completed（Overwritten 已在覆盖判定处上报，不重复覆盖）
                        if (!(existedBefore && resolved == outputPath))
                            progress?.Report(new ArchiveProgress { EntryKey = gzEntryKey, EntryStatus = ArchiveEntryStatus.Completed });
                    }
                    catch (UnauthorizedAccessException uax)
                    {
                        CoreLog.Info($"ExtractAsync: permission denied for '{outputPath}': {uax.Message}");
                        conflictStats.RecordFailed();
                        // 逐条目状态（D2）：权限失败 → Failed
                        progress?.Report(new ArchiveProgress { EntryKey = gzEntryKey, EntryStatus = ArchiveEntryStatus.Failed });
                        failedEntries = 1;
                    }
                    catch (IOException iox)
                    {
                        CoreLog.Info($"ExtractAsync: write failed for '{outputPath}': {iox.Message}");
                        conflictStats.RecordFailed();
                        // 逐条目状态（D2）：IO 写入失败 → Failed
                        progress?.Report(new ArchiveProgress { EntryKey = gzEntryKey, EntryStatus = ArchiveEntryStatus.Failed });
                        failedEntries = 1;
                    }
                }

                progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                {
                    CurrentFile = Path.GetFileName(outputPath),
                    PercentComplete = 100
                }));
            }

            progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100,
                TotalBytes = bytesTotal,
                ProcessedBytes = bytesTotal
            }));

            CoreLog.Info($"ExtractAsync: done, {sw.ElapsedMilliseconds}ms, failedEntries={failedEntries}");
            var tarStats = conflictStats.Snapshot;
            return new ExtractResult { SucceededEntries = successCount, FailedEntries = failedEntries, SkippedEntries = tarStats.Skipped, OverwrittenEntries = tarStats.Overwritten };
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
        return result;
    }

    public async Task CompressAsync(string[] sourcePaths, string outputPath, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"CompressAsync: [{string.Join("; ", sourcePaths)}] -> {outputPath}");
        var sw = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            try
            {
                var ext = Path.GetExtension(outputPath).ToLowerInvariant();
                var isTarGz = ext == ".tgz" || outputPath.EndsWith(".tar.gz");
                CoreLog.Info($"CompressAsync: format=tar.gz={isTarGz}");

                var (files, _) = FileScanner.CollectFiles(sourcePaths, progress, cancellationToken, options.FileWhitelist);
                CoreLog.Info($"CompressAsync: {files.Count} files to compress");

                long totalSourceBytes = 0; // 源文件未压缩字节总和（tar 逐文件累加，gz 为单文件大小）

                if (isTarGz || ext == ".tar")
                {
                    using var fileStream = File.Create(outputPath);
                    var compressionType = isTarGz ? CompressionType.GZip : CompressionType.None;
                    using SharpCompress.Writers.IWriter writer = TarWriter.OpenWriter(fileStream, new TarWriterOptions(compressionType, true)
                    {
                        CompressionLevel = options.CompressionLevel
                    });

                    foreach (var (fullPath, _) in files)
                        totalSourceBytes += SafeFileSize(fullPath);
                    long bytesDone = 0;

                    int processedFiles = 0;
                    int totalFiles = files.Count;
                    foreach (var (fullPath, relativePath) in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        long srcSize = SafeFileSize(fullPath);

                        progress?.Report(new ArchiveProgress
                        {
                            CurrentFile = relativePath,
                            // percent 语义保持文件计数基准不变（字节只挂在 ProcessedBytes 上）
                            PercentComplete = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 0,
                            FilePercentComplete = 0,
                            TotalFiles = totalFiles,
                            ProcessedFiles = processedFiles,
                            TotalBytes = totalSourceBytes,
                            ProcessedBytes = bytesDone
                        });

                        long lastByteReport = Environment.TickCount64;
                        if (!TarWriteFileWithRetry(fullPath, relativePath, options, writer, cancellationToken,
                                onRead: read =>
                                {
                                    if (Environment.TickCount64 - lastByteReport < 100 && read < srcSize) return;
                                    lastByteReport = Environment.TickCount64;
                                    progress?.Report(new ArchiveProgress
                                    {
                                        CurrentFile = relativePath,
                                        PercentComplete = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 0,
                                        FilePercentComplete = srcSize > 0 ? Math.Min(read, srcSize) * 100.0 / srcSize : 100,
                                        TotalFiles = totalFiles,
                                        ProcessedFiles = processedFiles,
                                        TotalBytes = totalSourceBytes,
                                        ProcessedBytes = bytesDone + Math.Min(read, srcSize)
                                    });
                                }))
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            // 逐条目状态（D2）：读取失败被跳过（ErrorResolver Skip 或重试耗尽）→ Skipped
                            progress?.Report(new ArchiveProgress { EntryKey = relativePath, EntryStatus = ArchiveEntryStatus.Skipped });
                            bytesDone += srcSize; // 跳过也计入已处理字节（ZipEngine.cs 先例）
                            continue;
                        }
                        processedFiles++;
                        bytesDone += srcSize;

                        progress?.Report(new ArchiveProgress
                        {
                            CurrentFile = relativePath,
                            PercentComplete = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 0,
                            FilePercentComplete = 100,
                            TotalFiles = totalFiles,
                            ProcessedFiles = processedFiles,
                            // 逐条目状态（D2）：本文件写入完成 → Completed（复用既有上报，非节流路径）
                            EntryKey = relativePath,
                            EntryStatus = ArchiveEntryStatus.Completed
                        });
                    }
                }
                else if (ext == ".gz")
                {
                    // 单纯 GZip 压缩（单文件）— 单文件无文件计数语义，percent 采用 byte 基准
                    if (files.Count > 0)
                    {
                        using var outputStream = File.Create(outputPath);
                        using var gzipWriter = GZipWriter.OpenWriter(outputStream, new GZipWriterOptions(options.CompressionLevel));

                        long gzSrcSize = SafeFileSize(files[0].FullPath);
                        totalSourceBytes = gzSrcSize;
                        long gzLastReport = Environment.TickCount64;
                        // 共享读：源文件可能正被编辑器以写权限持有
                        using var input = SharedReadStream.OpenRead(files[0].FullPath);
                        using var progressInput = new ReadProgressStream(input, read =>
                        {
                            if (Environment.TickCount64 - gzLastReport < 100 && read < gzSrcSize) return;
                            gzLastReport = Environment.TickCount64;
                            double gzPct = gzSrcSize > 0 ? Math.Min(read * 100.0 / gzSrcSize, 99.9) : 0;
                            progress?.Report(new ArchiveProgress
                            {
                                CurrentFile = Path.GetFileName(files[0].FullPath),
                                PercentComplete = gzPct,
                                FilePercentComplete = gzSrcSize > 0 ? Math.Min(read * 100.0 / gzSrcSize, 100) : 100,
                                TotalBytes = gzSrcSize,
                                ProcessedBytes = read,
                            });
                        });
                        gzipWriter.Write(Path.GetFileName(files[0].FullPath), progressInput, null);
                    }
                }

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = string.Empty,
                    PercentComplete = 100,
                    TotalFiles = files.Count,
                    ProcessedFiles = files.Count,
                    TotalBytes = totalSourceBytes,
                    ProcessedBytes = totalSourceBytes
                });

                CoreLog.Info($"CompressAsync: done, {sw.ElapsedMilliseconds}ms");
            }
            catch (OperationCanceledException)
            {
                CoreLog.Info("CompressAsync: cancelled, cleaning up partial output");
                if (File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); } catch (Exception cleanupEx) { CoreLog.Error("CompressAsync: failed to clean up partial output", cleanupEx); }
                }
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }

    /// <summary>
    /// 求单文件未压缩字节数；目录或读取失败（含属性查询失败）返回 0，
    /// 不阻断压缩（字节进度是尽力而为）。语义对齐 SevenZipEngine.SumSourceFileSizes 的单文件版。
    /// </summary>
    private static long SafeFileSize(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.Directory) != 0) return 0;
            return new FileInfo(path).Length;
        }
        catch (Exception ex)
        {
            CoreLog.Trace($"SafeFileSize skip {path}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// 带重试/跳过/中止的 TAR 文件压缩。返回 false 表示跳过此文件。
    /// </summary>
    /// <param name="onRead">可选的已读字节回调（传入时源流经 <see cref="ReadProgressStream"/> 包装，用于字节进度）。</param>
    private static bool TarWriteFileWithRetry(string fullPath, string relativePath,
        ArchiveOptions options, SharpCompress.Writers.IWriter writer, CancellationToken ct,
        Action<long>? onRead = null)
    {
        int retries = 3;
        while (retries > 0)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var fi = new FileInfo(fullPath);
                // 共享读：源文件可能正被 Word 等编辑器以写权限持有，File.OpenRead 会直接冲突
                using var sourceStream = SharedReadStream.OpenRead(fullPath);
                using Stream readStream = onRead is null
                    ? sourceStream
                    : new ReadProgressStream(sourceStream, onRead);
                // ReadProgressStream 保持可寻（TarWriter.Write 无显式 size 时会读 source.Length）
                writer.Write(relativePath, readStream, fi.LastWriteTime);
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CoreLog.Trace("ExtractAsync: IO error on '{0}': {1}", relativePath, ex.Message);
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

                if (action == FileErrorAction.Retry) continue;
                if (action == FileErrorAction.Skip) return false;
                throw;
            }
        }
        return false;
    }

    public async Task<IReadOnlyList<ArchiveItem>> ListEntriesAsync(string archivePath, string? password = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        if (!string.IsNullOrEmpty(password))
            CoreLog.Info($"WARN: TarGzEngine.ListEntriesAsync: password provided but TAR/GZ format does not support encryption (archive: {archivePath})");
        CoreLog.Info($"ListEntriesAsync: {archivePath}");
        var sw = Stopwatch.StartNew();

        var result = await Task.Run(() =>
        {
            var items = new List<ArchiveItem>();
            var ext = Path.GetExtension(archivePath).ToLowerInvariant();
            var isTarGz = ext == ".tgz" || archivePath.EndsWith(".tar.gz");

            if (ext == ".tar" || isTarGz)
            {
                // 注意：解析异常必须向上传播（不移除 catch 曾吞掉损坏 tar 的 IncorrectArchiveException，
                // 导致损坏包静默打开为空）。TarReader 对合法 tar 与空 tar 均能正常返回。
                using var inputStream = File.OpenRead(archivePath);
                using var reader = TarReader.OpenReader(inputStream, new ReaderOptions { LookForHeader = true });
                while (reader.MoveToNextEntry())
                {
                    var entry = reader.Entry;
                    var entryKey = entry.Key ?? string.Empty;
                    items.Add(new ArchiveItem
                    {
                        Name = entryKey,
                        FullPath = entry.IsDirectory ? entryKey.TrimEnd('/') : entryKey,
                        Size = entry.Size,
                        CompressedSize = entry.Size,
                        LastModified = entry.LastModifiedTime ?? DateTime.MinValue,
                        IsDirectory = entry.IsDirectory,
                        IsEncrypted = false
                    });
                }
            }
            else if (ext == ".gz")
            {
                var fi = new FileInfo(archivePath);
                items.Add(new ArchiveItem
                {
                    Name = Path.GetFileNameWithoutExtension(archivePath),
                    FullPath = Path.GetFileNameWithoutExtension(archivePath),
                    Size = fi.Length,
                    CompressedSize = fi.Length,
                    LastModified = fi.LastWriteTime,
                    IsDirectory = false,
                    IsEncrypted = false
                });
            }

            CoreLog.Info($"ListEntriesAsync: {items.Count} entries, {sw.ElapsedMilliseconds}ms");
            return (IReadOnlyList<ArchiveItem>)items;
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
        return result;
    }

    public async Task DeleteEntriesAsync(string archivePath, string[] entryPaths, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default, ArchiveOptions? options = null)
    {
        CoreLog.Entry();
        // options 仅 ZipEngine 使用（TAR/GZ 不支持删除，始终抛 NotSupportedException）
        _ = options;
        if (!string.IsNullOrEmpty(password))
            CoreLog.Info($"WARN: TarGzEngine.DeleteEntriesAsync: password provided but TAR/GZ format does not support encryption (archive: {archivePath})");
        CoreLog.Info($"DeleteEntriesAsync: {archivePath} — NotSupportedException");
        try
        {
            await Task.Run(() =>
            {
                throw new NotSupportedException("TAR/GZ 格式不支持直接删除文件，请重新创建压缩包");
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CoreLog.Exit();
        }
    }

    public async Task AddToArchiveAsync(string archivePath, string[] sourcePaths, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default, string? entryBasePath = null)
    {
        CoreLog.Entry();
        if (!string.IsNullOrEmpty(options.Password))
            CoreLog.Info($"WARN: TarGzEngine.AddToArchiveAsync: password provided but TAR/GZ format does not support encryption (archive: {archivePath})");
        CoreLog.Info($"AddToArchiveAsync: {archivePath} — NotSupportedException");
        try
        {
            await Task.Run(() =>
            {
                throw new NotSupportedException("TAR/GZ 格式不支持直接添加文件，请重新创建压缩包");
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CoreLog.Exit();
        }
    }

    public async Task<bool> TestArchiveAsync(string archivePath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        if (!string.IsNullOrEmpty(password))
            CoreLog.Info($"WARN: TarGzEngine.TestArchiveAsync: password provided but TAR/GZ format does not support encryption (archive: {archivePath})");
        CoreLog.Info($"TestArchiveAsync: {archivePath}");

        try
        {
            await Task.Run(() =>
            {
                var ext = Path.GetExtension(archivePath).ToLowerInvariant();
                var isTarGz = ext == ".tgz" || archivePath.EndsWith(".tar.gz");

                if (isTarGz || ext == ".tar")
                {
                    // TAR 或 TAR.GZ — 使用 TarReader 逐条目解压到空流验证完整性
                    using var inputStream = File.OpenRead(archivePath);
                    long totalCompressedBytes = inputStream.Length;
                    using var reader = TarReader.OpenReader(inputStream, new ReaderOptions { LookForHeader = true });

                    while (reader.MoveToNextEntry())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (reader.Entry.IsDirectory) continue;

                        var entryKey = reader.Entry.Key ?? "";
                        long entrySize = reader.Entry.Size;

                        using var entryStream = reader.OpenEntryStream();
                        // 完全解压条目以验证数据完整性（带 per-file 进度）

                        // 文件开始：文件进度条归零
                        progress?.Report(new ArchiveProgress
                        {
                            CurrentFile = entryKey,
                            PercentComplete = totalCompressedBytes > 0
                                ? (double)inputStream.Position / totalCompressedBytes * 100
                                : 0,
                            FilePercentComplete = 0,
                        });

                        // 带 per-file 进度的复制循环（100ms 节流，末尾强制上报 100%）
                        long totalRead = 0;
                        var lastReportTime = DateTime.Now;
                        var reportInterval = TimeSpan.FromMilliseconds(100);
                        var buffer = new byte[CopyBufferSize];
                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var read = entryStream.Read(buffer, 0, buffer.Length);
                            if (read <= 0) break;
                            totalRead += read;

                            var now = DateTime.Now;
                            if (now - lastReportTime >= reportInterval || totalRead >= entrySize)
                            {
                                var filePct = entrySize > 0 ? (double)totalRead / entrySize * 100 : 100;
                                progress?.Report(new ArchiveProgress
                                {
                                    CurrentFile = entryKey,
                                    PercentComplete = totalCompressedBytes > 0
                                        ? (double)inputStream.Position / totalCompressedBytes * 100
                                        : 0,
                                    FilePercentComplete = filePct,
                                });
                                lastReportTime = now;
                            }
                        }

                        progress?.Report(new ArchiveProgress
                        {
                            CurrentFile = entryKey,
                            PercentComplete = totalCompressedBytes > 0
                                ? (double)inputStream.Position / totalCompressedBytes * 100
                                : 0,
                            FilePercentComplete = 100,
                        });
                    }
                }
                else if (ext == ".gz")
                {
                    // 单个 GZip 文件 — 解压到空流验证
                    using var inputStream = File.OpenRead(archivePath);
                    using var gzipStream = new System.IO.Compression.GZipStream(inputStream, System.IO.Compression.CompressionMode.Decompress);
                    gzipStream.CopyTo(Stream.Null);

                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = Path.GetFileNameWithoutExtension(archivePath),
                        PercentComplete = 100,
                        FilePercentComplete = 100,
                    });
                }

                CoreLog.Info("TestArchiveAsync: passed");
            }, cancellationToken).ConfigureAwait(false);

            CoreLog.Exit();
            return true;
        }
        catch (Exception ex)
        {
            CoreLog.Error($"TestArchiveAsync: failed", ex);
            CoreLog.Exit();
            return false;
        }
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
        // TAR/GZ 为顺序流式格式，通过单次扫描匹配目标条目实现按条目提取
        // （不需要重新打开压缩包，与 ArchiveEntryExtractor.ExtractTarGzEntry 同思路）
        CoreLog.Entry();
        if (!string.IsNullOrEmpty(password))
            CoreLog.Info($"WARN: TarGzEngine.ExtractEntriesAsync: password provided but TAR/GZ format does not support encryption (archive: {archivePath})");
        CoreLog.Info($"ExtractEntriesAsync: {archivePath}, {entryKeys.Count} entries -> {destinationPath}");
        var sw = Stopwatch.StartNew();

        var keySet = new HashSet<string>(
            entryKeys.Select(k => ArchivePath.Normalize(k)),
            StringComparer.OrdinalIgnoreCase);

        await Task.Run(async () =>
        {
            var ext = Path.GetExtension(archivePath).ToLowerInvariant();
            var isTarGz = ext == ".tgz" || archivePath.EndsWith(".tar.gz");
            CoreLog.Info($"ExtractEntriesAsync: format=tar.gz={isTarGz}, ext={ext}");
            int failedEntries = 0;
            var conflictStats = new ConflictStatsCounter();
            long bytesTotal = 0; // 链尾最终报告使用

            if (isTarGz || ext == ".tar")
            {
                // TAR 或 TAR.GZ — 单次顺序扫描，匹配 entryKeys 的条目按 override/默认路径输出
                using var inputStream = File.OpenRead(archivePath);
                var totalCompressedBytes = inputStream.Length;
                bytesTotal = totalCompressedBytes;
                using var reader = TarReader.OpenReader(inputStream, new ReaderOptions { LookForHeader = true });

                int processed = 0, targetFound = 0;
                var lastReportTime = DateTime.Now;
                var reportInterval = TimeSpan.FromMilliseconds(100);

                while (reader.MoveToNextEntry())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entry = reader.Entry;
                    var entryKey = entry.Key ?? string.Empty;
                    var normalizedKey = ArchivePath.Normalize(entryKey);

                    if (entry.IsDirectory)
                        continue; // 目录不参与（写入文件时自动创建中间目录）

                    if (!keySet.Contains(normalizedKey))
                        continue; // 跳过未选中的条目，MoveToNextEntry 自动推进流

                    targetFound++;

                    var outputPath = outputPathOverrides?.GetValueOrDefault(normalizedKey)
                        ?? FileConflictHelper.GetSafePath(destinationPath, entryKey);
                    var outDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                        Directory.CreateDirectory(outDir);

                    var entryModified = entry.LastModifiedTime ?? DateTime.MinValue;

                    var compressedProgress = totalCompressedBytes > 0
                        ? (double)inputStream.Position / totalCompressedBytes * 100
                        : 0;

                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        PercentComplete = Math.Min(compressedProgress, 99.9),
                        FilePercentComplete = 0,
                        TotalBytes = totalCompressedBytes,
                        ProcessedBytes = inputStream.Position
                    }));

                    var existedBefore = File.Exists(outputPath);
                    var resolved = await FileConflictHelper.ResolvePathAsync(outputPath, options, entryModified, entry.Size);
                    if (resolved == null)
                    {
                        conflictStats.RecordSkipped();
                        // 逐条目状态（D2）：冲突跳过 → Skipped
                        progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Skipped });
                        continue; // 跳过/覆盖旧/覆盖小
                    }
                    if (existedBefore && resolved == outputPath)
                    {
                        conflictStats.RecordOverwritten();
                        // 逐条目状态（D2）：覆盖旧文件 → Overwritten
                        progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Overwritten });
                    }

                    try
                    {
                        // 带 per-file 进度的复制
                        using (var entryStream = reader.OpenEntryStream())
                        using (var outStream = File.Create(resolved))
                        {
                            var buffer = new byte[CopyBufferSize];
                            long totalRead = 0;
                            long entrySize = entry.Size;

                            while (true)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var read = entryStream.Read(buffer, 0, buffer.Length);
                                if (read <= 0) break;
                                outStream.Write(buffer, 0, read);
                                totalRead += read;

                                var now = DateTime.Now;
                                if (now - lastReportTime >= reportInterval || totalRead >= entrySize)
                                {
                                    var filePct = entrySize > 0 ? (double)totalRead / entrySize * 100 : 100;
                                    compressedProgress = totalCompressedBytes > 0
                                        ? (double)inputStream.Position / totalCompressedBytes * 100
                                        : 0;
                                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                                    {
                                        CurrentFile = entryKey,
                                        PercentComplete = Math.Min(compressedProgress, 99.9),
                                        FilePercentComplete = filePct,
                                        TotalBytes = totalCompressedBytes,
                                        ProcessedBytes = inputStream.Position
                                    }));
                                    lastReportTime = now;
                                }
                            }
                        }
                        // 恢复文件原始修改时间
                        try { File.SetLastWriteTime(resolved, entryModified); }
                        catch (Exception tsEx) { CoreLog.Info($"ExtractEntriesAsync: failed to set timestamp on {resolved}: {tsEx.Message}"); }

                        processed++;
                        // 逐条目状态（D2）：写盘成功 → Completed（Overwritten 已在覆盖判定处上报，不重复覆盖）
                        if (!(existedBefore && resolved == outputPath))
                            progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Completed });

                        var now2 = DateTime.Now;
                        if (now2 - lastReportTime >= reportInterval || processed == targetFound)
                        {
                            progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                            {
                                CurrentFile = entryKey,
                                PercentComplete = Math.Min(compressedProgress, 99.9),
                                FilePercentComplete = 100,
                                TotalFiles = targetFound,
                                ProcessedFiles = processed,
                                TotalBytes = totalCompressedBytes,
                                ProcessedBytes = inputStream.Position
                            }));
                            lastReportTime = now2;
                        }
                    }
                    catch (UnauthorizedAccessException uax)
                    {
                        CoreLog.Info($"ExtractEntriesAsync: permission denied for '{entryKey}': {uax.Message}");
                        conflictStats.RecordFailed();
                        // 逐条目状态（D2）：权限失败 → Failed
                        progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed });
                        failedEntries++;
                    }
                    catch (IOException iox)
                    {
                        // 目标文件被其他进程占用等 IO 失败：跳过该条目继续，避免单个文件中止整个解压
                        CoreLog.Info($"ExtractEntriesAsync: write failed for '{entryKey}': {iox.Message}");
                        conflictStats.RecordFailed();
                        // 逐条目状态（D2）：IO 写入失败 → Failed
                        progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed });
                        failedEntries++;
                    }
                }

                progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                {
                    CurrentFile = string.Empty,
                    PercentComplete = 100,
                    TotalFiles = targetFound,
                    ProcessedFiles = processed,
                    TotalBytes = bytesTotal,
                    ProcessedBytes = bytesTotal
                }));
            }
            else if (ext == ".gz")
            {
                // 单纯 GZip 单文件 — 整个流解压到目标（条目名 = 去掉 .gz 的文件名）
                var entryName = Path.GetFileNameWithoutExtension(archivePath);
                var outputPath = outputPathOverrides?.GetValueOrDefault(ArchivePath.Normalize(entryName))
                    ?? FileConflictHelper.GetSafePath(destinationPath, entryName);
                var outDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                    Directory.CreateDirectory(outDir);

                var existedBefore = File.Exists(outputPath);
                var resolved = await FileConflictHelper.ResolvePathAsync(outputPath, options);
                if (resolved == null)
                {
                    conflictStats.RecordSkipped();
                    // 逐条目状态（D2）：冲突跳过 → Skipped
                    progress?.Report(new ArchiveProgress { EntryKey = entryName, EntryStatus = ArchiveEntryStatus.Skipped });
                }
                else
                {
                    if (existedBefore && resolved == outputPath)
                    {
                        conflictStats.RecordOverwritten();
                        // 逐条目状态（D2）：覆盖旧文件 → Overwritten
                        progress?.Report(new ArchiveProgress { EntryKey = entryName, EntryStatus = ArchiveEntryStatus.Overwritten });
                    }
                    try
                    {
                        using var inputStream = File.OpenRead(archivePath);
                        using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
                        using var output = File.Create(resolved);
                        // 手动缓冲循环：以压缩流位置为字节进度
                        long gzLen = inputStream.Length;
                        bytesTotal = gzLen;
                        long gzLastReport = Environment.TickCount64;
                        progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                        {
                            CurrentFile = entryName,
                            PercentComplete = 0,
                            FilePercentComplete = 0,
                            TotalBytes = gzLen,
                            ProcessedBytes = 0,
                        }));
                        var gzBuffer = new byte[CopyBufferSize];
                        int gzRead;
                        while ((gzRead = gzipStream.Read(gzBuffer, 0, gzBuffer.Length)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            output.Write(gzBuffer, 0, gzRead);
                            if (Environment.TickCount64 - gzLastReport >= 100)
                            {
                                gzLastReport = Environment.TickCount64;
                                double gzPct = gzLen > 0 ? Math.Min(inputStream.Position * 100.0 / gzLen, 99.9) : 0;
                                progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                                {
                                    CurrentFile = entryName,
                                    PercentComplete = gzPct,
                                    TotalBytes = gzLen,
                                    ProcessedBytes = inputStream.Position,
                                }));
                            }
                        }
                        // 逐条目状态（D2）：写盘成功 → Completed（Overwritten 已在覆盖判定处上报，不重复覆盖）
                        if (!(existedBefore && resolved == outputPath))
                            progress?.Report(new ArchiveProgress { EntryKey = entryName, EntryStatus = ArchiveEntryStatus.Completed });
                    }
                    catch (UnauthorizedAccessException uax)
                    {
                        CoreLog.Info($"ExtractEntriesAsync: permission denied for '{outputPath}': {uax.Message}");
                        conflictStats.RecordFailed();
                        // 逐条目状态（D2）：权限失败 → Failed
                        progress?.Report(new ArchiveProgress { EntryKey = entryName, EntryStatus = ArchiveEntryStatus.Failed });
                        failedEntries++;
                    }
                    catch (IOException iox)
                    {
                        CoreLog.Info($"ExtractEntriesAsync: write failed for '{outputPath}': {iox.Message}");
                        conflictStats.RecordFailed();
                        // 逐条目状态（D2）：IO 写入失败 → Failed
                        progress?.Report(new ArchiveProgress { EntryKey = entryName, EntryStatus = ArchiveEntryStatus.Failed });
                        failedEntries++;
                    }
                }

                progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                {
                    CurrentFile = entryName,
                    PercentComplete = 100,
                    TotalBytes = bytesTotal,
                    ProcessedBytes = bytesTotal
                }));
            }

            CoreLog.Info($"ExtractEntriesAsync: done, {sw.ElapsedMilliseconds}ms, failedEntries={failedEntries}");
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }
}
