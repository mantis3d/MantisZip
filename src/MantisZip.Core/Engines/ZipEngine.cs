using System.Diagnostics;
using System.IO;
using System.Linq;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;

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
        var zf = new ZipFile(archivePath, codec);
        if (!string.IsNullOrEmpty(password))
            zf.Password = password;

        if (zf.Cast<ZipEntry>().Any(e => !e.IsUnicodeText))
        {
            CoreLog.Info("OpenZipFile: detected non-UTF-8 entries, switching to system default encoding");
            zf.Close();
            ((IDisposable?)zf)?.Dispose();
            // StringCodec.Default 使用系统 ANSI 编码（中文=GBK，日文=Shift-JIS，等）
            // 不硬编码 936，尊重当前系统区域设置
            zf = new ZipFile(archivePath, StringCodec.Default);
            if (!string.IsNullOrEmpty(password))
                zf.Password = password;
        }
        return zf;
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
            ZipFile? zipFile = null;
            try
            {
                zipFile = OpenZipFile(archivePath, password);

                // 检查是否有加密条目但未提供密码
                // 同时检测 ZIP 传统加密 (IsCrypted) 和 AES 加密 (AESKeySize > 0)
                var hasEncrypted = zipFile.Cast<ZipEntry>().Any(e => e.IsCrypted || e.AESKeySize > 0);
                if (hasEncrypted && string.IsNullOrEmpty(password))
                {
                    CoreLog.Info("ExtractAsync: archive has encrypted entries but no password provided");
                    throw new InvalidOperationException("此压缩包已加密，请输入密码 (This archive is encrypted, password required)");
                }

                var allEntries = zipFile.Cast<ZipEntry>().ToList();
                var entries = allEntries.Where(e => !e.IsDirectory).ToList();
                var totalBytes = entries.Sum(e => e.Size);
                var processedBytes = 0L;
                var processedFiles = 0;

                CoreLog.Info($"ExtractAsync: {entries.Count} entries, {totalBytes} total bytes");

                foreach (ZipEntry entry in allEntries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (entry.IsDirectory)
                    {
                        // 主动创建目录条目，否则带 "." 的目录名或空目录会丢失
                        var dirPath = FileConflictHelper.GetSafePath(destinationPath, entry.Name);
                        if (!Directory.Exists(dirPath))
                            Directory.CreateDirectory(dirPath);
                        continue;
                    }

                    var outputPath = FileConflictHelper.GetSafePath(destinationPath, entry.Name);
                    var outputDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    // 冲突处理
                    var resolvedPath = FileConflictHelper.ResolvePath(outputPath, options, entry.DateTime, entry.Size);
                    if (resolvedPath == null)
                    {
                        // Skip: 不提取此文件
                        processedBytes += entry.Size;
                        continue;
                    }

                    var entrySize = entry.Size;
                    var entryModified = entry.DateTime;
                    using (var inputStream = zipFile.GetInputStream(entry))
                    using (var outputStream = File.Create(resolvedPath))
                    {
                        var buffer = new byte[81920];
                        var entryProcessed = 0L;
                        var lastReportTime = DateTime.Now;
                        var reportInterval = TimeSpan.FromMilliseconds(100);

                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var read = inputStream.Read(buffer, 0, buffer.Length);
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
                                    CurrentFile = entry.Name,
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
                    // 恢复文件原始修改时间（流已关闭，才能设置）
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
            }
            finally
            {
                zipFile?.Close();
                ((IDisposable?)zipFile)?.Dispose();
            }
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

            try
            {
                var outputStream = options.SplitSize > 0
                    ? (Stream)new SplitOutputStream(outputPath, options.SplitSize)
                    : File.Create(outputPath);
                using var fsOut = outputStream;
                using var zipStream = new ZipOutputStream(fsOut);
                zipStream.SetLevel(options.CompressionLevel);

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
                            ref processedBytes, totalBytes, cancellationToken, progress, ref lastReportTime))
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        // skip 此文件，继续下一个
                        continue;
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
            // 使用 OpenZipFile 自动检测编码（UTF-8 → 系统默认编码 fallback）
            using var zipFile = OpenZipFile(archivePath, password);

            var items = zipFile.Cast<ZipEntry>().Select(entry => new ArchiveItem
            {
                Name = entry.Name,
                FullPath = entry.IsDirectory ? entry.Name.TrimEnd('/') : entry.Name,
                Size = entry.Size,
                CompressedSize = entry.CompressedSize,
                LastModified = entry.DateTime,
                IsDirectory = entry.IsDirectory,
                IsEncrypted = entry.IsCrypted || entry.AESKeySize > 0
            }).ToList();

            CoreLog.Info($"ListEntriesAsync: {items.Count} entries, {sw.ElapsedMilliseconds}ms");
            return items;
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
                using var zipFile = OpenZipFile(archivePath, password);

                foreach (ZipEntry entry in zipFile)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entry.IsDirectory) continue;
                    using var stream = zipFile.GetInputStream(entry);
                    // 尝试读取第一个字节来验证数据完整性
                    stream.ReadByte();
                }

                CoreLog.Info("TestArchiveAsync: passed");
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
            // 收集需要添加的文件
            var files = new List<(string FullPath, string EntryName)>();
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
                        files.Add((file, entryName));
                    }
                }
                else if (File.Exists(sourcePath))
                {
                    var entryName = string.IsNullOrEmpty(entryBasePath) ? Path.GetFileName(sourcePath) : entryBasePath + "/" + Path.GetFileName(sourcePath);
                    files.Add((sourcePath, entryName));
                }
            }

            if (files.Count == 0)
            {
                CoreLog.Info("AddToArchiveAsync: no files to add");
                return;
            }

            // 使用 SharpZipLib 的原地更新功能
            // StringCodec.Default 处理所有编码情况：
            // - 有 UTF-8 标记的条目 → UTF-8 解码
            // - 无 UTF-8 标记的条目 → 系统 ANSI 解码（中文=GBK，日文=Shift-JIS 等）
            // - 新增的条目 → UTF-8 写入（自动设置 UTF-8 标记）
            // 不做硬编码编码假设，尊重系统区域设置
            using var zipFile = new ZipFile(archivePath, StringCodec.Default);

            // 旧条目数（CommitUpdate 时需要 I/O 复制的量）
            var oldEntryCount = zipFile.Count;
            var totalNewFiles = files.Count;
            var totalWorkUnits = totalNewFiles + oldEntryCount; // 新文件压缩 + 旧条目 I/O

            zipFile.BeginUpdate();

            CoreLog.Trace($"[TRACE] ZipEngine.AddToArchiveAsync: totalNewFiles={totalNewFiles}, oldEntries={oldEntryCount}, totalWorkUnits={totalWorkUnits}");

            // 报告 0% 确保进度窗口立即显示刷新
            progress?.Report(new ArchiveProgress
            {
                CurrentFile = totalNewFiles > 0 ? Path.GetFileName(files[0].EntryName) : "",
                PercentComplete = 0,
                FilePercentComplete = 0
            });
            CoreLog.Trace("[TRACE] ZipEngine.AddToArchiveAsync: reported 0%");
            for (int i = 0; i < totalNewFiles; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (fullPath, entryName) = files[i];
                zipFile.Add(fullPath, entryName);

                // PercentComplete 按总工作量加权（新文件压缩 + 旧条目 I/O 复制）
                var overallPct = (double)(i + 1) / totalWorkUnits * 100;
                // FilePercentComplete 仍按新文件进度
                var filePct = (double)(i + 1) / totalNewFiles * 100;
                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = entryName,
                    PercentComplete = overallPct,
                    FilePercentComplete = filePct
                });
                CoreLog.Trace($"[TRACE] ZipEngine.AddToArchiveAsync: reported overall={overallPct:F1}%, file={filePct:F0}% for '{entryName}'");
            }

            cancellationToken.ThrowIfCancellationRequested();
            zipFile.CommitUpdate();

            // CommitUpdate 完成后上报 100%
            progress?.Report(new ArchiveProgress
            {
                CurrentFile = "",
                PercentComplete = 100,
                FilePercentComplete = 100
            });

            CoreLog.Info($"AddToArchiveAsync: done, {totalNewFiles} files added ({oldEntryCount} old entries kept), {sw.ElapsedMilliseconds}ms");
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
            using var zipFile = OpenZipFile(archivePath, password);

            // 验证所有条目是否存在
            var entryNames = new HashSet<string>(zipFile.Cast<ZipEntry>().Select(e => e.Name));
            foreach (var entryPath in entryPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entryNames.Contains(entryPath))
                {
                    CoreLog.Error($"DeleteEntriesAsync: entry not found: {entryPath}");
                    throw new FileNotFoundException($"压缩包中不存在条目: {entryPath}", entryPath);
                }
            }

            if (entryPaths.Length == 0)
            {
                CoreLog.Info("DeleteEntriesAsync: no entries to delete");
                return;
            }

            // 总条目数（作为总工作量：删除标记 + CommitUpdate 复制剩余条目）
            var totalOldEntries = zipFile.Count;

            // 报告 0% 确保进度窗口立即显示刷新
            progress?.Report(new ArchiveProgress
            {
                CurrentFile = entryPaths[0],
                PercentComplete = 0,
                FilePercentComplete = 0
            });

            zipFile.BeginUpdate();
            for (int i = 0; i < entryPaths.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                zipFile.Delete(entryPaths[i]);

                // PercentComplete 按总条目加权（删除标记 + 剩余条目的 CommitUpdate I/O）
                var overallPct = (double)(i + 1) / totalOldEntries * 100;
                var filePct = (double)(i + 1) / entryPaths.Length * 100;
                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = entryPaths[i],
                    PercentComplete = overallPct,
                    FilePercentComplete = filePct
                });
            }
            cancellationToken.ThrowIfCancellationRequested();
            zipFile.CommitUpdate();

            // CommitUpdate 完成后上报 100%
            progress?.Report(new ArchiveProgress
            {
                CurrentFile = "",
                PercentComplete = 100,
                FilePercentComplete = 100
            });

            var entriesKept = totalOldEntries - entryPaths.Length;
            CoreLog.Info($"DeleteEntriesAsync: done, {entryPaths.Length} entries deleted ({entriesKept} kept), {sw.ElapsedMilliseconds}ms");
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }

    /// <summary>
    /// 带重试/跳过/中止的文件压缩读取。返回 false 表示跳过此文件。
    /// </summary>
    private bool ReadFileWithRetry(string fullPath, string relativePath,
        ArchiveOptions options, ZipOutputStream zipStream, ref long processedBytes, long totalBytes,
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
                                FilePercentComplete = filePct
                            });
                            lastReportTime = now;
                        }
                    }
                }
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
