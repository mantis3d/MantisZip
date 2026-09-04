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
            // 便携版：exe 同目录下的 7z.dll
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7z.dll"),
            // 应用目录下的平台子目录（SharpSevenZip 默认搜索路径）
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Environment.Is64BitProcess ? "x64" : "x86", "7z.dll"),
            // 标准 7-Zip 安装路径
            @"C:\Program Files\7-Zip\7z.dll",
            @"C:\Program Files (x86)\7-Zip\7z.dll",
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    /// <summary>
    /// 7z.dll 状态信息，供设置窗口 UI 显示。
    /// </summary>
    public class SevenZipDllStatus
    {
        /// <summary>当前配置的 7z.dll 路径。</summary>
        public string ConfiguredPath { get; init; } = "";
        /// <summary>该路径的文件是否存在。</summary>
        public bool Exists { get; init; }
        /// <summary>是否已通过 EnsureLibraryPath 初始化（仅标记已尝试）。</summary>
        public bool IsInitialized { get; init; }
    }

    /// <summary>
    /// 获取当前 7z.dll 状态（不触发回调对话框）。
    /// </summary>
    public static SevenZipDllStatus GetSevenZipDllStatus()
    {
        return new SevenZipDllStatus
        {
            ConfiguredPath = SevenZipDllPath,
            Exists = File.Exists(SevenZipDllPath),
            IsInitialized = _libraryPathInitialized,
        };
    }

    /// <summary>
    /// 清除已初始化标记并重新探测默认路径，下次 EnsureLibraryPath 时重新生效。
    /// 通常在用户修改了 7z.dll 路径后调用。
    /// </summary>
    /// <summary>
    /// 清除已初始化标记并重新探测默认路径，下次 EnsureLibraryPath 时重新生效。
    /// 通常在用户清除了手动指定的 7z.dll 路径后调用。
    /// </summary>
    public static void ResetLibraryPath()
    {
        lock (_libraryLock)
        {
            _libraryPathInitialized = false;
            SevenZipDllPath = ResolveDefaultSevenZipDllPath();
        }
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

        // 压缩方法（来自 DynamicFormatOptionsPanel SevenZipCompressionMethod）
        compr.CompressionMethod = options.SevenZipCompressionMethod?.ToLowerInvariant() switch
        {
            "lzma" => CompressionMethod.Lzma,
            "lzma2" => CompressionMethod.Lzma2,
            "ppmd" => CompressionMethod.Ppmd,
            "bzip2" => CompressionMethod.BZip2,
            "deflate" => CompressionMethod.Deflate,
            _ => CompressionMethod.Lzma2, // 默认
        };

        // 固实压缩（SharpSevenZip 无原生属性，通过 CustomParameters 传递）
        if (!string.IsNullOrEmpty(options.SevenZipSolidBlockSize))
        {
            // 既有固实块大小值，设为 s=N 直接启用固实+指定块大小
            compr.CustomParameters["s"] = options.SevenZipSolidBlockSize;
        }
        else if (!options.SevenZipSolid)
        {
            compr.CustomParameters["s"] = "off";
        }
        // 默认（固实开启但无块大小）→ 不设 s，7z.dll 使用默认固实行为

        // 字典大小（仅 LZMA/LZMA2 有效，但设了也无害）
        // 在 SharpSevenZip 中这些是静态属性（全局生效于 7z.dll 上下文）
        if (options.SevenZipDictionarySize.HasValue)
            SharpSevenZipCompressor.LzmaDictionarySize = options.SevenZipDictionarySize.Value;

        // Word Size（快速字节数）
        if (options.SevenZipNumFastBytes.HasValue)
            SharpSevenZipCompressor.LzmaNumFastBytes = options.SevenZipNumFastBytes.Value;

        // 匹配器
        if (!string.IsNullOrEmpty(options.SevenZipMatchFinder))
            SharpSevenZipCompressor.LzmaMatchFinder = options.SevenZipMatchFinder;

        compr.IncludeEmptyDirectories = true;
        compr.DirectoryStructure = true;

        // 加密（密码通过 CompressFilesEncrypted/CompressDirectory 的方法参数传递）
        if (options.Encrypt && !string.IsNullOrEmpty(options.Password))
        {
            compr.EncryptHeaders = options.SevenZipEncryptHeaders;
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

    public async Task<ExtractResult> ExtractAsync(
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

        var result = await Task.Run(async () =>
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
            int failedEntries = 0;
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

                string fileName = ArchivePath.Normalize(entry.FileName);
                var outputPath = FileConflictHelper.GetSafePath(destinationPath, fileName);
                var outDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                    Directory.CreateDirectory(outDir);

                var resolvedPath = await FileConflictHelper.ResolvePathAsync(outputPath, options, entry.LastWriteTime, (long)entry.Size);
                if (resolvedPath == null)
                {
                    // 跳过（跳过/覆盖旧/覆盖小）
                    continue;
                }

                var entrySize = (long)entry.Size;

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = fileName,
                    PercentComplete = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 0,
                    FilePercentComplete = 0,
                    TotalFiles = totalFiles,
                    ProcessedFiles = processedFiles,
                });

                try
                {
                    // 使用 WriteProgressStream 在 ExtractFile 写入过程中获得逐块进度
                    var lastFileReport = DateTime.Now;
                    using (var fileStream = new FileStream(resolvedPath, FileMode.Create, FileAccess.Write))
                    using (var progressStream = new WriteProgressStream(fileStream, bytesWritten =>
                    {
                        // 每 100ms 更新一次进度（与 ZipEngine 一致），避免 UI 过载
                        var now = DateTime.Now;
                        if (now - lastFileReport < reportInterval && bytesWritten < entrySize)
                            return;

                        var filePct = entrySize > 0 ? (double)bytesWritten / entrySize * 100 : 100;
                        var overallPct = totalFiles > 0
                            ? (double)(processedFiles + (double)bytesWritten / entrySize) / totalFiles * 100
                            : 0;

                        progress?.Report(new ArchiveProgress
                        {
                            CurrentFile = fileName,
                            PercentComplete = Math.Min(overallPct, 100),
                            FilePercentComplete = Math.Min(filePct, 100),
                            TotalFiles = totalFiles,
                            ProcessedFiles = processedFiles,
                        });
                        lastFileReport = now;
                    }))
                    {
                        extractor.ExtractFile(entry.Index, progressStream);
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
                catch (UnauthorizedAccessException uax)
                {
                    CoreLog.Info($"ExtractAsync: permission denied for '{fileName}': {uax.Message}");
                    failedEntries++;
                }
                catch (IOException iox)
                {
                    // 目标文件被其他进程占用等 IO 失败：跳过该条目继续，避免单个文件中止整个解压
                    CoreLog.Info($"ExtractAsync: write failed for '{fileName}': {iox.Message}");
                    failedEntries++;
                }
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100,
                TotalFiles = totalFiles,
                ProcessedFiles = processedFiles,
            });

            CoreLog.Info($"ExtractAsync: done, {sw.ElapsedMilliseconds}ms, failedEntries={failedEntries}");
            return new ExtractResult { SucceededEntries = processedFiles, FailedEntries = failedEntries };
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
        return result;
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
            try
            {
                var compr = new SharpSevenZipCompressor();
                ConfigureCompressor(compr, options);
                AttachCompressorProgress(compr, progress);

                // 预检：7z 原生压缩是单次调用，无法逐文件恢复。先展开待压缩文件集，
                // 统一做读权限预检（文件被占用/权限不足 → ErrorResolver 弹窗 重试/跳过/中止），
                // 跳过则从文件集中剔除，避免单个不可读文件导致整个压缩直接中止。
                var files = ExpandSourcePaths(sourcePaths);
                if (options.FileWhitelist != null)
                    files = files.Where(f => options.FileWhitelist.Contains(f)).ToArray();
                var validated = ReadErrorHandler.FilterUnreadableFiles(files, options, cancellationToken);

                // 单一目录·无白名单·且无文件被跳过 → 保留 CompressDirectory 的 PreserveDirectoryRoot 语义
                bool singleDirClean = sourcePaths.Length == 1
                    && Directory.Exists(sourcePaths[0])
                    && options.FileWhitelist == null
                    && validated.Count == files.Length;
                if (singleDirClean)
                {
                    // 单一目录且无文件白名单 — 使用 CompressDirectory
                    compr.PreserveDirectoryRoot = options.PreserveDirectoryRoot;
                    compr.CompressDirectory(
                        sourcePaths[0],
                        outputPath,
                        options.Encrypt ? options.Password ?? "" : "",
                        "*",
                        recursion: true);
                }
                else if (validated.Count > 0)
                {
                    // 多个文件、混合源、存在文件白名单、或预检跳过了不可读文件 —
                    // 展开后按白名单过滤并经预检剔除，再使用 CompressFilesEncrypted
                    compr.CompressFilesEncrypted(
                        outputPath,
                        options.Encrypt ? options.Password ?? "" : "",
                        validated.ToArray());
                }
                else
                {
                    // 所有文件均不可读且被跳过 → 无内容可压缩
                    CoreLog.Info("SevenZipEngine.CompressAsync: all files skipped due to read errors, nothing to compress");
                }

                // 压缩完成后必须把文件进度条也置满（仅 PercentComplete=100 时文件进度条会停在 accumulatedPercent）
                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = string.Empty,
                    PercentComplete = 100,
                    FilePercentComplete = 100,
                });

                CoreLog.Info($"CompressAsync: done, {sw.ElapsedMilliseconds}ms");
            }
            catch (OperationCanceledException)
            {
                CoreLog.Info("CompressAsync: cancelled, cleaning up partial output");
                if (File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); } catch (Exception cleanupEx) { CoreLog.Error("CompressAsync: failed to clean up partial output", cleanupEx); }
                }
                throw;
            }
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
                    string fileName = ArchivePath.Normalize(entry.FileName);
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

                // 1. 校验压缩包结构（7z.dll 的 Check 会验证头信息和结构完整性）
                bool valid = extractor.Check();

                // 2. 逐条目解压到空流以验证每条数据的完整性（CRC 由 7z.dll 内部校验）
                var entries = extractor.ArchiveFileData.ToList();
                int totalEntries = entries.Count;
                int processed = 0;

                for (int i = 0; i < totalEntries; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (entries[i].IsDirectory)
                    {
                        processed++;
                        continue;
                    }

                    // ExtractFile 为原子调用（内部校验 CRC），无法获取单文件中间进度；
                    // 提取前后各上报一次 0%/100% 以驱动文件进度条
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = entries[i].FileName,
                        PercentComplete = totalEntries > 0 ? (double)processed / totalEntries * 100 : 100,
                        FilePercentComplete = 0,
                        TotalFiles = totalEntries,
                        ProcessedFiles = processed,
                    });

                    // 实际解压条目到空流 — 7z.dll 在 ExtractFile 内部会校验 CRC
                    extractor.ExtractFile(entries[i].Index, Stream.Null);

                    processed++;

                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = entries[i].FileName,
                        PercentComplete = totalEntries > 0 ? (double)processed / totalEntries * 100 : 100,
                        FilePercentComplete = 100,
                        TotalFiles = totalEntries,
                        ProcessedFiles = processed,
                    });
                }

                CoreLog.Info($"TestArchiveAsync: passed, {totalEntries} entries verified, valid={valid}");
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
        ArchiveOptions? options = null,
        IReadOnlyDictionary<string, string>? outputPathOverrides = null)
    {
        CoreLog.Entry();
        CoreLog.Info($"ExtractEntriesAsync: {archivePath}, {entryKeys.Count} entries -> {destinationPath}");
        var sw = Stopwatch.StartNew();

        var keySet = new HashSet<string>(entryKeys.Select(k => ArchivePath.Normalize(k)), StringComparer.OrdinalIgnoreCase);

        EnsureLibraryPath();

        await Task.Run(async () =>
        {
            using var extractor = string.IsNullOrEmpty(password)
                ? new SharpSevenZipExtractor(archivePath)
                : new SharpSevenZipExtractor(archivePath, password);

            var allEntries = extractor.ArchiveFileData.ToList();
            int totalTarget = allEntries.Count(e => !e.IsDirectory && keySet.Contains(ArchivePath.Normalize(e.FileName)));
            int processed = 0;
            int failedEntries = 0;
            var lastReportTime = DateTime.Now;
            var reportInterval = TimeSpan.FromMilliseconds(100);

            foreach (var entry in allEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fileName = ArchivePath.Normalize(entry.FileName);

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

                var outputPath = outputPathOverrides?.GetValueOrDefault(fileName)
                    ?? FileConflictHelper.GetSafePath(destinationPath, fileName);
                var outDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                    Directory.CreateDirectory(outDir);

                var resolvedPath = await FileConflictHelper.ResolvePathAsync(outputPath, options, entry.LastWriteTime, (long)entry.Size);
                if (resolvedPath == null)
                    continue;

                var entrySize = (long)entry.Size;

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = fileName,
                    PercentComplete = totalTarget > 0 ? (double)processed / totalTarget * 100 : 0,
                    TotalFiles = totalTarget,
                    ProcessedFiles = processed,
                });

                // 使用 WriteProgressStream 在 ExtractFile 写入过程中获得逐块进度
                var lastFileReport = DateTime.Now;
                try
                {
                    using (var fileStream = new FileStream(resolvedPath, FileMode.Create, FileAccess.Write))
                    using (var progressStream = new WriteProgressStream(fileStream, bytesWritten =>
                    {
                        var now = DateTime.Now;
                        if (now - lastFileReport < reportInterval && bytesWritten < entrySize)
                            return;

                        var filePct = entrySize > 0 ? (double)bytesWritten / entrySize * 100 : 100;
                        var overallPct = totalTarget > 0
                            ? (double)(processed + (double)bytesWritten / entrySize) / totalTarget * 100
                            : 0;

                        progress?.Report(new ArchiveProgress
                        {
                            CurrentFile = fileName,
                            PercentComplete = Math.Min(overallPct, 100),
                            FilePercentComplete = Math.Min(filePct, 100),
                            TotalFiles = totalTarget,
                            ProcessedFiles = processed,
                        });
                        lastFileReport = now;
                    }))
                    {
                        extractor.ExtractFile(entry.Index, progressStream);
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
                            FilePercentComplete = 100,
                            TotalFiles = totalTarget,
                            ProcessedFiles = processed,
                        });
                        lastReportTime = now;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (UnauthorizedAccessException uax)
                {
                    CoreLog.Info($"ExtractEntriesAsync: permission denied for '{fileName}': {uax.Message}");
                    failedEntries++;
                }
                catch (IOException iox)
                {
                    // 目标文件被其他进程占用等 IO 失败：跳过该条目继续，避免单个文件中止整个解压
                    CoreLog.Info($"ExtractEntriesAsync: write failed for '{fileName}': {iox.Message}");
                    failedEntries++;
                }
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100,
                TotalFiles = totalTarget,
                ProcessedFiles = processed,
            });

            CoreLog.Info($"ExtractEntriesAsync: done, {sw.ElapsedMilliseconds}ms, failedEntries={failedEntries}");
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
                var deletedSet = new HashSet<string>(entryPaths.Select(p => ArchivePath.Normalize(p)));
                foreach (var entry in extractor.ArchiveFileData)
                {
                    var normalized = ArchivePath.Normalize(entry.FileName);
                    if (!deletedSet.Contains(normalized))
                    {
                        keepEntries.Add((normalized, entry.IsDirectory));
                    }
                }
            }

            if (keepEntries.Count == 0)
            {
                // 所有条目都被删除 — 删除原文件
                try { File.Delete(archivePath); } catch { CoreLog.Trace("SevenZipEngine: failed to delete empty archive '{0}'", archivePath); }
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
                        .ToDictionary(e => ArchivePath.Normalize(e.FileName), e => e, StringComparer.OrdinalIgnoreCase);

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
                        try { File.Delete(tempArchive); } catch { CoreLog.Trace("SevenZipEngine: failed to delete temp archive '{0}'", tempArchive); }
                    }
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { CoreLog.Trace("SevenZipEngine: failed to delete temp dir '{0}'", tempDir); }
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

        await Task.Run(async () =>
        {
            var compr = new SharpSevenZipCompressor();
            ConfigureCompressor(compr, options);
            compr.CompressionMode = CompressionMode.Append; // 追加到已有归档

            AttachCompressorProgress(compr, progress);

            // entryBasePath 前缀（与 ZipEngine 语义一致）："docs" → 条目名 "docs/<相对路径>"。
            // 空/根目录时无前缀，条目落在归档根目录。
            var basePath = string.IsNullOrEmpty(entryBasePath) ? "" : entryBasePath.TrimEnd('/') + "/";

            // entry 名 → 源文件绝对路径的字典。
            // CompressFileDictionary 将字典 key 原样作为归档条目名（"/" 分隔），
            // 借此精确控制添加到当前浏览目录，取代旧的 CompressFilesEncrypted（只能按公共根推导条目名）。
            // 注意：不添加目录条目（null 值）——SharpSevenZip 的 ArchiveUpdateCallback.GetStream
            // 对 null 流会抛 NullReferenceException（7z.dll 在 Update 模式会对目录项调用 GetStream）。
            // 目录结构由文件路径隐式生成，归档内/UI 目录树均能正确还原。
            var fileDict = new Dictionary<string, string>();
            foreach (var sourcePath in sourcePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Directory.Exists(sourcePath))
                {
                    var dirName = ArchivePath.GetFileName(sourcePath);
                    foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
                    {
                        // FileWhitelist（来自压缩预览过滤 B）命中时只添加匹配文件，保证 预览=实际。
                        // 白名单值为预览收集的原始绝对路径（\ 分隔），与 FileScanner 匹配方式一致，勿 Normalize。
                        if (options.FileWhitelist != null && !options.FileWhitelist.Contains(file))
                            continue;
                        var relativePath = Path.Combine(dirName, Path.GetRelativePath(sourcePath, file));
                        fileDict[basePath + relativePath.Replace('\\', '/')] = file;
                    }
                }
                else if (File.Exists(sourcePath))
                {
                    fileDict[basePath + Path.GetFileName(sourcePath)] = sourcePath;
                }
            }

            if (fileDict.Count == 0)
            {
                CoreLog.Info("AddToArchiveAsync: no files to add (whitelist filtered)");
                return;
            }

            // 收集压缩包现有条目（名称/大小/时间/索引）供冲突处理
            // 注意：加密文件名（EncryptHeaders）的 7z 需密码才能列出条目，与 AddToArchiveAsync 既有约束一致
            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingEntryInfo = new Dictionary<string, (int Index, long Size, DateTime Modified)>(StringComparer.OrdinalIgnoreCase);
            using (var extractor = string.IsNullOrEmpty(options.Password)
                       ? new SharpSevenZipExtractor(archivePath)
                       : new SharpSevenZipExtractor(archivePath, options.Password))
            {
                foreach (var e in extractor.ArchiveFileData)
                {
                    if (e.IsDirectory) continue;
                    var normalized = ArchivePath.Normalize(e.FileName);
                    existingNames.Add(normalized);
                    existingEntryInfo[normalized] = (e.Index, (long)e.Size, e.LastWriteTime);
                }
            }

            // 解析条目名冲突（语义方向反转见 AddConflictHelper；覆盖 = 先删旧条目再追加）
            var occupiedNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
            var finalDict = new Dictionary<string, string>();
            var deleteIndexes = new Dictionary<int, string>();
            foreach (var (entryName, sourcePath) in fileDict)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalized = ArchivePath.Normalize(entryName);
                existingEntryInfo.TryGetValue(normalized, out var existing);
                var fi = new FileInfo(sourcePath);
                var finalName = await AddConflictHelper.ResolveEntryNameAsync(
                    normalized, options, existing.Modified, existing.Size, fi.LastWriteTime, fi.Length, occupiedNames);
                if (finalName == null)
                {
                    CoreLog.Info($"AddToArchiveAsync: skipped '{entryName}' (conflict action)");
                    continue;
                }
                if (existingNames.Contains(normalized) && finalName == normalized)
                    deleteIndexes[existing.Index] = null!; // 覆盖：ModifyArchive 传 null 值 = 删除该索引条目
                finalDict[finalName] = sourcePath;
            }

            if (finalDict.Count == 0)
            {
                CoreLog.Info("AddToArchiveAsync: all files skipped by conflict handling");
                return;
            }

            // 覆盖条目先删除（探针验证：ModifyArchive(index→null) 删除有效），再追加
            if (deleteIndexes.Count > 0)
            {
                CoreLog.Info($"AddToArchiveAsync: deleting {deleteIndexes.Count} overwritten entries via ModifyArchive");
                var delCompr = new SharpSevenZipCompressor { ArchiveFormat = OutArchiveFormat.SevenZip };
                delCompr.ModifyArchive(archivePath, deleteIndexes, options.Encrypt ? options.Password ?? "" : "");
            }

            compr.CompressFileDictionary(
                finalDict,
                archivePath,
                options.Encrypt ? options.Password ?? "" : "");

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
