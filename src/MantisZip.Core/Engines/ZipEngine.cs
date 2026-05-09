using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core.Abstractions;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace MantisZip.Core.Engines;

/// <summary>
/// ZIP 压缩引擎
/// </summary>
public class ZipEngine : IArchiveEngine
{
    // 注册 GBK 编码支持（.NET Core/5+ 需要）
    static ZipEngine()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public bool CanHandle(ArchiveFormat format) => format == ArchiveFormat.Zip;

    public async Task ExtractAsync(string archivePath, string destinationPath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            // 支持 GBK 编码（解决中文文件名乱码问题）
            ZipStrings.CodePage = 936;
            using var zipFile = new ZipFile(archivePath);
            if (!string.IsNullOrEmpty(password))
            {
                zipFile.Password = password;
            }

            var entries = zipFile.Cast<ZipEntry>().Where(e => !e.IsDirectory).ToList();
            var totalBytes = entries.Sum(e => e.Size);
            var processedBytes = 0L;
            var processedFiles = 0;

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

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = entry.Name,
                    TotalFiles = entries.Count,
                    ProcessedFiles = processedFiles,
                    TotalBytes = totalBytes,
                    ProcessedBytes = processedBytes,
                    PercentComplete = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : 0,
                    FilePercentComplete = 0
                });

                using var inputStream = zipFile.GetInputStream(entry);
                using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);

                // 逐块拷贝，上报当前文件进度
                var entrySize = entry.Size;
                var entryProcessed = 0L;
                var buffer = new byte[81920];
                var lastReportTime = DateTime.Now;
                var reportInterval = TimeSpan.FromMilliseconds(100);
                int read;
                while ((read = inputStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
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
                TotalFiles = entries.Count,
                ProcessedFiles = processedFiles,
                TotalBytes = totalBytes,
                ProcessedBytes = processedBytes,
                PercentComplete = 100,
                FilePercentComplete = 100
            });
        }, cancellationToken);
    }

    public async Task CompressAsync(string[] sourcePaths, string outputPath, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var files = new List<(string FullPath, string RelativePath)>();
        foreach (var sourcePath in sourcePaths)
        {
            if (Directory.Exists(sourcePath))
            {
                var dirName = Path.GetFileName(sourcePath);
                foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.Combine(dirName, Path.GetRelativePath(sourcePath, file));
                    files.Add((file, relativePath));
                }
            }
            else if (File.Exists(sourcePath))
            {
                files.Add((sourcePath, Path.GetFileName(sourcePath)));
            }
        }

        if (files.Count == 0) return;

        var totalBytes = files.Sum(f => new FileInfo(f.FullPath).Length);
        long processedBytes = 0;
        
        try
        {
            using var fsOut = File.Create(outputPath);
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
                    // 不设置 Size，让库自动计算（加密时会不同）
                    AESKeySize = options.Encrypt ? 256 : 0
                };

                zipStream.PutNextEntry(entry);

                var buffer = new byte[4096];
                using var fsInput = File.OpenRead(fullPath);
                var totalRead = 0;
                var fiLen = fi.Length;

                // 开始压缩当前文件
                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = "正在压缩: " + relativePath,
                    PercentComplete = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : 0,
                    FilePercentComplete = 0
                });

                while (totalRead < fiLen)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var read = await fsInput.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (read <= 0) break;
                    
                    await zipStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    totalRead += read;
                    processedBytes += read;

                    var now = DateTime.Now;
                    if (now - lastReportTime >= reportInterval || totalRead >= fiLen)
                    {
                        var pct = totalBytes > 0 ? (double)processedBytes / (double)totalBytes * 100 : 0;
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
                
                zipStream.CloseEntry();
            }

            progress?.Report(new ArchiveProgress { PercentComplete = 100, FilePercentComplete = 100 });
        }
        catch (OperationCanceledException)
        {
            // 取消时删除未完成的文件
            if (File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch { }
            }
            throw;
        }
    }

    public async Task<IReadOnlyList<ArchiveItem>> ListEntriesAsync(string archivePath, string? password = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // 读取 ZIP 文件条目
            // 支持 GBK 编码（解决中文文件名乱码问题）
            ZipStrings.CodePage = 936; // GBK
            using var zipFile = new ZipFile(archivePath);
            if (!string.IsNullOrEmpty(password))
            {
                zipFile.Password = password;
            }

            return zipFile.Cast<ZipEntry>()
                .Select(e => new ArchiveItem
                {
                    Name = e.Name,  // 保持完整路径
                    FullPath = e.IsDirectory ? e.Name.TrimEnd('/') : e.Name,
                    Size = e.Size,
                    CompressedSize = e.CompressedSize,
                    LastModified = e.DateTime,
                    IsDirectory = e.IsDirectory,
                    IsEncrypted = e.IsCrypted,
                    Crc32 = (int)e.Crc
                })
                .ToList();
        }, cancellationToken);
    }

    public async Task<bool> TestArchiveAsync(string archivePath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                ZipStrings.CodePage = 936;
                using var zipFile = new ZipFile(archivePath);
                if (!string.IsNullOrEmpty(password))
                {
                    zipFile.Password = password;
                }

                foreach (ZipEntry entry in zipFile)
                {
                    if (entry.IsDirectory) continue;

                    using var stream = zipFile.GetInputStream(entry);
                    var buffer = new byte[4096];
                    while (stream.Read(buffer, 0, buffer.Length) > 0) { }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }, cancellationToken);
    }

    public async Task AddToArchiveAsync(string archivePath, string[] sourcePaths, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            ZipStrings.CodePage = 936;

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
                        var entryName = Path.Combine(dirName, Path.GetRelativePath(sourcePath, file));
                        files.Add((file, entryName));
                    }
                }
                else if (File.Exists(sourcePath))
                {
                    files.Add((sourcePath, Path.GetFileName(sourcePath)));
                }
            }

            if (files.Count == 0) return;

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
                    CurrentFile = "正在添加: " + entryName,
                    ProcessedFiles = i + 1,
                    TotalFiles = totalFiles,
                    PercentComplete = (double)(i + 1) / totalFiles * 100,
                    FilePercentComplete = 100
                });
            }

            zipFile.CommitUpdate();

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100,
                FilePercentComplete = 100
            });
        }, cancellationToken);
    }
}