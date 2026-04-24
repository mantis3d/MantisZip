using SevenZipExtractor;
using MantisZip.Core.Abstractions;
using System.IO;

namespace MantisZip.Core.Engines;

/// <summary>
/// 7z 压缩引擎
/// </summary>
public class SevenZipEngine : IArchiveEngine
{
    public bool CanHandle(ArchiveFormat format) => format == ArchiveFormat.SevenZip;

    public async Task ExtractAsync(string archivePath, string destinationPath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            using var archiveFile = new ArchiveFile(archivePath);
            if (!string.IsNullOrEmpty(password))
            {
                archiveFile.Extract(destinationPath, overwrite: true, password: password);
            }
            else
            {
                archiveFile.Extract(destinationPath, overwrite: true);
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100
            });
        }, cancellationToken);
    }

    public async Task CompressAsync(string[] sourcePaths, string outputPath, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // SevenZipExtractor 主要用于解压
        // 压缩功能暂时返回空操作，后续可以使用 Process 调用 7z.exe 或其他方式实现
        await Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ArchiveItem>> ListEntriesAsync(string archivePath, string? password = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var items = new List<ArchiveItem>();

            using var archiveFile = new ArchiveFile(archivePath);
            foreach (var entry in archiveFile.Entries)
            {
                // 检查是否目录（以 / 结尾或没有扩展名的是目录）
                string fileName = entry.FileName;
                bool isDir = string.IsNullOrEmpty(Path.GetExtension(fileName)) || fileName.EndsWith("/");

                items.Add(new ArchiveItem
                {
                    Name = fileName,
                    FullPath = isDir ? fileName.TrimEnd('/') : fileName,
                    Size = isDir ? 0 : (long)entry.Size,
                    CompressedSize = isDir ? 0 : (long)entry.Size,
                    LastModified = entry.LastWriteTime,
                    IsDirectory = isDir,
                    IsEncrypted = false
                });
            }

            return (IReadOnlyList<ArchiveItem>)items;
        }, cancellationToken);
    }

    public async Task<bool> TestArchiveAsync(string archivePath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var archiveFile = new ArchiveFile(archivePath);
                var count = archiveFile.Entries.Count;
                return count >= 0;
            }
            catch
            {
                return false;
            }
        }, cancellationToken);
    }
}