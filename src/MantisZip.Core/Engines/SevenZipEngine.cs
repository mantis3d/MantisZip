using System.Diagnostics;
using SharpSevenZip;
using SharpSevenZip.EventArguments;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;

namespace MantisZip.Core.Engines;

/// <summary>
/// 7z 压缩引擎 — 使用 SharpSevenZip (7z.dll COM 绑定)
/// 读取操作使用 SharpSevenZipExtractor，写入操作使用 SharpSevenZipCompressor
/// </summary>
public class SevenZipEngine : IArchiveEngine
{
    #region 7z.dll 路径配置

    private static bool _libraryPathInitialized;
    private static readonly object _libraryLock = new();

    /// <summary>
    /// 7z.dll 路径（SharpSevenZip 通过 COM 加载 7z.dll）。
    /// 默认自动探测标准安装路径，可在应用启动时从 AppSettings 覆写。
    /// </summary>
    public static string SevenZipDllPath { get; set; } = ResolveDefaultSevenZipDllPath();

    /// <summary>
    /// 7z.dll 解析回调 — 由 UI 层注册。
    /// 当默认位置找不到 7z.dll 时调用，返回用户手动指定的路径，或 null（用户取消）。
    /// </summary>
    public static Func<string?>? SevenZipDllResolveCallback { get; set; }

    /// <summary>
    /// 向后兼容 — 设置/获取 7z.exe 路径，实际映射到 7z.dll。
    /// 尽量使用 <see cref="SevenZipDllPath"/> 替代。
    /// </summary>
    [Obsolete("Use SevenZipDllPath instead. SharpSevenZip uses 7z.dll, not 7z.exe.")]
    public static string SevenZipPath
    {
        get => Path.ChangeExtension(SevenZipDllPath, ".exe");
        set => SevenZipDllPath = Path.ChangeExtension(value, ".dll");
    }

