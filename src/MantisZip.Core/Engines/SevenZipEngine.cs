using System.Diagnostics;
using System.Text;
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

    public bool CanHandle(ArchiveFormat format) => format is ArchiveFormat.SevenZip or ArchiveFormat.Rar or ArchiveFormat.Iso;

    public async Task ExtractAsync(string archivePath, string destinationPath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default, ArchiveOptions? options = null)
    {
        CoreLog.Entry();
        CoreLog.Info($"ExtractAsync: {archivePath} -> {destinationPath}, password={(password != null ? "***" : "null")}");
        var sw = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            // 有密码时用带密码的构造器，这样才能解密每个条目
            using var archiveFile = string.IsNullOrEmpty(password)
                ? new ArchiveFile(archivePath)
                : new ArchiveFile(archivePath, password);
            // 第一遍：统计条目数 + 检查加密（避免 ToList() 全量加载到内存）
            int totalEntries = 0;
            bool hasEncrypted = false;
            foreach (var entry in archiveFile.Entries)
            {
                totalEntries++;
                if (!entry.IsFolder && entry.IsEncrypted)
                    hasEncrypted = true;
            }

            CoreLog.Info($"ExtractAsync: {totalEntries} entries in archive");

            if (hasEncrypted && string.IsNullOrEmpty(password))
            {
                CoreLog.Info("ExtractAsync: archive has encrypted entries but no password provided");
                throw new InvalidOperationException("此压缩包已加密，请输入密码 (This archive is encrypted, password required)");
            }

            {
                // 第二遍：逐条目提取（支持密码 + 冲突处理）
                var lastReportTime = DateTime.Now;
                var reportInterval = TimeSpan.FromMilliseconds(100);
                int fileIndex = 0;
                int i = 0;

                foreach (var entry in archiveFile.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (entry.IsFolder) { i++; continue; }

                    fileIndex++;
                    var pct = totalEntries > 0 ? (double)i / totalEntries * 100 : 0;

                    var outputPath = FileConflictHelper.GetSafePath(destinationPath, entry.FileName);
                    var outDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                        Directory.CreateDirectory(outDir);

                    // 冲突处理
                    var resolvedPath = FileConflictHelper.ResolvePath(outputPath, options, entry.LastWriteTime, (long)entry.Size);
                    if (resolvedPath == null)
                    {
                        i++;
                        continue; // Skip
                    }

                    // 报告当前文件
                    var now = DateTime.Now;
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = entry.FileName,
                        PercentComplete = pct,
                        FilePercentComplete = 0
                    });

                    // 用流方式提取
                    var entryModified = entry.LastWriteTime;
                    using (var fileStream = new FileStream(resolvedPath, FileMode.Create, FileAccess.Write))
                    {
                        entry.Extract(fileStream);
                    }
                    // 恢复文件原始修改时间（流已关闭）
                    try { File.SetLastWriteTime(resolvedPath, entryModified); }
                    catch (Exception tsEx) { CoreLog.Info($"ExtractAsync: failed to set timestamp on {resolvedPath}: {tsEx.Message}"); }

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

                    i++;
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

        await Task.Run(async () =>
        {
            var sevenZipPath = SevenZipPath; // 快照，防止设置窗口在压缩中修改路径
            if (!File.Exists(sevenZipPath))
            {
                throw new FileNotFoundException(
                    $"找不到 7z.exe，请确保已安装 7-Zip 或在设置中正确配置路径。当前路径: {sevenZipPath}",
                    sevenZipPath);
            }

            // 使用 ArgumentList 而非手动拼装 Arguments 字符串，以消除命令注入风险
            // 注: 密码仍可通过进程列表看到（7z.exe 未提供 stdin 密码输入），但至少 shell 参数注入被消除
            var psi = new ProcessStartInfo
            {
                FileName = sevenZipPath,
                UseShellExecute = false,
                RedirectStandardOutput = false,  // 不重定向 stdout（不使用输出内容，避免管道阻塞）
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            psi.ArgumentList.Add("a");                    // 添加
            psi.ArgumentList.Add("-t7z");                  // 7z 格式
            psi.ArgumentList.Add(outputPath);               // 输出文件
            foreach (var src in sourcePaths)
                psi.ArgumentList.Add(src);

            // 压缩级别 (0-9, 0=store, 1=fastest, 9=ultra)
            var mx = Math.Clamp(options.CompressionLevel, 0, 9);
            psi.ArgumentList.Add($"-mx{mx}");

            string? passwordFile = null;
            try
            {
                // 加密：用临时响应文件传递密码，避免出现在进程命令行（Process Explorer 可见）
                if (options.Encrypt && !string.IsNullOrEmpty(options.Password))
                {
                    passwordFile = Path.Combine(Path.GetTempPath(), $"MantisZip_pwd_{Guid.NewGuid()}.tmp");
                    await File.WriteAllTextAsync(passwordFile, $"-p{options.Password}", CancellationToken.None).ConfigureAwait(false);
                    psi.ArgumentList.Add($"@{passwordFile}");
                    psi.ArgumentList.Add("-mhe=on"); // 加密头部
                }

                // 分卷压缩
                if (options.SplitSize > 0)
                {
                    psi.ArgumentList.Add($"-v{options.SplitSize}b");
                }

                CoreLog.Info($"CompressAsync: 7z.exe output={outputPath}, mx={mx}, encrypt={options.Encrypt}");

                using var process = Process.Start(psi)
                    ?? throw new InvalidOperationException($"无法启动 7z.exe: {sevenZipPath}");

                // 异步读取 stderr，防止管道缓冲区满导致进程阻塞
                var errorBuilder = new StringBuilder();
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };
                process.BeginErrorReadLine();

                // 取消时立即终止 7z 进程
                using (cancellationToken.Register(() =>
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                }))
                {
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }

                process.WaitForExit(); // 刷新异步输出缓冲区

                cancellationToken.ThrowIfCancellationRequested();

                var exitCode = process.ExitCode;
                if (exitCode != 0)
                {
                    var error = errorBuilder.ToString();
                    CoreLog.Error($"CompressAsync: 7z.exe exited with code {exitCode}: {error}");
                    throw new Exception($"7z.exe 错误 (code {exitCode}): {error}");
                }

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = string.Empty,
                    PercentComplete = 100
                });

                CoreLog.Info($"CompressAsync: done, exitCode=0, {sw.ElapsedMilliseconds}ms");
            }
            finally
            {
                if (passwordFile != null)
                {
                    try { File.Delete(passwordFile); } catch { }
                }
            }
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

            using var archiveFile = string.IsNullOrEmpty(password)
                ? new ArchiveFile(archivePath)
                : new ArchiveFile(archivePath, password);
            foreach (var entry in archiveFile.Entries)
            {
                // 统一路径分隔符为 /（RAR 文件可能使用 \）
                string fileName = entry.FileName.Replace('\\', '/');
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

                // SevenZipExtractor 构造器验证压缩包签名，访问 Entries 读取目录。
                // 两者都通过说明压缩包结构完整。
                var count = archiveFile.Entries.Count;
                CoreLog.Info($"TestArchiveAsync: passed, {count} entries");
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

    public async Task AddToArchiveAsync(string archivePath, string[] sourcePaths, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default, string? entryBasePath = null)
    {
        CoreLog.Entry();
        CoreLog.Info($"AddToArchiveAsync: {archivePath}, sources=[{string.Join("; ", sourcePaths)}]");
        var sw = Stopwatch.StartNew();

        await Task.Run(async () =>
        {
            var sevenZipPath = SevenZipPath; // 快照，防止设置窗口在修改路径
            if (!File.Exists(sevenZipPath))
            {
                throw new FileNotFoundException(
                    $"找不到 7z.exe，请确保已安装 7-Zip 或在设置中正确配置路径。当前路径: {sevenZipPath}",
                    sevenZipPath);
            }

            var psi = new ProcessStartInfo
            {
                FileName = sevenZipPath,
                UseShellExecute = false,
                RedirectStandardOutput = false,  // 不重定向 stdout，避免管道阻塞
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            psi.ArgumentList.Add("u");                     // 更新
            psi.ArgumentList.Add(archivePath);              // 目标压缩包
            foreach (var src in sourcePaths)
                psi.ArgumentList.Add(src);

            // 压缩级别 (0-9)
            var mx = Math.Clamp(options.CompressionLevel, 0, 9);
            psi.ArgumentList.Add($"-mx{mx}");

            string? passwordFile = null;
            try
            {
                // 加密：用临时响应文件传递密码，避免出现在进程命令行（Process Explorer 可见）
                if (options.Encrypt && !string.IsNullOrEmpty(options.Password))
                {
                    passwordFile = Path.Combine(Path.GetTempPath(), $"MantisZip_pwd_{Guid.NewGuid()}.tmp");
                    await File.WriteAllTextAsync(passwordFile, $"-p{options.Password}", CancellationToken.None).ConfigureAwait(false);
                    psi.ArgumentList.Add($"@{passwordFile}");
                    psi.ArgumentList.Add("-mhe=on");
                }

                CoreLog.Info($"AddToArchiveAsync: 7z.exe archive={archivePath}, mx={mx}, encrypt={options.Encrypt}");

                using var process = Process.Start(psi)
                    ?? throw new InvalidOperationException($"无法启动 7z.exe: {sevenZipPath}");

                // 异步读取 stderr，防止管道缓冲区满导致进程阻塞
                var errorBuilder = new StringBuilder();
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };
                process.BeginErrorReadLine();

                while (!process.HasExited)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        process.Kill();
                        throw new OperationCanceledException();
                    }
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }

                process.WaitForExit(); // 确保异步读取完成

                var exitCode = process.ExitCode;
                if (exitCode != 0)
                {
                    var error = errorBuilder.ToString();
                    CoreLog.Error($"AddToArchiveAsync: 7z.exe exited with code {exitCode}: {error}");
                    throw new Exception($"7z.exe 错误 (code {exitCode}): {error}");
                }

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = string.Empty,
                    PercentComplete = 100
                });

                CoreLog.Info($"AddToArchiveAsync: done, exitCode=0, {sw.ElapsedMilliseconds}ms");
            }
            finally
            {
                if (passwordFile != null)
                {
                    try { File.Delete(passwordFile); } catch { }
                }
            }
        }, cancellationToken);

        CoreLog.Exit();
    }
}
