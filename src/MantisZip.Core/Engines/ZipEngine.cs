using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core.Abstractions;
using System.IO;

namespace MantisZip.Core.Engines;

/// <summary>
/// ZIP 压缩引擎
/// </summary>
public class ZipEngine : IArchiveEngine
{
    public bool CanHandle(ArchiveFormat format) => format == ArchiveFormat.Zip;

    public async Task ExtractAsync(string archivePath, string destinationPath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
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

                if (entry.IsDirectory) continue;

                var outputPath = Path.Combine(destinationPath, entry.Name);
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                var progressReport = new ArchiveProgress
                {
                    CurrentFile = entry.Name,
                    TotalFiles = entries.Count,
                    ProcessedFiles = processedFiles,
                    TotalBytes = totalBytes,
                    ProcessedBytes = processedBytes,
                    PercentComplete = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : 0
                };
                progress?.Report(progressReport);

                using var inputStream = zipFile.GetInputStream(entry);
                using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                inputStream.CopyTo(outputStream);

                processedBytes += entry.Size;
                processedFiles++;
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                TotalFiles = entries.Count,
                ProcessedFiles = processedFiles,
                TotalBytes = totalBytes,
                ProcessedBytes = processedBytes,
                PercentComplete = 100
            });
        }, cancellationToken);
    }

    public async Task CompressAsync(string[] sourcePaths, string outputPath, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            using var fsOut = File.Create(outputPath);
            using var zipStream = new ZipOutputStream(fsOut);

            zipStream.SetLevel(options.CompressionLevel);

            if (options.Encrypt && !string.IsNullOrEmpty(options.Password))
            {
                zipStream.Password = options.Password;
            }

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

            var totalBytes = files.Sum(f => new FileInfo(f.FullPath).Length);
            var processedBytes = 0L;
            var processedFiles = 0;

            foreach (var (fullPath, relativePath) in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fi = new FileInfo(fullPath);
                var entry = new ZipEntry(ZipEntry.CleanName(relativePath))
                {
                    DateTime = fi.LastWriteTime,
                    Size = fi.Length,
                    AESKeySize = options.Encrypt ? 256 : 0
                };

                zipStream.PutNextEntry(entry);

                var buffer = new byte[4096];
                using var fsInput = File.OpenRead(fullPath);
                StreamUtils.Copy(fsInput, zipStream, buffer);
                zipStream.CloseEntry();

                var progressReport = new ArchiveProgress
                {
                    CurrentFile = relativePath,
                    TotalFiles = files.Count,
                    ProcessedFiles = processedFiles,
                    TotalBytes = totalBytes,
                    ProcessedBytes = processedBytes,
                    PercentComplete = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : 0
                };
                progress?.Report(progressReport);

                processedBytes += fi.Length;
                processedFiles++;
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                TotalFiles = files.Count,
                ProcessedFiles = processedFiles,
                TotalBytes = totalBytes,
                ProcessedBytes = processedBytes,
                PercentComplete = 100
            });
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<ArchiveItem>> ListEntriesAsync(string archivePath, string? password = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
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
}