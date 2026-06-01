using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
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
/// ZIP 压缩引擎（基于 SharpZipLib）
/// </summary>
public class ZipEngine : IArchiveEngine
{
    /// <summary>
    /// 以自动检测编码的方式打开 ZIP 文件。
    /// 先尝试 UTF-8 解码，若检测到不含 UTF-8 标记的非 ASCII 条目，
    /// 则改用 StringCodec.Default（即当前系统的 ANSI 编码）重新打开。
    /// 中文系统上 Default = GBK(936)，日文系统上 = Shift-JIS(932)，
    /// 不再硬编码假设一种语言。
    /// 使用 per-instance StringCodec，不修改全局 ZipStrings.CodePage 状态。
    /// </summary>
    public static ZipFile OpenZipFile(string archivePath, string? password = null)
    {
        var codec = StringCodec.FromCodePage(65001);
        var first = new ZipFile(archivePath, codec);
        if (!string.IsNullOrEmpty(password))
            first.Password = password;

        try
        {
            var hasNonUtf8 = first.Cast<ZipEntry>().Any(e => !e.IsUnicodeText);
            CoreLog.Info($"OpenZipFile: archive='{archivePath}', hasNonUtf8Entries={hasNonUtf8}");
            if (hasNonUtf8)
            {
                CoreLog.Info("OpenZipFile: switching to StringCodec.Default (system ANSI encoding)");
                first.Close();
                ((IDisposable?)first)?.Dispose();
                // StringCodec.Default 使用系统 ANSI 编码（中文=GBK，日文=Shift-JIS，等）
                // 不硬编码 936，尊重当前系统区域设置
                var result = new ZipFile(archivePath, StringCodec.Default);
                if (!string.IsNullOrEmpty(password))
                    result.Password = password;
                return result;
            }

            CoreLog.Info("OpenZipFile: all entries use UTF-8, keeping UTF-8 codec");
            return first;
        }
        catch (Exception ex)
        {
            // 枚举或构造期间异常：释放已打开的 ZipFile，防止文件句柄泄漏
            CoreLog.Info("OpenZipFile: failed during encoding detection: {0}", ex.Message);
            first.Close();
            ((IDisposable?)first)?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 使用 SharpCompress 打开 ZIP 文件，自动检测编码（UTF-8 → GBK 回退）。
    /// SharpCompress 每实例设置编码，无全局副作用。
    /// </summary>
    private static IArchive OpenArchiveWithEncodingFallback(string archivePath, string? password = null)
    {
        // 先以 UTF-8 尝试打开
        var options = new ReaderOptions { Password = password ?? string.Empty };
        var archive = ArchiveFactory.OpenArchive(archivePath, options);

        try
        {
            // 检查条目名是否有高位 ASCII 字符（可能是非 UTF-8 编码的遗留 ZIP）
            var hasHighAscii = archive.Entries.Any(e =>
                !string.IsNullOrEmpty(e.Key) && e.Key.Any(c => c > 127));

            if (hasHighAscii)
            {
                CoreLog.Info("OpenArchiveWithEncodingFallback: detected high-ASCII entry names, retrying with GBK");
                archive.Dispose();
                var gbkOptions = new ReaderOptions
                {
                    Password = password ?? string.Empty,
                    ArchiveEncoding = new SharpCompress.Common.ArchiveEncoding
                    {
                        Default = Encoding.GetEncoding("gbk")
                    }
                };
                return ArchiveFactory.OpenArchive(archivePath, gbkOptions);
            }

            CoreLog.Info("OpenArchiveWithEncodingFallback: entries appear UTF-8, keeping UTF-8 codec");
            return archive;
        }
        catch
        {
            // 如果枚举 entries 失败（如格式错误），释放并尝试 GBK
            archive.Dispose();
            var gbkOptions = new ReaderOptions
            {
                Password = password ?? string.Empty,
                ArchiveEncoding = new SharpCompress.Common.ArchiveEncoding
                {
                    Default = Encoding.GetEncoding("gbk")
                }
            };
            return ArchiveFactory.OpenArchive(archivePath, gbkOptions);
        }
    }

    public bool CanHandle(ArchiveFormat format) => format == ArchiveFormat.Zip;

    public bool CanAdd(ArchiveFormat format) => format == ArchiveFormat.Zip;

    public bool CanDelete(ArchiveFormat format) => format == ArchiveFormat.Zip;

    public async Task ExtractAsync(string archivePath, string destinationPath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default, ArchiveOptions? options = null)
    {
        CoreLog.Entry();
        CoreLog.Info($"ExtractAsync: {archivePath} -> {destinationPath}, password={(password != null ? "***" : "null")}");
        var sw = Stopwatch.StartNew();

        await Task.Run(() =>
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
                var resolvedPath = FileConflictHelper.ResolvePath(outputPath, options, entryModified, entry.Size);
                if (resolvedPath == null)
                {
                    processedBytes += entry.Size;
                    continue;
                }

                var entrySize = entry.Size;
                using (var entryStream = entry.OpenEntryStream())
                using (var outputStream = File.Create(resolvedPath))
                {
                    var buffer = new byte[81920];
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

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100
            });

            CoreLog.Info($"ExtractAsync: done, {processedFiles} files, {processedBytes} bytes, {sw.ElapsedMilliseconds}ms");
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }

    public async Task ExtractEntriesAsync(
        string archivePath,
        IReadOnlyList<string> entryKeys,
        string destinationPath,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default,
        ArchiveOptions? options = null)
    {
        CoreLog.Entry();
        CoreLog.Info($"ExtractEntriesAsync: {archivePath}, {entryKeys.Count} entries -> {destinationPath}");
        var sw = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            using var archive = OpenArchiveWithEncodingFallback(archivePath, password);
            var entries = archive.Entries.ToList();
            var totalBytes = entries.Where(e => entryKeys.Contains(e.Key)).Sum(e => e.Size);
            var processedBytes = 0L;
            var processedFiles = 0;
            var filteredEntries = entries.Where(e => entryKeys.Contains(e.Key)).ToList();

            CoreLog.Info($"ExtractEntriesAsync: {filteredEntries.Count} matching entries");

            foreach (var entry in filteredEntries)
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
                    Directory.CreateDirectory(outputDir);

                var entryModified = entry.LastModifiedTime ?? DateTime.MinValue;
                var resolvedPath = FileConflictHelper.ResolvePath(outputPath, options, entryModified, entry.Size);
                if (resolvedPath == null)
                {
                    processedBytes += entry.Size;
                    continue;
                }

                var entrySize = entry.Size;
                using (var entryStream = entry.OpenEntryStream())
                using (var outputStream = File.Create(resolvedPath))
                {
                    var buffer = new byte[81920];
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
            var (files, totalBytes) = FileScanner.CollectFiles(sourcePaths, progress, cancellationToken);

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
                using var zipStream = new ZipOutputStream(fsOut);
                zipStream.SetLevel(options.CompressionLevel);

                if (!string.IsNullOrEmpty(options.Comment))
                    zipStream.SetComment(options.Comment);

                if (options.Encrypt && !string.IsNullOrEmpty(options.Password))
                {
                    zipStream.Password = options.Password;
                }

                var lastReportTime = DateTime.Now;
                var reportInterval = TimeSpan.FromMilliseconds(100);

                foreach (var (fullPath, relativePath) in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 带重试/跳过/中止的文件读取
                    if (!ReadFileWithRetry(fullPath, relativePath, options, zipStream,
                            ref processedBytes, totalBytes, totalFiles, ref processedFiles,
                            cancellationToken, progress, ref lastReportTime))
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        // skip 此文件，继续下一个
                        continue;
                    }

                    // 文件压缩完成后上报文件计数（确保 0 字节文件也能刷新计数）
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
                var entryKey = entry.Key ?? string.Empty;
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

                // 预先收集非目录条目，确保总条目数已知
                var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
                int totalEntries = entries.Count;
                int processed = 0;
                var lastReportTime = DateTime.Now;
                var reportInterval = TimeSpan.FromMilliseconds(500);

                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 先报一次 0% 让进度条立即开始动
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = entry.Key ?? $"entry_{processed}",
                        PercentComplete = totalEntries > 0 ? (double)processed / totalEntries * 100 : 100,
                        FilePercentComplete = 0,
                    });

                    var entryKey = entry.Key ?? $"entry_{processed}";
                    var entrySize = entry.Size;

                    // 读全量数据触发 SharpCompress 的 CRC32 校验（Dispose 时比对）
                    using var stream = entry.OpenEntryStream();
                    var buffer = new byte[81920];
                    long totalRead = 0;

                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead <= 0) break;
                        totalRead += bytesRead;

                        // 大文件每 500ms 报一次进度
                        var now = DateTime.Now;
                        if (now - lastReportTime >= reportInterval || totalRead >= entrySize)
                        {
                            var filePct = entrySize > 0
                                ? Math.Min((double)totalRead / entrySize * 100, 100)
                                : 100;
                            var overallPct = totalEntries > 0
                                ? ((double)processed + (double)totalRead / Math.Max(entrySize, 1)) / totalEntries * 100
                                : 100;
                            progress?.Report(new ArchiveProgress
                            {
                                CurrentFile = entryKey,
                                PercentComplete = Math.Min(overallPct, 100),
                                FilePercentComplete = filePct,
                            });
                            lastReportTime = now;
                        }
                    }

