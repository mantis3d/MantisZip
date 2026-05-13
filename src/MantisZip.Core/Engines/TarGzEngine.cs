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

    public async Task ExtractAsync(string archivePath, string destinationPath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
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
                // TAR 或 TAR.GZ 解压 - 使用 TarInputStream 逐文件处理，支持进度
                using var inputStream = File.OpenRead(archivePath);
                Stream tarStream = isTarGz
                    ? new GZipInputStream(inputStream)
                    : inputStream;

                using var tarIn = new TarInputStream(tarStream, Encoding.UTF8);

                // 第一遍：预扫描文件数（用于百分比计算）
                // TarInputStream 只能向前，需重新打开
                int totalFiles = 0;
                using (var scanStream = File.OpenRead(archivePath))
                {
                    Stream scanTarStream = isTarGz
                        ? new GZipInputStream(scanStream)
                        : scanStream;
                    using var scanTarIn = new TarInputStream(scanTarStream, Encoding.UTF8);
                    TarEntry scanEntry;
                    while ((scanEntry = scanTarIn.GetNextEntry()) != null)
                    {
                        if (!scanEntry.IsDirectory) totalFiles++;
                    }
                }
                CoreLog.Info($"ExtractAsync: {totalFiles} files to extract");

                // 第二遍：实际解压并报告进度
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
                        var dirPath = Path.Combine(destinationPath, entry.Name);
                        if (!Directory.Exists(dirPath))
                            Directory.CreateDirectory(dirPath);
                        continue;
                    }

                    fileIndex++;

                    var outputFilePath = Path.Combine(destinationPath, entry.Name);
                    var outDir = Path.GetDirectoryName(outputFilePath);
                    if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                        Directory.CreateDirectory(outDir);

                    // 逐文件报告
                    var overallPct = totalFiles > 0 ? (double)(fileIndex - 1) / totalFiles * 100 : 0;
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = entry.Name,
                        PercentComplete = overallPct,
                        FilePercentComplete = 0
                    });

                    // 带 per-file 进度的复制
                    using var outStream = File.Create(outputFilePath);
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
                            progress?.Report(new ArchiveProgress
                            {
                                CurrentFile = entry.Name,
                                PercentComplete = totalFiles > 0
                                    ? (double)(fileIndex - 1 + totalRead / (double)Math.Max(entrySize, 1)) / totalFiles * 100
                                    : 0,
                                FilePercentComplete = filePct
                            });
                            lastReportTime = now;
                        }
                    }
                }
            }
            else if (ext == ".gz")
            {
                // 单纯 GZip 解压单个文件
                using var gzip = new GZipInputStream(File.OpenRead(archivePath));
                var outputPath = Path.Combine(destinationPath, Path.GetFileNameWithoutExtension(archivePath));
                using var output = File.Create(outputPath);
                gzip.CopyTo(output);

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

            // 收集所有文件（使用 EnumerateFiles 延迟枚举，边发现边报告进度）
            var files = new List<(string FullPath, string RelativePath)>();
            var lastScanReportTime = DateTime.Now;
            var scanReportInterval = TimeSpan.FromMilliseconds(100);

            foreach (var sourcePath in sourcePaths)
            {
                if (Directory.Exists(sourcePath))
                {
                    var baseDir = Path.GetFileName(sourcePath);
                    foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var relativePath = Path.Combine(baseDir, Path.GetRelativePath(sourcePath, file));
                        files.Add((file, relativePath));

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
                }
            }

            CoreLog.Info($"CompressAsync: {files.Count} files to compress");

            if (isTarGz)
            {
                // TAR.GZ 压缩
                using var fileStream = File.Create(outputPath);
                using var gzipOutput = new GZipOutputStream(fileStream);
                gzipOutput.SetLevel(options.CompressionLevel);
                using var tarArchive = TarArchive.CreateOutputTarArchive(gzipOutput);

                foreach (var (fullPath, relativePath) in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = TarEntry.CreateEntryFromFile(fullPath);
                    entry.Name = relativePath;
                    tarArchive.WriteEntry(entry, false);
                }

                tarArchive.Close();
            }
            else if (ext == ".tar")
            {
                // 纯 TAR 压缩
                using var fileStream = File.Create(outputPath);
                using var tarArchive = TarArchive.CreateOutputTarArchive(fileStream);

                foreach (var (fullPath, relativePath) in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = TarEntry.CreateEntryFromFile(fullPath);
                    entry.Name = relativePath;
                    tarArchive.WriteEntry(entry, false);
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
                    TarArchive tarArchive;

                    if (isTarGz)
                    {
                        var gzipInput = new GZipInputStream(inputStream);
                        tarArchive = TarArchive.CreateInputTarArchive(gzipInput, Encoding.UTF8);
                    }
                    else
                    {
                        tarArchive = TarArchive.CreateInputTarArchive(inputStream, Encoding.UTF8);
                    }

                    // 直接从文件流创建 TarInputStream
                    inputStream.Position = 0;
                    var tarIn = new TarInputStream(inputStream, Encoding.UTF8);
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

        var result = await Task.Run(() =>
        {
            try
            {
                var items = ListEntriesAsync(archivePath, password, cancellationToken).Result;
                var ok = items.Count >= 0;
                CoreLog.Info($"TestArchiveAsync: passed, {items.Count} entries");
                return ok;
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
}
