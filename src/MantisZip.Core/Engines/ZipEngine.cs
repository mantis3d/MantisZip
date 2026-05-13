using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;

namespace MantisZip.Core.Engines;

/// <summary>
/// ZIP 压缩引擎（基于 SharpZipLib）
/// </summary>
public class ZipEngine : IArchiveEngine
{
    public bool CanHandle(ArchiveFormat format) => format == ArchiveFormat.Zip;

    public async Task ExtractAsync(string archivePath, string destinationPath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"ExtractAsync: {archivePath} -> {destinationPath}, password={(password != null ? "***" : "null")}");
        var sw = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            // 支持 GBK 编码（解决中文文件名乱码问题）
#pragma warning disable CS0618
            ZipStrings.CodePage = 936;
#pragma warning restore CS0618
            using var zipFile = new ZipFile(archivePath);
            if (!string.IsNullOrEmpty(password))
            {
                zipFile.Password = password;
            }

            // 检查是否有加密条目但未提供密码
            var hasEncrypted = zipFile.Cast<ZipEntry>().Any(e => e.IsCrypted);
            if (hasEncrypted && string.IsNullOrEmpty(password))
            {
                CoreLog.Info("ExtractAsync: archive has encrypted entries but no password provided");
                throw new InvalidOperationException("此压缩包已加密，请输入密码 (This archive is encrypted, password required)");
            }

            var entries = zipFile.Cast<ZipEntry>().Where(e => !e.IsDirectory).ToList();
            var totalBytes = entries.Sum(e => e.Size);
            var processedBytes = 0L;
            var processedFiles = 0;

            CoreLog.Info($"ExtractAsync: {entries.Count} entries, {totalBytes} total bytes");

            foreach (ZipEntry entry in zipFile)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry.IsDirectory)
                {
                    // 主动创建目录条目，否则带 "." 的目录名或空目录会丢失
                    var dirPath = Path.Combine(destinationPath, entry.Name);
                    if (!Directory.Exists(dirPath))
                        Directory.CreateDirectory(dirPath);
                    continue;
                }

                var outputPath = Path.Combine(destinationPath, entry.Name);
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                var entrySize = entry.Size;
                using var inputStream = zipFile.GetInputStream(entry);
                using var outputStream = File.Create(outputPath);

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

                processedBytes += entrySize;
                processedFiles++;
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100
            });

            CoreLog.Info($"ExtractAsync: done, {processedFiles} files, {processedBytes} bytes, {sw.ElapsedMilliseconds}ms");
        }, cancellationToken);

        CoreLog.Exit();
    }

    public async Task CompressAsync(string[] sourcePaths, string outputPath, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"CompressAsync: [{string.Join("; ", sourcePaths)}] -> {outputPath}, level={options.CompressionLevel}, split={options.SplitSize}");
        var sw = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            // 收集所有文件（使用 EnumerateFiles 延迟枚举，边发现边报告进度）
            var files = new List<(string FullPath, string RelativePath)>();
            long totalBytes = 0;
            var lastScanReportTime = DateTime.Now;
            var scanReportInterval = TimeSpan.FromMilliseconds(100);

            foreach (var sourcePath in sourcePaths)
            {
                if (Directory.Exists(sourcePath))
                {
                    var dirName = Path.GetFileName(sourcePath);
                    foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var relativePath = Path.Combine(dirName, Path.GetRelativePath(sourcePath, file));
                        files.Add((file, relativePath));

                        // 在扫描阶段同时累计总大小
                        try { totalBytes += new FileInfo(file).Length; } catch { }

                        // 每 100ms 报告一次扫描进度，让用户看到正在枚举文件
                        var now = DateTime.Now;
                        if (now - lastScanReportTime >= scanReportInterval)
                        {
                            progress?.Report(new ArchiveProgress
                            {
                                CurrentFile = $"正在扫描: {relativePath} ({files.Count} 个文件)",
                                PercentComplete = 0,
                                FilePercentComplete = 0
                            });
                            lastScanReportTime = now;
                        }
                    }
                }
                else if (File.Exists(sourcePath))
                {
                    files.Add((sourcePath, Path.GetFileName(sourcePath)));
                    try { totalBytes += new FileInfo(sourcePath).Length; } catch { }
                }
            }

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

                    var fi = new FileInfo(fullPath);
                    var entry = new ZipEntry(ZipEntry.CleanName(relativePath))
                    {
                        DateTime = fi.LastWriteTime,
                        AESKeySize = options.Encrypt ? 256 : 0
                    };

                    zipStream.PutNextEntry(entry);

                    var buffer = new byte[81920];
                    long totalRead = 0;
                    var fiLen = fi.Length;

                    using var fsInput = File.OpenRead(fullPath);
                    while (totalRead < fiLen)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var read = fsInput.Read(buffer, 0, buffer.Length);
                        if (read <= 0) break;
                        zipStream.Write(buffer, 0, read);
                        totalRead += read;
                        processedBytes += read;

                        var now = DateTime.Now;
                        if (now - lastReportTime >= reportInterval || totalRead >= fiLen)
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
        }, cancellationToken);

        CoreLog.Exit();
    }

    public async Task<IReadOnlyList<ArchiveItem>> ListEntriesAsync(string archivePath, string? password = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"ListEntriesAsync: {archivePath}");
        var sw = Stopwatch.StartNew();

        var result = await Task.Run(() =>
        {
            // 读取 ZIP 文件条目
            // 支持 GBK 编码（解决中文文件名乱码问题）
#pragma warning disable CS0618
            ZipStrings.CodePage = 936; // GBK
#pragma warning restore CS0618
            using var zipFile = new ZipFile(archivePath);
            if (!string.IsNullOrEmpty(password))
            {
                zipFile.Password = password;
            }

            var items = zipFile.Cast<ZipEntry>().Select(entry => new ArchiveItem
            {
                Name = entry.Name,
                FullPath = entry.IsDirectory ? entry.Name.TrimEnd('/') : entry.Name,
                Size = entry.Size,
                CompressedSize = entry.CompressedSize,
                LastModified = entry.DateTime,
                IsDirectory = entry.IsDirectory,
                IsEncrypted = entry.IsCrypted
            }).ToList();

            CoreLog.Info($"ListEntriesAsync: {items.Count} entries, {sw.ElapsedMilliseconds}ms");
            return items;
        }, cancellationToken);

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
#pragma warning disable CS0618
                ZipStrings.CodePage = 936;
#pragma warning restore CS0618
                using var zipFile = new ZipFile(archivePath);
                if (!string.IsNullOrEmpty(password))
                {
                    zipFile.Password = password;
                }

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
        }, cancellationToken);

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
#pragma warning disable CS0618
            ZipStrings.CodePage = 936;
#pragma warning restore CS0618

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
            using var zipFile = new ZipFile(archivePath);
            zipFile.BeginUpdate();

            var totalFiles = files.Count;
            for (int i = 0; i < totalFiles; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (fullPath, entryName) = files[i];
                zipFile.Add(fullPath, entryName);

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = entryName,
                    PercentComplete = (double)(i + 1) / totalFiles * 100
                });
            }

            zipFile.CommitUpdate();
            CoreLog.Info($"AddToArchiveAsync: done, {totalFiles} files added, {sw.ElapsedMilliseconds}ms");
        }, cancellationToken);

        CoreLog.Exit();
    }
}