                    processed++;
                }

                // 报告 100%
                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = string.Empty,
                    PercentComplete = 100,
                    FilePercentComplete = 100,
                });

                CoreLog.Info($"TestArchiveAsync: passed, {totalEntries} entries verified");
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

    public async Task AddToArchiveAsync(string archivePath, string[] sourcePaths, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default, string? entryBasePath = null)
    {
        CoreLog.Entry();
        CoreLog.Info($"AddToArchiveAsync: {archivePath}, sources=[{string.Join("; ", sourcePaths)}]");
        var sw = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            // 收集需要添加的新文件
            var newFiles = new List<(string FullPath, string EntryName)>();
            foreach (var sourcePath in sourcePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Directory.Exists(sourcePath))
                {
                    var dirName = Path.GetFileName(sourcePath.TrimEnd('\\', '/'));
                    foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
                    {
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

            // 计算旧条目信息（使用 SharpZipLib ZipFile 读取，确保文件句柄及时释放）
            int oldEntryCount = 0;
            long oldTotalBytes = 0;
            var oldEntriesWithTime = new List<(string Name, long Size, DateTime DateTime)>();
            using (var zipFile = OpenZipFile(archivePath))
            {
                foreach (ZipEntry entry in zipFile)
                {
                    if (entry.IsDirectory) continue;
                    oldTotalBytes += entry.Size;
                    oldEntriesWithTime.Add((entry.Name, entry.Size, entry.DateTime));
                }
                oldEntryCount = oldEntriesWithTime.Count;
            }

            long newTotalBytes = newFiles.Sum(f => new FileInfo(f.FullPath).Length);
            // 总工作量 = 提取旧条目字节 + 压缩全部字节
            long workTotal = oldTotalBytes + oldTotalBytes + newTotalBytes;
            if (workTotal == 0) workTotal = 1;

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

                using (var zipFile = OpenZipFile(archivePath))
                {
                    foreach (ZipEntry entry in zipFile)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entryName = entry.Name;

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
                        using (var entryStream = zipFile.GetInputStream(entry))
                        using (var outStream = File.Create(outPath))
                        {
                            var buffer = new byte[81920];
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
                        try { File.SetLastWriteTime(outPath, entry.DateTime); } catch { CoreLog.Trace("ZipEngine: failed to set last write time for '{0}'", outPath); }
                    }
                }

                CoreLog.Trace($"[TRACE] ZipEngine.AddToArchiveAsync: Phase 1 done, extracted {processedBytes} bytes");

                // === Phase 2: 复制新文件到临时目录 ===
                foreach (var (fullPath, entryName) in newFiles)
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

                // === Phase 3: ZipOutputStream 重压缩（字节加权平滑进度） ===
                CoreLog.Trace($"[TRACE] ZipEngine.AddToArchiveAsync: Phase 3 — recompressing {compressFiles.Count} files, {compressTotalBytes} bytes");
                using (var fsOut = File.Create(tempArchive))
                using (var zipStream = new ZipOutputStream(fsOut))
                {
                    zipStream.SetLevel(options.CompressionLevel);
                    if (!string.IsNullOrEmpty(options.Comment))
                        zipStream.SetComment(options.Comment);
                    if (options.Encrypt && !string.IsNullOrEmpty(options.Password))
                        zipStream.Password = options.Password;

                    foreach (var (fullPath, relPath) in compressFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var fi = new FileInfo(fullPath);
                        var entry = new ZipEntry(ZipEntry.CleanName(relPath))
                        {
                            DateTime = fi.LastWriteTime,
                            AESKeySize = options.Encrypt ? 256 : 0
                        };
                        zipStream.PutNextEntry(entry);

                        var buffer = new byte[81920];
                        long totalRead = 0;
                        var fiLen = fi.Length;
                        using (var fsInput = File.OpenRead(fullPath))
                        {
                            while (totalRead < fiLen)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var read = fsInput.Read(buffer, 0, buffer.Length);
                                if (read <= 0) break;
                                zipStream.Write(buffer, 0, read);
                                totalRead += read;
                                compressProcessed += read;

                                var now = DateTime.Now;
                                if (now - lastReportTime >= reportInterval || totalRead >= fiLen)
                                {
                                    // 累计进度 = 已提取的旧字节 + 正在压缩的字节量
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

                        // 恢复文件修改时间到 ZIP 条目
                        // ZipEntry.DateTime 已在 PutNextEntry 前设置
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
            var deletedSet = new HashSet<string>(entryPaths.Select(p => p.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
            if (entryPaths.Length == 0)
            {
                CoreLog.Info("DeleteEntriesAsync: no entries to delete");
                return;
            }

            // 创建临时目录和工作文件
            var tempDir = Path.Combine(Path.GetTempPath(), "MantisZip", "DeleteTemp", Guid.NewGuid().ToString());
            var tempArchive = tempDir + ".new.zip";

            // 使用 SharpZipLib ZipFile 完成验证 + 确定保留项 + 提取
            long totalKeepBytes = 0;
            int keepEntryCount = 0;
            long workTotal = 1;
            long processedBytes = 0;
            var lastReportTime = DateTime.Now;
            var reportInterval = TimeSpan.FromMilliseconds(100);

            try
            {
                Directory.CreateDirectory(tempDir);

                // Pass 1: 验证 + 确定保留项 + 计算总字节数
                var keepNames = new List<string>();
                using (var zipFile = OpenZipFile(archivePath, password))
                {
                    var allNames = new List<string>();
                    foreach (ZipEntry entry in zipFile)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var name = entry.Name;
                        allNames.Add(name);
                    }

                    // 验证要删除的条目都存在
                    var entryNameSet = new HashSet<string>(allNames);
                    foreach (var entryPath in entryPaths)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var normalized = entryPath.Replace('\\', '/');
                        if (!entryNameSet.Contains(normalized))
                        {
                            CoreLog.Error($"DeleteEntriesAsync: entry not found: {entryPath}");
                            throw new FileNotFoundException($"压缩包中不存在条目: {entryPath}", entryPath);
                        }
                    }

                    foreach (var name in allNames)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var normalized = name.Replace('\\', '/');
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

                // Pass 2: 提取保留条目到临时目录（带进度）
                using (var zipFile = OpenZipFile(archivePath, password))
                {
                    // 先算 totalKeepBytes
                    foreach (ZipEntry entry in zipFile)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (entry.IsDirectory) continue;
                        if (keepNames.Contains(entry.Name))
                            totalKeepBytes += entry.Size;
                    }
                }

                workTotal = totalKeepBytes + totalKeepBytes;
                if (workTotal == 0) workTotal = 1;

                // Pass 3: 实际提取
                using (var zipFile = OpenZipFile(archivePath, password))
                {
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = "正在提取保留条目...",
                        PercentComplete = 0
                    });

                    foreach (ZipEntry entry in zipFile)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entryName = entry.Name;

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
                        using (var entryStream = zipFile.GetInputStream(entry))
                        using (var outStream = File.Create(outPath))
                        {
                            var buffer = new byte[81920];
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
                        try { File.SetLastWriteTime(outPath, entry.DateTime); } catch { CoreLog.Trace("ZipEngine: failed to set last write time for '{0}'", outPath); }
                    }
                }

                // === Phase 2: 扫描临时目录并重压缩 ===
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

                using (var fsOut = File.Create(tempArchive))
                using (var zipStream = new ZipOutputStream(fsOut))
                {
                    // 保持默认压缩级别
                    zipStream.SetLevel(6);

                    foreach (var (fullPath, relPath) in compressFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var fi = new FileInfo(fullPath);
                        var entry = new ZipEntry(ZipEntry.CleanName(relPath))
                        {
                            DateTime = fi.LastWriteTime
                        };
                        zipStream.PutNextEntry(entry);

                        var buffer = new byte[81920];
                        long totalRead = 0;
                        var fiLen = fi.Length;
                        using (var fsInput = File.OpenRead(fullPath))
                        {
                            while (totalRead < fiLen)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var read = fsInput.Read(buffer, 0, buffer.Length);
                                if (read <= 0) break;
                                zipStream.Write(buffer, 0, read);
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

    /// <summary>
    /// 带重试/跳过/中止的文件压缩读取。返回 false 表示跳过此文件。
    /// </summary>
    private bool ReadFileWithRetry(string fullPath, string relativePath,
        ArchiveOptions options, ZipOutputStream zipStream, ref long processedBytes, long totalBytes,
        int totalFiles, ref int processedFiles,
        CancellationToken ct, IProgress<ArchiveProgress>? progress, ref DateTime lastReportTime)
    {
        int retries = 3;
        while (retries > 0)
        {
            try
            {
                var fi = new FileInfo(fullPath);
                var entry = new ZipEntry(ZipEntry.CleanName(relativePath))
                {
                    DateTime = fi.LastWriteTime,
                    AESKeySize = options?.Encrypt == true ? 256 : 0
                };

                zipStream.PutNextEntry(entry);

                var buffer = new byte[81920];
                long totalRead = 0;
                var fiLen = fi.Length;

                using (var fsInput = File.OpenRead(fullPath))
                {
                    while (totalRead < fiLen)
                    {
                        ct.ThrowIfCancellationRequested();
                        var read = fsInput.Read(buffer, 0, buffer.Length);
                        if (read <= 0) break;
                        zipStream.Write(buffer, 0, read);
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
                return true; // success
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                retries--;
                if (options?.ErrorResolver == null)
                {
                    // 没有回调 → 直接重试
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
                    // 已减 retries，直接继续循环
                    continue;
                }
                if (action == FileErrorAction.Skip)
                {
                    return false; // 跳过此文件
                }
                // Abort
                throw;
            }
        }
        return false;
    }
}