    private static string ResolveDefaultSevenZipDllPath()
    {
        var candidates = new List<string>
        {
            // 应用目录下的平台子目录（SharpSevenZip 默认搜索路径）
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Environment.Is64BitProcess ? "x64" : "x86", "7z.dll"),
            // 标准 7-Zip 安装路径
            @"C:\Program Files\7-Zip\7z.dll",
            @"C:\Program Files (x86)\7-Zip\7z.dll",
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    /// <summary>
    /// 确保 SharpSevenZipLibraryManager 已配置 7z.dll 路径（线程安全，只执行一次）。
    /// </summary>
    internal static void EnsureLibraryPath()
    {
        if (_libraryPathInitialized) return;
        lock (_libraryLock)
        {
            if (_libraryPathInitialized) return;

            if (File.Exists(SevenZipDllPath))
            {
                SharpSevenZipBase.SetLibraryPath(SevenZipDllPath);
                CoreLog.Info($"SevenZipEngine: 7z.dll path set: {SevenZipDllPath}");
                _libraryPathInitialized = true;
                return;
            }

            // 默认位置未找到 — 尝试回调让用户手动指定
            CoreLog.Info($"SevenZipEngine: 7z.dll not found at {SevenZipDllPath}, invoking user resolve callback");
            var callback = SevenZipDllResolveCallback;
            if (callback != null)
            {
                try
                {
                    var userPath = callback();
                    if (!string.IsNullOrEmpty(userPath) && File.Exists(userPath))
                    {
                        SevenZipDllPath = userPath;
                        SharpSevenZipBase.SetLibraryPath(userPath);
                        CoreLog.Info($"SevenZipEngine: 7z.dll path set via user callback: {userPath}");
                        _libraryPathInitialized = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    CoreLog.Info($"SevenZipEngine: user resolve callback failed: {ex.Message}");
                }
            }

            CoreLog.Info($"SevenZipEngine: 7z.dll not found at any location. " +
                         "SharpSevenZip operations will fail with SharpSevenZipLibraryException.");
            _libraryPathInitialized = true; // 标记已尝试，避免每步都弹
        }
    }

    #endregion

    #region 压缩级别映射

    private static CompressionLevel MapCompressionLevel(int level) => level switch
    {
        0 => CompressionLevel.None,
        1 or 2 => CompressionLevel.Fast,
        3 or 4 => CompressionLevel.Low,
        5 or 6 => CompressionLevel.Normal,
        7 or 8 => CompressionLevel.High,
        9 => CompressionLevel.Ultra,
        _ => CompressionLevel.Normal,
    };

    #endregion

    #region 源路径展开

    /// <summary>
    /// 将源路径（可能含目录）展开为扁平的文件/目录列表。
    /// 目录被递归展开，同时保留空目录项。
    /// </summary>
    private static string[] ExpandSourcePaths(string[] sourcePaths)
    {
        var entries = new List<string>();
        foreach (var path in sourcePaths)
        {
            if (Directory.Exists(path))
            {
                // 保留目录本身（确保空目录也会出现在归档中）
                entries.Add(path);
                // 递归所有文件
                entries.AddRange(Directory.GetFiles(path, "*", SearchOption.AllDirectories));
                // 递归所有子目录
                entries.AddRange(Directory.GetDirectories(path, "*", SearchOption.AllDirectories));
            }
            else if (File.Exists(path))
            {
                entries.Add(path);
            }
        }
        return entries.ToArray();
    }

    #endregion

    #region 进度挂接

    /// <summary>
    /// 将 SharpSevenZipCompressor 的进度事件桥接到 IProgress&lt;ArchiveProgress&gt;。
    /// 注意：SharpSevenZipCompressor 无公开取消 API，CancellationToken 在操作前后检查。
    /// </summary>
    private static void AttachCompressorProgress(
        SharpSevenZipCompressor compr,
        IProgress<ArchiveProgress>? progress)
    {
        if (progress == null)
            return;

        double accumulatedPercent = 0;
        string currentFile = "";

        compr.FileCompressionStarted += (_, e) =>
        {
            currentFile = e.FileName ?? "";
        };

        compr.Compressing += (_, e) =>
        {
            accumulatedPercent = Math.Min(100, accumulatedPercent + e.PercentDelta);
            progress.Report(new ArchiveProgress
            {
                CurrentFile = currentFile,
                PercentComplete = accumulatedPercent,
                FilePercentComplete = accumulatedPercent,
            });
        };
    }

    #endregion

    #region 压缩器通用配置

    private static void ConfigureCompressor(SharpSevenZipCompressor compr, ArchiveOptions options)
    {
        compr.ArchiveFormat = OutArchiveFormat.SevenZip;
        compr.CompressionLevel = MapCompressionLevel(options.CompressionLevel);
        compr.CompressionMethod = CompressionMethod.Lzma2;
        compr.IncludeEmptyDirectories = true;
        compr.DirectoryStructure = true;

        // 加密（密码通过 CompressFilesEncrypted/CompressDirectory 的方法参数传递）
        if (options.Encrypt && !string.IsNullOrEmpty(options.Password))
        {
            compr.EncryptHeaders = true;
        }

        // 分卷
        if (options.SplitSize > 0)
        {
            compr.VolumeSize = options.SplitSize;
        }
    }

    #endregion

    #region IArchiveEngine

    public bool CanHandle(ArchiveFormat format) =>
        format is ArchiveFormat.SevenZip or ArchiveFormat.Rar or ArchiveFormat.Iso;

    public bool CanAdd(ArchiveFormat format) => format == ArchiveFormat.SevenZip;

    public bool CanDelete(ArchiveFormat format) => format == ArchiveFormat.SevenZip;

    #endregion

    #region ExtractAsync（SharpSevenZipExtractor）

    public async Task ExtractAsync(
        string archivePath, string destinationPath,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default,
        ArchiveOptions? options = null)
    {
        CoreLog.Entry();
        CoreLog.Info($"ExtractAsync: {archivePath} -> {destinationPath}, password={(password != null ? "***" : "null")}");
        var sw = Stopwatch.StartNew();

        EnsureLibraryPath();

        await Task.Run(() =>
        {
            using var extractor = string.IsNullOrEmpty(password)
                ? new SharpSevenZipExtractor(archivePath)
                : new SharpSevenZipExtractor(archivePath, password);

            // 检查是否有加密条目但未提供密码
            bool hasEncrypted = extractor.ArchiveFileData.Any(e => !e.IsDirectory && e.Encrypted);
            if (hasEncrypted && string.IsNullOrEmpty(password))
            {
                CoreLog.Info("ExtractAsync: archive has encrypted entries but no password provided");
                throw new InvalidOperationException(
                    "此压缩包已加密，请输入密码 (This archive is encrypted, password required)");
            }

            var allEntries = extractor.ArchiveFileData.ToList();
            int totalFiles = allEntries.Count(e => !e.IsDirectory);
            int processedFiles = 0;
            var lastReportTime = DateTime.Now;
            var reportInterval = TimeSpan.FromMilliseconds(100);

            // 逐条目提取（使用 ExtractFile(index, stream) 支持所有 7z 类型包括 solid 归档）
            for (int i = 0; i < allEntries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = allEntries[i];
                if (entry.IsDirectory)
                {
                    // 创建目录结构
                    var dirPath = FileConflictHelper.GetSafePath(destinationPath, entry.FileName);
                    if (!Directory.Exists(dirPath))
                        Directory.CreateDirectory(dirPath);
                    continue;
                }

                string fileName = entry.FileName.Replace('\\', '/');
                var outputPath = FileConflictHelper.GetSafePath(destinationPath, fileName);
                var outDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                    Directory.CreateDirectory(outDir);

                var resolvedPath = FileConflictHelper.ResolvePath(outputPath, options, entry.LastWriteTime, (long)entry.Size);
                if (resolvedPath == null)
                {
                    // 跳过（跳过/覆盖旧/覆盖小）
                    continue;
                }

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = fileName,
                    PercentComplete = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 0,
                    FilePercentComplete = 0,
                    TotalFiles = totalFiles,
                    ProcessedFiles = processedFiles,
                });

                using (var fileStream = new FileStream(resolvedPath, FileMode.Create, FileAccess.Write))
                {
                    extractor.ExtractFile(entry.Index, fileStream);
                }

                try { File.SetLastWriteTime(resolvedPath, entry.LastWriteTime); }
                catch (Exception tsEx)
                {
                    CoreLog.Info($"ExtractAsync: failed to set timestamp on {resolvedPath}: {tsEx.Message}");
                }

                processedFiles++;

                var now = DateTime.Now;
                if (now - lastReportTime >= reportInterval || processedFiles == totalFiles)
                {
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = fileName,
                        PercentComplete = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 100,
                        FilePercentComplete = 100,
                        TotalFiles = totalFiles,
                        ProcessedFiles = processedFiles,
                    });
                    lastReportTime = now;
                }
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100,
                TotalFiles = totalFiles,
                ProcessedFiles = processedFiles,
            });

