using System.Diagnostics;
using System.Linq;
using SevenZipExtractor;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using System.IO;

namespace MantisZip.Core.Engines;

/// <summary>
/// 7z 压缩引擎
/// </summary>
public class SevenZipEngine : IArchiveEngine
{
    /// <summary>
    /// 7z.exe 路径。默认值指向标准安装路径。
    /// 可在应用启动时从 AppSettings 覆写：
    /// <c>SevenZipEngine.SevenZipPath = AppSettings.Instance.SevenZipPath;</c>
    /// </summary>
    public static string SevenZipPath { get; set; } = @"C:\Program Files\7-Zip\7z.exe";

    public bool CanHandle(ArchiveFormat format) => format is ArchiveFormat.SevenZip or ArchiveFormat.Rar;

    public async Task ExtractAsync(string archivePath, string destinationPath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"ExtractAsync: {archivePath} -> {destinationPath}, password={(password != null ? "***" : "null")}");
        var sw = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            using var archiveFile = new ArchiveFile(archivePath);
            var entries = archiveFile.Entries.ToList();
            CoreLog.Info($"ExtractAsync: {entries.Count} entries in archive");

            // 检查是否有加密条目但未提供密码
            var hasEncrypted = entries.Any(e => !e.IsFolder && e.IsEncrypted);
            if (hasEncrypted && string.IsNullOrEmpty(password))
            {
                CoreLog.Info("ExtractAsync: archive has encrypted entries but no password provided");
                throw new InvalidOperationException("此压缩包已加密，请输入密码 (This archive is encrypted, password required)");
            }

            if (!string.IsNullOrEmpty(password))
            {
                // 有密码时用批量 API（逐条目提取不支持密码）
                archiveFile.Extract(destinationPath, overwrite: true, password: password);

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = string.Empty,
                    PercentComplete = 100
                });
            }
            else
            {
                // 无加密时的逐条目提取并报告进度
                var lastReportTime = DateTime.Now;
                var reportInterval = TimeSpan.FromMilliseconds(100);
                int fileIndex = 0;
                int totalEntries = entries.Count;

                for (int i = 0; i < totalEntries; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entry = entries[i];
                    if (entry.IsFolder) continue;

                    fileIndex++;
                    var pct = totalEntries > 0 ? (double)i / totalEntries * 100 : 0;

                    var outputPath = Path.Combine(destinationPath, entry.FileName);
                    var outDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                        Directory.CreateDirectory(outDir);

                    // 报告当前文件
                    var now = DateTime.Now;
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = entry.FileName,
                        PercentComplete = pct,
                        FilePercentComplete = 0
                    });

                    // 用流方式提取（支持覆盖写入）
                    using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                    entry.Extract(fileStream);

                    // 文件完成时再报告一次
                    now = DateTime.Now;
                    if (now - lastReportTime >= reportInterval || i == totalEntries - 1)
                    {
                        progress?.Report(new ArchiveProgress
                        {
                            CurrentFile = entry.FileName,
                            PercentComplete = totalEntries > 0 ? (double)(i + 1) / totalEntries * 100 : 100,
                            FilePercentComplete = 100
                        });
                        lastReportTime = now;
                    }
                }

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = string.Empty,
                    PercentComplete = 100
                });
            }

            CoreLog.Info($"ExtractAsync: done, {sw.ElapsedMilliseconds}ms");
        }, cancellationToken);

        CoreLog.Exit();
    }

    public async Task CompressAsync(string[] sourcePaths, string outputPath, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"CompressAsync: [{string.Join("; ", sourcePaths)}] -> {outputPath}, level={options.CompressionLevel}");
        var sw = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            // 检查 7z.exe 是否存在
            if (!File.Exists(SevenZipPath))
            {
                throw new FileNotFoundException(
                    $"找不到 7z.exe，请确保已安装 7-Zip 或在设置中正确配置路径。当前路径: {SevenZipPath}",
                    SevenZipPath);
            }

            // 7z.exe 命令: 7z a -t7z output files [-mxN] [-p"password"]
            var args = new List<string>
            {
                "a",                    // 添加
                "-t7z",                  // 7z 格式
                outputPath                 // 输出文件
            };
            args.AddRange(sourcePaths);

            // 压缩级别 (0-9, 0=store, 1=fastest, 9=ultra)
            var mx = Math.Clamp(options.CompressionLevel, 0, 9);
            args.Add($"-mx{mx}");

            // 加密 (必须放在文件列表之后)
            if (options.Encrypt && !string.IsNullOrEmpty(options.Password))
            {
                // 注意: 密码通过命令行参数传递，在进程列表中可见。
                // 为降低特殊字符导致的问题，密码参数整体用双引号包裹。
                args.Add($"-p\"{options.Password}\"");
                args.Add("-mhe=on"); // 加密头部
            }

            // 分卷压缩
            if (options.SplitSize > 0)
            {
                args.Add($"-v{options.SplitSize}b");
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

            CoreLog.Info($"CompressAsync: 7z.exe args: {psi.Arguments}");

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException($"无法启动 7z.exe: {SevenZipPath}");

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
                CoreLog.Error($"CompressAsync: 7z.exe exited with code {exitCode}: {error}");
                throw new Exception($"7z.exe 错误 (code {exitCode}): {error}");
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100
            });

            CoreLog.Info($"CompressAsync: done, exitCode=0, {sw.ElapsedMilliseconds}ms");
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
                    IsEncrypted = entry.IsEncrypted
                });
            }

            CoreLog.Info($"ListEntriesAsync: {items.Count} entries, {sw.ElapsedMilliseconds}ms");
            return (IReadOnlyList<ArchiveItem>)items;
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
                using var archiveFile = string.IsNullOrEmpty(password)
                    ? new ArchiveFile(archivePath)
                    : new ArchiveFile(archivePath, password);
                var count = archiveFile.Entries.Count;
                CoreLog.Info($"TestArchiveAsync: passed, {count} entries");
                return count >= 0;
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

    public async Task AddToArchiveAsync(string archivePath, string[] sourcePaths, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default, string? entryBasePath = null)
    {
        CoreLog.Entry();
        CoreLog.Info($"AddToArchiveAsync: {archivePath}, sources=[{string.Join("; ", sourcePaths)}]");
        var sw = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            // 检查 7z.exe 是否存在
            if (!File.Exists(SevenZipPath))
            {
                throw new FileNotFoundException(
                    $"找不到 7z.exe，请确保已安装 7-Zip 或在设置中正确配置路径。当前路径: {SevenZipPath}",
                    SevenZipPath);
            }

            // 7z.exe 命令: 7z u archive.7z file1 file2
            var args = new List<string>
            {
                "u",                     // 更新
                archivePath               // 目标压缩包
            };
            args.AddRange(sourcePaths);

            // 压缩级别 (0-9)
            var mx = Math.Clamp(options.CompressionLevel, 0, 9);
            args.Add($"-mx{mx}");

            // 加密
            if (options.Encrypt && !string.IsNullOrEmpty(options.Password))
            {
                args.Add($"-p\"{options.Password}\"");
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

            CoreLog.Info($"AddToArchiveAsync: 7z.exe args: {psi.Arguments}");

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException($"无法启动 7z.exe: {SevenZipPath}");

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
                CoreLog.Error($"AddToArchiveAsync: 7z.exe exited with code {exitCode}: {error}");
                throw new Exception($"7z.exe 错误 (code {exitCode}): {error}");
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100
            });

            CoreLog.Info($"AddToArchiveAsync: done, exitCode=0, {sw.ElapsedMilliseconds}ms");
        }, cancellationToken);

        CoreLog.Exit();
    }
}
