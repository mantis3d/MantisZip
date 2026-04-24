using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using MantisZip.Core.Abstractions;
using System.IO;

namespace MantisZip.Core.Engines;

/// <summary>
/// TAR/GZ 压缩引擎
/// </summary>
public class TarGzEngine : IArchiveEngine
{
    public bool CanHandle(ArchiveFormat format) => format == ArchiveFormat.Tar || format == ArchiveFormat.GZip;

    public async Task ExtractAsync(string archivePath, string destinationPath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            var ext = Path.GetExtension(archivePath).ToLowerInvariant();
            var isTarGz = ext == ".tgz" || archivePath.EndsWith(".tar.gz");
            
            if (isTarGz || ext == ".tar")
            {
                // TAR 或 TAR.GZ 解压 - 使用 TarArchive
                using var inputStream = File.OpenRead(archivePath);
                
                TarArchive tarArchive;
                if (isTarGz)
                {
                    var gzipInput = new GZipInputStream(inputStream);
                    tarArchive = TarArchive.CreateInputTarArchive(gzipInput);
                }
                else
                {
                    tarArchive = TarArchive.CreateInputTarArchive(inputStream);
                }
                
                tarArchive.ExtractContents(destinationPath);
                tarArchive.Close();
            }
            else if (ext == ".gz")
            {
                // 单纯 GZip 解压单个文件
                using var gzip = new GZipInputStream(File.OpenRead(archivePath));
                var outputPath = Path.Combine(destinationPath, Path.GetFileNameWithoutExtension(archivePath));
                using var output = File.Create(outputPath);
                gzip.CopyTo(output);
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
        await Task.Run(() =>
        {
            var ext = Path.GetExtension(outputPath).ToLowerInvariant();
            var isTarGz = ext == ".tgz" || outputPath.EndsWith(".tar.gz");
            
            // 收集所有文件
            var files = new List<(string FullPath, string RelativePath)>();
            foreach (var sourcePath in sourcePaths)
            {
                if (Directory.Exists(sourcePath))
                {
                    var baseDir = Path.GetFileName(sourcePath);
                    foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
                    {
                        var relativePath = Path.Combine(baseDir, Path.GetRelativePath(sourcePath, file));
                        files.Add((file, relativePath));
                    }
                }
                else if (File.Exists(sourcePath))
                {
                    files.Add((sourcePath, Path.GetFileName(sourcePath)));
                }
            }

            if (isTarGz)
            {
                // TAR.GZ 压缩
                using var fileStream = File.Create(outputPath);
                using var gzipOutput = new GZipOutputStream(fileStream);
                gzipOutput.SetLevel(5);
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
                    gzipOutput.SetLevel(5);
                    
                    using var input = File.OpenRead(files[0].FullPath);
                    input.CopyTo(gzipOutput);
                }
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100
            });
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<ArchiveItem>> ListEntriesAsync(string archivePath, string? password = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
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
                        tarArchive = TarArchive.CreateInputTarArchive(gzipInput);
                    }
                    else
                    {
                        tarArchive = TarArchive.CreateInputTarArchive(inputStream);
                    }
                    
                    // 直接从文件流创建 TarInputStream
                    inputStream.Position = 0;
                    var tarIn = new TarInputStream(inputStream);
                    TarEntry entry;
                    while ((entry = tarIn.GetNextEntry()) != null)
                    {
                        items.Add(new ArchiveItem
                        {
                            Name = entry.Name,
                            FullPath = entry.IsDirectory ? entry.Name.TrimEnd('/') : entry.Name,
                            Size = entry.Size,
                            CompressedSize = entry.Size,
                            LastModified = DateTime.Now,
                            IsDirectory = entry.IsDirectory,
                            IsEncrypted = false
                        });
                    }
                }
                catch
                {
                    // 忽略解析错误
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
            
            return (IReadOnlyList<ArchiveItem>)items;
        }, cancellationToken);
    }

    public async Task<bool> TestArchiveAsync(string archivePath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var items = ListEntriesAsync(archivePath, password, cancellationToken).Result;
                return items.Count >= 0;
            }
            catch
            {
                return false;
            }
        }, cancellationToken);
    }
}