            CoreLog.Info($"ExtractAsync: done, {sw.ElapsedMilliseconds}ms");
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }

    #endregion

    #region CompressAsync（SharpSevenZipCompressor）

    public async Task CompressAsync(
        string[] sourcePaths, string outputPath, ArchiveOptions options,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"CompressAsync: [{string.Join("; ", sourcePaths)}] -> {outputPath}, level={options.CompressionLevel}");
        var sw = Stopwatch.StartNew();

        EnsureLibraryPath();

        await Task.Run(() =>
        {
            var compr = new SharpSevenZipCompressor();
            ConfigureCompressor(compr, options);
            AttachCompressorProgress(compr, progress);

            if (sourcePaths.Length == 1 && Directory.Exists(sourcePaths[0]))
            {
                // 单一目录 — 使用 CompressDirectory 保留目录根
                compr.PreserveDirectoryRoot = true;
                compr.CompressDirectory(
                    sourcePaths[0],
                    outputPath,
                    options.Encrypt ? options.Password ?? "" : "",
                    "*",
                    recursion: true);
            }
            else
            {
                // 多个文件或混合 — 展开后使用 CompressFilesEncrypted
                var files = ExpandSourcePaths(sourcePaths);
                compr.CompressFilesEncrypted(
                    outputPath,
                    options.Encrypt ? options.Password ?? "" : "",
                    files);
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100,
            });

            CoreLog.Info($"CompressAsync: done, {sw.ElapsedMilliseconds}ms");
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }

    #endregion

    #region ListEntriesAsync（SharpSevenZipExtractor）

    public async Task<IReadOnlyList<ArchiveItem>> ListEntriesAsync(
        string archivePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"ListEntriesAsync: {archivePath}");
        var sw = Stopwatch.StartNew();

        EnsureLibraryPath();

        var result = await Task.Run(() =>
        {
            using var extractor = string.IsNullOrEmpty(password)
                ? new SharpSevenZipExtractor(archivePath)
                : new SharpSevenZipExtractor(archivePath, password);

            var items = extractor.ArchiveFileData
                .Where(entry =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return true;
                })
                .Select(entry =>
                {
                    string fileName = entry.FileName.Replace('\\', '/');
                    bool isDir = entry.IsDirectory;

                    return new ArchiveItem
                    {
                        Name = fileName,
                        FullPath = isDir ? fileName.TrimEnd('/') : fileName,
                        Size = isDir ? 0 : (long)entry.Size,
                        CompressedSize = 0, // SharpSevenZip 不提供逐项压缩后大小
                        LastModified = entry.LastWriteTime,
                        IsDirectory = isDir,
                        IsEncrypted = entry.Encrypted,
                        Crc32 = isDir ? 0 : (int)entry.Crc,
                    };
                })
                .ToList();

            CoreLog.Info($"ListEntriesAsync: {items.Count} entries, {sw.ElapsedMilliseconds}ms");
            return (IReadOnlyList<ArchiveItem>)items;
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
        return result;
    }

    #endregion

    #region TestArchiveAsync（SharpSevenZipExtractor）

    public async Task<bool> TestArchiveAsync(
        string archivePath,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"TestArchiveAsync: {archivePath}");

        EnsureLibraryPath();

        var result = await Task.Run(() =>
        {
            try
            {
                using var extractor = string.IsNullOrEmpty(password)
                    ? new SharpSevenZipExtractor(archivePath)
                    : new SharpSevenZipExtractor(archivePath, password);

                // SharpSevenZipExtractor.Check() 调用 7z.dll 的 OpenArchive + Test
                bool valid = extractor.Check();

                // 即使 Check() 通过，也枚举条目以验证可读性
                var entries = extractor.ArchiveFileData.ToList();
                int totalEntries = entries.Count;

                for (int i = 0; i < totalEntries; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entries[i].IsDirectory) continue;

                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = entries[i].FileName,
                        PercentComplete = totalEntries > 0 ? (double)(i + 1) / totalEntries * 100 : 100,
                    });
                }

                CoreLog.Info($"TestArchiveAsync: passed, {totalEntries} entries, valid={valid}");
                return valid;
            }
            catch (Exception ex)
            {
                CoreLog.Error($"TestArchiveAsync: failed", ex);
                return false;
            }
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
        return result;
    }

    #endregion

    #region ExtractEntriesAsync（SharpSevenZipExtractor）

    public async Task ExtractEntriesAsync(
        string archivePath,
        IReadOnlyList<string> entryKeys,
        string destinationPath,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default,
        ArchiveOptions? options = null)
    {
        CoreLog.Entry();
        CoreLog.Info($"ExtractEntriesAsync: {archivePath}, {entryKeys.Count} entries -> {destinationPath}");
        var sw = Stopwatch.StartNew();

        var keySet = new HashSet<string>(entryKeys.Select(k => k.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);

        EnsureLibraryPath();

        await Task.Run(() =>
        {
            using var extractor = string.IsNullOrEmpty(password)
                ? new SharpSevenZipExtractor(archivePath)
                : new SharpSevenZipExtractor(archivePath, password);

            var allEntries = extractor.ArchiveFileData.ToList();
            int totalTarget = allEntries.Count(e => !e.IsDirectory && keySet.Contains(e.FileName.Replace('\\', '/')));
            int processed = 0;
            var lastReportTime = DateTime.Now;
            var reportInterval = TimeSpan.FromMilliseconds(100);

            foreach (var entry in allEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fileName = entry.FileName.Replace('\\', '/');

                // 只提取请求的条目
                if (!keySet.Contains(fileName))
                    continue;

                if (entry.IsDirectory)
                {
                    var dirPath = FileConflictHelper.GetSafePath(destinationPath, fileName);
                    if (!Directory.Exists(dirPath))
                        Directory.CreateDirectory(dirPath);
                    continue;
                }

                var outputPath = FileConflictHelper.GetSafePath(destinationPath, fileName);
                var outDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                    Directory.CreateDirectory(outDir);

                var resolvedPath = FileConflictHelper.ResolvePath(outputPath, options, entry.LastWriteTime, (long)entry.Size);
                if (resolvedPath == null)
                    continue;

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = fileName,
                    PercentComplete = totalTarget > 0 ? (double)processed / totalTarget * 100 : 0,
                    TotalFiles = totalTarget,
                    ProcessedFiles = processed,
                });

                using (var fileStream = new FileStream(resolvedPath, FileMode.Create, FileAccess.Write))
                {
                    extractor.ExtractFile(entry.Index, fileStream);
                }

                try { File.SetLastWriteTime(resolvedPath, entry.LastWriteTime); }
                catch (Exception tsEx)
                {
                    CoreLog.Info($"ExtractEntriesAsync: failed to set timestamp on {resolvedPath}: {tsEx.Message}");
                }

                processed++;

                var now = DateTime.Now;
                if (now - lastReportTime >= reportInterval || processed == totalTarget)
                {
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = fileName,
                        PercentComplete = totalTarget > 0 ? (double)processed / totalTarget * 100 : 100,
                        TotalFiles = totalTarget,
                        ProcessedFiles = processed,
                    });
                    lastReportTime = now;
                }
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100,
                TotalFiles = totalTarget,
                ProcessedFiles = processed,
            });

            CoreLog.Info($"ExtractEntriesAsync: done, {sw.ElapsedMilliseconds}ms");
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }

    #endregion

    #region DeleteEntriesAsync（SharpSevenZip 提取-重打包）

    public async Task DeleteEntriesAsync(
        string archivePath,
        string[] entryPaths,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"DeleteEntriesAsync: {archivePath}, entries=[{string.Join("; ", entryPaths)}]");
        var sw = Stopwatch.StartNew();

        EnsureLibraryPath();

        await Task.Run(() =>
        {
            // 1. 列出所有条目，排除要删除项
            var keepEntries = new List<(string path, bool isDir)>();
            using (var extractor = string.IsNullOrEmpty(password)
                       ? new SharpSevenZipExtractor(archivePath)
                       : new SharpSevenZipExtractor(archivePath, password))
            {
                var deletedSet = new HashSet<string>(entryPaths.Select(p => p.Replace('\\', '/')));
                foreach (var entry in extractor.ArchiveFileData)
                {
                    var normalized = entry.FileName.Replace('\\', '/');
                    if (!deletedSet.Contains(normalized))
                    {
                        keepEntries.Add((normalized, entry.IsDirectory));
                    }
                }
            }

            if (keepEntries.Count == 0)
            {
                // 所有条目都被删除 — 删除原文件
                try { File.Delete(archivePath); } catch { }
                CoreLog.Info($"DeleteEntriesAsync: all entries deleted, removed archive");
                return;
            }

            // 2. 将保留条目解压到临时目录（逐项提取，支持 solid 归档）
            var tempDir = Path.Combine(Path.GetTempPath(), "MantisZip", "DeleteTemp", Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(tempDir);

                using (var extractor = string.IsNullOrEmpty(password)
                           ? new SharpSevenZipExtractor(archivePath)
                           : new SharpSevenZipExtractor(archivePath, password))
                {
                    // 建立 fileName → ArchiveFileInfo 索引
                    var entryMap = extractor.ArchiveFileData
                        .ToDictionary(e => e.FileName.Replace('\\', '/'), e => e, StringComparer.OrdinalIgnoreCase);

                    int total = keepEntries.Count;
                    int processed = 0;
                    foreach (var (path, isDir) in keepEntries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (isDir)
                        {
                            var dirPath = Path.Combine(tempDir, path);
                            if (!Directory.Exists(dirPath))
                                Directory.CreateDirectory(dirPath);
                            continue;
                        }

                        if (!entryMap.TryGetValue(path, out var entry))
                            continue;

                        var outPath = Path.Combine(tempDir, path);
                        var outDir = Path.GetDirectoryName(outPath);
                        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                            Directory.CreateDirectory(outDir);

                        using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
                        {
                            extractor.ExtractFile(entry.Index, fs);
                        }

                        processed++;
                        var pct = total > 0 ? (double)processed / total * 100 : 100;
                        progress?.Report(new ArchiveProgress
                        {
                            CurrentFile = path,
                            PercentComplete = pct * 0.5, // 提取阶段占 50%
                        });
                    }
                }

                // 3. 用 SharpSevenZipCompressor 重打包
                var tempArchive = Path.Combine(Path.GetTempPath(), "MantisZip", "DeleteTemp",
                    $"{Guid.NewGuid()}.7z");
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(tempArchive)!);

                    var compr = new SharpSevenZipCompressor();
                    compr.ArchiveFormat = OutArchiveFormat.SevenZip;
                    compr.CompressionLevel = CompressionLevel.Normal;
                    compr.IncludeEmptyDirectories = true;
                    compr.DirectoryStructure = true;

                    if (!string.IsNullOrEmpty(password))
                    {
                        compr.EncryptHeaders = true;
                    }

                    AttachCompressorProgress(compr, progress is not null
                        ? new Progress<ArchiveProgress>(p =>
                        {
                            progress.Report(new ArchiveProgress
                            {
                                CurrentFile = p.CurrentFile,
                                PercentComplete = 50 + p.PercentComplete * 0.5, // 压缩阶段占 50%
                            });
                        })
                        : null);

                    compr.PreserveDirectoryRoot = true;
                    compr.CompressDirectory(tempDir, tempArchive, password ?? "", "*", true);

                    // 4. 替换原归档
                    File.Delete(archivePath);
                    File.Move(tempArchive, archivePath);

                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = string.Empty,
                        PercentComplete = 100,
                    });

                    CoreLog.Info($"DeleteEntriesAsync: done, {sw.ElapsedMilliseconds}ms");
                }
                finally
                {
                    if (File.Exists(tempArchive))
                    {
                        try { File.Delete(tempArchive); } catch { }
                    }
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                }
            }
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }

    #endregion

    #region AddToArchiveAsync（SharpSevenZipCompressor Append 模式）

    public async Task AddToArchiveAsync(
        string archivePath,
        string[] sourcePaths,
        ArchiveOptions options,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? entryBasePath = null)
    {
        CoreLog.Entry();
        CoreLog.Info($"AddToArchiveAsync: {archivePath}, sources=[{string.Join("; ", sourcePaths)}]");
        var sw = Stopwatch.StartNew();

        EnsureLibraryPath();

        await Task.Run(() =>
        {
            var compr = new SharpSevenZipCompressor();
            ConfigureCompressor(compr, options);
            compr.CompressionMode = CompressionMode.Append; // 追加到已有归档

            AttachCompressorProgress(compr, progress);

            // 展开源路径，追加到归档
            var files = ExpandSourcePaths(sourcePaths);
            compr.CompressFilesEncrypted(
                archivePath,
                options.Encrypt ? options.Password ?? "" : "",
                files);

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100,
            });

            CoreLog.Info($"AddToArchiveAsync: done, {sw.ElapsedMilliseconds}ms");
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }

    #endregion
}
