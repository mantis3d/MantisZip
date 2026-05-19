using System.Diagnostics;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
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
                // TAR 或 TAR.GZ 解压 - 单遍扫描，使用压缩流位置估算总体进度
                // 不预扫描文件数（避免对 .tar.gz 重复解压缩），进度基于已读取的压缩字节数
                using var inputStream = File.OpenRead(archivePath);
                var totalCompressedBytes = inputStream.Length;
                Stream tarStream = isTarGz
                    ? new GZipInputStream(inputStream)
                    : inputStream;

                using var tarIn = new TarInputStream(tarStream, Encoding.UTF8);

                TarEntry entry;
                int fileIndex = 0;
                var lastReportTime = DateTime.Now;
                var reportInterval = TimeSpan.FromMilliseconds(100);

                while ((entry = tarIn.GetNextEntry()) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (entry.IsDirectory)
                    {
                        // 创建空目录
                        var dirPath = FileConflictHelper.GetSafePath(destinationPath, entry.Name);
                        if (!Directory.Exists(dirPath))
                            Directory.CreateDirectory(dirPath);
                        continue;
                    }

                    fileIndex++;
                    // 总体进度基于已读取的压缩字节（对于 .tar.gz 是合理的近似，避免了双遍扫描）
                    var compressedProgress = totalCompressedBytes > 0
                        ? (double)inputStream.Position / totalCompressedBytes * 100
                        : 0;

                    var outputFilePath = FileConflictHelper.GetSafePath(destinationPath, entry.Name);
                    var outDir = Path.GetDirectoryName(outputFilePath);
                    if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                        Directory.CreateDirectory(outDir);

                    // 逐文件报告
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = entry.Name,
                        PercentComplete = Math.Min(compressedProgress, 99.9), // 留到结束报告 100%
                        FilePercentComplete = 0
                    });

                    // 冲突处理
                    var resolved = FileConflictHelper.ResolvePath(outputFilePath, options, entry.ModTime, entry.Size);
                    if (resolved == null)
                    {
                        // 跳过文件但需要消费 Tar 流数据以推进到下一个条目
                        var discardBuf = new byte[81920];
                        while (tarIn.Read(discardBuf, 0, discardBuf.Length) > 0) { }
                        continue;
                    }

                    // 带 per-file 进度的复制
                    var entryModified = entry.ModTime;
                    using (var outStream = File.Create(resolved))
                    {
                        var buffer = new byte[81920];
                        long totalRead = 0;
                        long entrySize = entry.Size;

                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var read = tarIn.Read(buffer, 0, buffer.Length);
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
                                    CurrentFile = entry.Name,
                                    PercentComplete = Math.Min(compressedProgress, 99.9),
                                    FilePercentComplete = filePct
                                });
                                lastReportTime = now;
                            }
                        }
                    }
                    // 恢复文件原始修改时间（流已关闭）
                    try { File.SetLastWriteTime(resolved, entryModified); } catch (Exception tsEx) { CoreLog.Info($"ExtractAsync: failed to set timestamp on {resolved}: {tsEx.Message}"); }
                }
            }
            else if (ext == ".gz")
            {
                // 单纯 GZip 解压单个文件
                using var gzip = new GZipInputStream(File.OpenRead(archivePath));
                var outputPath = Path.Combine(destinationPath, Path.GetFileNameWithoutExtension(archivePath));
                var resolved = FileConflictHelper.ResolvePath(outputPath, options);
                if (resolved != null)
                {
                    using var output = File.Create(resolved);
                    gzip.CopyTo(output);
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
        }, cancellationToken);

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

            // 收集所有文件（使用 FileScanner 共享工具，边发现边报告进度）
            var (files, _) = FileScanner.CollectFiles(sourcePaths, progress, cancellationToken);

            CoreLog.Info($"CompressAsync: {files.Count} files to compress");

            if (isTarGz || ext == ".tar")
            {
                using var fileStream = File.Create(outputPath);
                using var gzipOutput = isTarGz ? new GZipOutputStream(fileStream) : null;
                using var tarArchive = gzipOutput != null
                    ? TarArchive.CreateOutputTarArchive(gzipOutput)
                    : TarArchive.CreateOutputTarArchive(fileStream);

                if (gzipOutput != null) gzipOutput.SetLevel(options.CompressionLevel);

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

                    if (!TarReadFileWithRetry(fullPath, relativePath, options, tarArchive, cancellationToken))
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        continue;
                    }
                    processedFiles++;
                }

                tarArchive.Close();
            }
            else if (ext == ".gz")
            {
                // 单纯 GZip 压缩（单文件）
                if (files.Count > 0)
                {
                    using var outputStream = File.Create(outputPath);
                    using var gzipOutput = new GZipOutputStream(outputStream);
                    gzipOutput.SetLevel(options.CompressionLevel);

                    using var input = File.OpenRead(files[0].FullPath);
                    input.CopyTo(gzipOutput);
                }
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100
            });

            CoreLog.Info($"CompressAsync: done, {sw.ElapsedMilliseconds}ms");
        }, cancellationToken);

        CoreLog.Exit();
    }

    /// <summary>
    /// 带重试/跳过/中止的 TAR 文件压缩。返回 false 表示跳过此文件。
    /// </summary>
    private static bool TarReadFileWithRetry(string fullPath, string relativePath,
        ArchiveOptions options, TarArchive tarArchive, CancellationToken ct)
    {
        int retries = 3;
        while (retries > 0)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var entry = TarEntry.CreateEntryFromFile(fullPath);
                entry.Name = relativePath;
                tarArchive.WriteEntry(entry, false);
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
                    Stream tarStream = inputStream;

                    if (isTarGz)
                    {
                        // tar.gz: 先解压 GZip 流，再读取 Tar
                        tarStream = new GZipInputStream(inputStream);
                    }

                    using var tarIn = new TarInputStream(tarStream, Encoding.UTF8);
                    TarEntry entry;
                    while ((entry = tarIn.GetNextEntry()) != null)
                    {
                        items.Add(new ArchiveItem
                        {
                            Name = entry.Name,
                            FullPath = entry.IsDirectory ? entry.Name.TrimEnd('/') : entry.Name,
                            Size = entry.Size,
                            CompressedSize = entry.Size,
                            LastModified = entry.ModTime,
                            IsDirectory = entry.IsDirectory,
                            IsEncrypted = false
                        });
                    }
                }
                catch (Exception ex)
                {
                    CoreLog.Error($"ListEntriesAsync: parse error", ex);
                    // 忽略解析错误，返回已解析的部分
                }
            }
            else if (ext == ".gz")
            {
                // GZip 单文件
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
        }, cancellationToken);

        CoreLog.Exit();
        return result;
    }

    public async Task DeleteEntriesAsync(string archivePath, string[] entryPaths, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"DeleteEntriesAsync: {archivePath} — NotSupportedException");
        await Task.Run(() =>
        {
            throw new NotSupportedException("TAR/GZ 格式不支持直接删除文件，请重新创建压缩包");
        }, cancellationToken);
    }

    public async Task AddToArchiveAsync(string archivePath, string[] sourcePaths, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default, string? entryBasePath = null)
    {
        CoreLog.Entry();
        CoreLog.Info($"AddToArchiveAsync: {archivePath} — NotSupportedException");
        await Task.Run(() =>
        {
            throw new NotSupportedException("TAR/GZ 格式不支持直接添加文件，请重新创建压缩包");
        }, cancellationToken);
        // No CoreLog.Exit() — exception propagates
    }

    public async Task<bool> TestArchiveAsync(string archivePath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"TestArchiveAsync: {archivePath}");

        try
        {
            // ListEntriesAsync 内部已做 Task.Run，无需再包一层
            var items = await ListEntriesAsync(archivePath, password, cancellationToken);
            var ok = items.Count > 0;
            CoreLog.Info($"TestArchiveAsync: {(ok ? "passed" : "failed (no entries)")}, {items.Count} entries");
            return ok;
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
}
