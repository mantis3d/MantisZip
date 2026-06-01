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
    public bool CanHandle(ArchiveFormat format) => format == ArchiveFormat.Tar || format == ArchiveFormat.GZip;

    public async Task ExtractAsync(string archivePath, string destinationPath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default, ArchiveOptions? options = null)
    {
        CoreLog.Entry();
        CoreLog.Info($"ExtractAsync: {archivePath} -> {destinationPath}");
        var sw = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            var ext = Path.GetExtension(archivePath).ToLowerInvariant();
            var isTarGz = ext == ".tgz" || archivePath.EndsWith(".tar.gz");
            CoreLog.Info($"ExtractAsync: format=tar.gz={isTarGz}, ext={ext}");

            if (isTarGz || ext == ".tar")
            {
                // TAR 或 TAR.GZ 解压 - 使用 SharpCompress TarReader
                // 注意：不手动解压 GZip，直接传入原始压缩流让 TarReader 自动检测 gzip 头
                using var inputStream = File.OpenRead(archivePath);
                var totalCompressedBytes = inputStream.Length;

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
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        PercentComplete = Math.Min(compressedProgress, 99.9),
                        FilePercentComplete = 0
                    });

                    // 冲突处理
                    var resolved = FileConflictHelper.ResolvePath(outputFilePath, options, entryModified, entry.Size);
                    if (resolved == null)
                    {
                        // 跳过文件，TarReader.MoveToNextEntry 自动处理流推进
                        continue;
                    }

                    // 带 per-file 进度的复制
                    using (var entryStream = reader.OpenEntryStream())
                    using (var outStream = File.Create(resolved))
                    {
                        var buffer = new byte[81920];
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
                                progress?.Report(new ArchiveProgress
                                {
                                    CurrentFile = entryKey,
                                    PercentComplete = Math.Min(compressedProgress, 99.9),
                                    FilePercentComplete = filePct
                                });
                                lastReportTime = now;
                            }
                        }
                    }
                    // 恢复文件原始修改时间
                    try { File.SetLastWriteTime(resolved, entryModified); } catch (Exception tsEx) { CoreLog.Info($"ExtractAsync: failed to set timestamp on {resolved}: {tsEx.Message}"); }
                }
            }
            else if (ext == ".gz")
            {
                // 单纯 GZip 解压单个文件
                using var inputStream = File.OpenRead(archivePath);
                using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
                var outputPath = Path.Combine(destinationPath, Path.GetFileNameWithoutExtension(archivePath));
                var resolved = FileConflictHelper.ResolvePath(outputPath, options);
                if (resolved != null)
                {
                    using var output = File.Create(resolved);
                    gzipStream.CopyTo(output);
                }

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = Path.GetFileName(outputPath),
                    PercentComplete = 100
                });
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100
            });

            CoreLog.Info($"ExtractAsync: done, {sw.ElapsedMilliseconds}ms");
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }

    public async Task CompressAsync(string[] sourcePaths, string outputPath, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"CompressAsync: [{string.Join("; ", sourcePaths)}] -> {outputPath}");
        var sw = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            var ext = Path.GetExtension(outputPath).ToLowerInvariant();
            var isTarGz = ext == ".tgz" || outputPath.EndsWith(".tar.gz");
            CoreLog.Info($"CompressAsync: format=tar.gz={isTarGz}");

            var (files, _) = FileScanner.CollectFiles(sourcePaths, progress, cancellationToken);
            CoreLog.Info($"CompressAsync: {files.Count} files to compress");

            if (isTarGz || ext == ".tar")
            {
                using var fileStream = File.Create(outputPath);
                var compressionType = isTarGz ? CompressionType.GZip : CompressionType.None;
                using SharpCompress.Writers.IWriter writer = TarWriter.OpenWriter(fileStream, new TarWriterOptions(compressionType, true)
                {
                    CompressionLevel = options.CompressionLevel
                });

                int processedFiles = 0;
                int totalFiles = files.Count;
                foreach (var (fullPath, relativePath) in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = relativePath,
                        PercentComplete = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 0,
                        FilePercentComplete = 0,
                        TotalFiles = totalFiles,
                        ProcessedFiles = processedFiles
                    });

                    if (!TarWriteFileWithRetry(fullPath, relativePath, options, writer, cancellationToken))
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        continue;
                    }
                    processedFiles++;

                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = relativePath,
                        PercentComplete = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 0,
                        FilePercentComplete = 100,
                        TotalFiles = totalFiles,
                        ProcessedFiles = processedFiles
                    });
                }
            }
            else if (ext == ".gz")
            {
                // 单纯 GZip 压缩（单文件）
                if (files.Count > 0)
                {
                    using var outputStream = File.Create(outputPath);
                    using var gzipWriter = GZipWriter.OpenWriter(outputStream, new GZipWriterOptions(options.CompressionLevel));

                    using var input = File.OpenRead(files[0].FullPath);
                    gzipWriter.Write(Path.GetFileName(files[0].FullPath), input, null);
                }
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100,
                TotalFiles = files.Count,
                ProcessedFiles = files.Count
            });

            CoreLog.Info($"CompressAsync: done, {sw.ElapsedMilliseconds}ms");
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }

    /// <summary>
    /// 带重试/跳过/中止的 TAR 文件压缩。返回 false 表示跳过此文件。
    /// </summary>
    private static bool TarWriteFileWithRetry(string fullPath, string relativePath,
        ArchiveOptions options, SharpCompress.Writers.IWriter writer, CancellationToken ct)
    {
        int retries = 3;
        while (retries > 0)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var fi = new FileInfo(fullPath);
                using var sourceStream = File.OpenRead(fullPath);
                writer.Write(relativePath, sourceStream, fi.LastWriteTime);
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
        CoreLog.Info($"ListEntriesAsync: {archivePath}");
        var sw = Stopwatch.StartNew();

        var result = await Task.Run(() =>
        {
            var items = new List<ArchiveItem>();
            var ext = Path.GetExtension(archivePath).ToLowerInvariant();
            var isTarGz = ext == ".tgz" || archivePath.EndsWith(".tar.gz");

            if (ext == ".tar" || isTarGz)
            {
                try
                {
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
                catch (Exception ex)
                {
                    CoreLog.Trace($"ListEntriesAsync: parse error: {ex.Message}");
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

    public async Task DeleteEntriesAsync(string archivePath, string[] entryPaths, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
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
        CoreLog.Info($"TestArchiveAsync: {archivePath}");

        try
        {
            await Task.Run(() =>
            {
                var ext = Path.GetExtension(archivePath).ToLowerInvariant();
                var isTarGz = ext == ".tgz" || archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);

                if (ext != ".tar" && !isTarGz)
                    throw new NotSupportedException($"Unsupported extension: {ext}");

                using var inputStream = File.OpenRead(archivePath);
                var totalBytes = inputStream.Length;

                using var reader = TarReader.OpenReader(inputStream, new ReaderOptions { LookForHeader = true });

                int entryIndex = 0;
                while (reader.MoveToNextEntry())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entry = reader.Entry;
                    var entryKey = entry.Key ?? $"entry_{entryIndex}";

                    if (!entry.IsDirectory)
                    {
                        // 读取全量数据触发 GZip CRC32 校验
                        using var entryStream = reader.OpenEntryStream();
                        var buffer = new byte[81920];
                        while (entryStream.Read(buffer, 0, buffer.Length) > 0) { }
                    }

                    entryIndex++;

                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        PercentComplete = totalBytes > 0
                            ? Math.Min((double)inputStream.Position / totalBytes * 100, 100)
                            : 0,
                    });
                }

                // 报告 100%
                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = string.Empty,
                    PercentComplete = 100,
                });

                CoreLog.Info($"TestArchiveAsync: passed, {entryIndex} entries verified");
            }, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            CoreLog.Error($"TestArchiveAsync: failed", ex);
            return false;
        }
        finally
        {
            CoreLog.Exit();
        }
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
        // TAR/GZ 不支持按条目选择性解压（需要完整顺序遍历流）。
        CoreLog.Entry();
        CoreLog.Info($"ExtractEntriesAsync: {archivePath} — NotSupportedException");
        try
        {
            await Task.Run(() =>
            {
                throw new NotSupportedException("TAR/GZ 格式不支持按条目选择性解压，请使用完整 ExtractAsync");
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CoreLog.Exit();
        }
    }
}
