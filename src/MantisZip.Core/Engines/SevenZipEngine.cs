using System.Diagnostics;
using System.Linq;
using SevenZipExtractor;
using MantisZip.Core.Abstractions;
using System.IO;

namespace MantisZip.Core.Engines;

/// <summary>
/// 7z 压缩引擎
/// </summary>
public class SevenZipEngine : IArchiveEngine
{
    private const string SevenZipPath = @"C:\Program Files\7-Zip\7z.exe";

    public bool CanHandle(ArchiveFormat format) => format is ArchiveFormat.SevenZip or ArchiveFormat.Rar;

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
        await Task.Run(() =>
        {
            // 7z.exe 命令: 7z a -t7z output files [-mxN] [-p"password"]
            var args = new List<string>
            {
                "a",                    // 添加
                "-t7z",                  // 7z 格式
                outputPath                 // 输出文件
            };
            args.AddRange(sourcePaths);

            // 压缩级别
            args.Add(options.CompressionLevel switch
            {
                1 => "-mx1",   //  fastest
                5 => "-mx5",   //  fast
                9 => "-mx9",   //  ultra
                _ => "-mx5"    //  normal (default)
            });

            // 加密 (必须放在文件列表之后)
            if (options.Encrypt && !string.IsNullOrEmpty(options.Password))
            {
                args.Add($"-p{options.Password}");
                args.Add("-mhe=on"); // 加密头部
            }

            var psi = new ProcessStartInfo
            {
                FileName = SevenZipPath,
                Arguments = string.Join(" ", args.Select(a => a.Contains(" ") ? $"\"{a}\"" : a)),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return;

            // 简单的进度轮询
            while (!process.HasExited)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    process.Kill();
                    throw new OperationCanceledException();
                }
                Thread.Sleep(100);
            }

            var exitCode = process.ExitCode;
            if (exitCode != 0)
            {
                var error = process.StandardError.ReadToEnd();
                throw new Exception($"7z.exe 错误 (code {exitCode}): {error}");
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

            using var archiveFile = new ArchiveFile(archivePath);
            foreach (var entry in archiveFile.Entries)
            {
                string fileName = entry.FileName;
                bool isDir = entry.IsFolder;

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

    public async Task AddToArchiveAsync(string archivePath, string[] sourcePaths, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default, string? entryBasePath = null)
    {
        await Task.Run(() =>
        {
            // 7z.exe 命令: 7z u archive.7z file1 file2
            var args = new List<string>
            {
                "u",                     // 更新
                archivePath               // 目标压缩包
            };
            args.AddRange(sourcePaths);

            // 压缩级别
            args.Add(options.CompressionLevel switch
            {
                1 => "-mx1",
                5 => "-mx5",
                9 => "-mx9",
                _ => "-mx5"
            });

            // 加密
            if (options.Encrypt && !string.IsNullOrEmpty(options.Password))
            {
                args.Add($"-p{options.Password}");
                args.Add("-mhe=on");
            }

            var psi = new ProcessStartInfo
            {
                FileName = SevenZipPath,
                Arguments = string.Join(" ", args.Select(a => a.Contains(" ") ? $"\"{a}\"" : a)),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return;

            while (!process.HasExited)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    process.Kill();
                    throw new OperationCanceledException();
                }
                Thread.Sleep(100);
            }

            var exitCode = process.ExitCode;
            if (exitCode != 0)
            {
                var error = process.StandardError.ReadToEnd();
                throw new Exception($"7z.exe 错误 (code {exitCode}): {error}");
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100
            });
        }, cancellationToken);
    }
}