using System.Diagnostics;
using SharpSevenZip;
using SharpSevenZip.EventArguments;
using SharpSevenZip.Exceptions;
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
    /// 进程启动时**提前**把 7z.dll 路径交给 SharpSevenZip（不探测、不弹回调对话框）。
    /// <para>
    /// 为什么必须提前：SharpSevenZipLibraryManager 是进程级单例，库一旦被 LoadLibrary 加载，
    /// <c>SetLibraryPath</c> 就只接受「与已加载路径相同」的值，否则抛
    /// <see cref="SharpSevenZipLibraryException"/>（"can not change the library path while the
    /// library ... is being used"）。若某个不经过 <see cref="EnsureLibraryPath"/> 的触达点
    /// （如 CLI 解压前的密码探测）先按 SharpSevenZip 默认探测路径加载了库，之后引擎再设
    /// 用户配置的路径就会失败 —— 即 issue #52（右键「原地解压」报错）。
    /// </para>
    /// 路径不存在时静默返回，仍由 <see cref="EnsureLibraryPath"/> 懒执行 + 回调对话框兜底。
    /// </summary>
    internal static void ConfigureLibraryPathEarly()
    {
        if (_libraryPathInitialized) return;
        lock (_libraryLock)
        {
            if (_libraryPathInitialized) return;
            if (!File.Exists(SevenZipDllPath)) return;

            SetLibraryPathSafe(SevenZipDllPath, "early");
            _libraryPathInitialized = true;
        }
    }

    /// <summary>
    /// 设置 SharpSevenZip 的库路径，异常不外抛、统一记日志。
    /// <para>
    /// SharpSevenZip 2.0.45 没有 <c>TrySetLibraryPath</c>，库已被其它路径加载时只能靠异常识别；
    /// 此时**沿用已加载的库**（它本身可用）而不是让每次操作都重抛 —— 否则整个进程会话的
    /// 7z/RAR/ISO 操作全部失败。
    /// </para>
    /// </summary>
    /// <param name="path">要设置的 7z.dll 路径。</param>
    /// <param name="source">来源标记，仅用于日志区分（early / configured / user callback）。</param>
    private static void SetLibraryPathSafe(string path, string source)
    {
        try
        {
            SharpSevenZipBase.SetLibraryPath(path);
            CoreLog.Info($"SevenZipEngine: 7z.dll path set ({source}): {path}");
        }
        catch (SharpSevenZipLibraryException ex)
        {
            CoreLog.Info($"SevenZipEngine: 7z.dll already in use, keep loaded library " +
                         $"(ignored {source} path '{path}'): {ex.Message}");
        }
        catch (Exception ex)
        {
            CoreLog.Info($"SevenZipEngine: failed to set 7z.dll path ({source} '{path}'): {ex.Message}");
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
                SetLibraryPathSafe(SevenZipDllPath, "configured");
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
                        SetLibraryPathSafe(userPath, "user callback");
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
    /// 求源文件未压缩字节总和（跳过目录）；单文件读取失败仅 Trace 并计 0，
    /// 不阻断压缩（字节进度是尽力而为）。
    /// </summary>
    private static long SumSourceFileSizes(IEnumerable<string> paths)
    {
        long total = 0;
        foreach (var path in paths)
        {
            try
            {
                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.Directory) != 0) continue;
                total += new FileInfo(path).Length;
            }
            catch (Exception ex)
            {
                CoreLog.Trace($"SumSourceFileSizes skip {path}: {ex.Message}");
            }
        }
        return total;
    }

    /// <summary>
    /// 将 SharpSevenZipCompressor 的进度事件桥接到 IProgress&lt;ArchiveProgress&gt;。
    /// 注意：SharpSevenZipCompressor 无公开取消 API，CancellationToken 在操作前后检查。
    /// </summary>
    /// <param name="percentBase">百分比映射基准（删除重打包段传 50，占整体后 50%）。</param>
    /// <param name="percentScale">百分比映射比例（删除重打包段传 0.5）。</param>
    /// <remarks>
    /// 事件回调内同步 Report（不使用 System.Progress 的异步派发），保证报告顺序与
    /// 字节单调性——异步派发会让低百分比回调晚于最终 100% 报告到达，造成 ProcessedBytes 回退。
    /// </remarks>
    private static void AttachCompressorProgress(
        SharpSevenZipCompressor compr,
        IProgress<ArchiveProgress>? progress,
        long totalSourceBytes,
        double percentBase = 0,
        double percentScale = 1)
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
            var mappedPercent = percentBase + accumulatedPercent * percentScale;
            progress.Report(new ArchiveProgress
            {
                CurrentFile = currentFile,
                PercentComplete = mappedPercent,
                FilePercentComplete = accumulatedPercent,
                TotalBytes = totalSourceBytes,
                ProcessedBytes = (long)(totalSourceBytes * mappedPercent / 100.0),
            });
        };
    }

    /// <summary>
    /// 逐条目状态（D2）：挂接 7z 压缩器逐文件事件，按源相对路径上报 Completed。
    /// FileCompressionStarted 仅携带叶文件名（同名文件按展开顺序 FIFO 消歧）；
    /// FileCompressionFinished 不带文件名 —— Started 时把解析出的条目键推入在途队列，Finished 出队上报。
    /// </summary>
    private static void AttachCompressorEntryTelemetry(
        SharpSevenZipCompressor compr,
        IProgress<ArchiveProgress>? progress,
        IReadOnlyList<string> compressedFiles,
        string[] sourcePaths)
    {
        if (progress == null)
            return;

        // 叶文件名 → 条目键 FIFO（同叶名多文件按展开顺序出队；目录不入队，压缩事件仅针对文件）
        var keyByLeaf = new Dictionary<string, Queue<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in compressedFiles)
        {
            if (!File.Exists(path))
                continue;
            var leaf = Path.GetFileName(path);
            if (string.IsNullOrEmpty(leaf))
                continue;
            if (!keyByLeaf.TryGetValue(leaf, out var queue))
                keyByLeaf[leaf] = queue = new Queue<string>();
            queue.Enqueue(BuildSourceRelativeKey(path, sourcePaths));
        }

        // 在途队列：Started 推入（未预测到的文件推 null 占位保持配对平衡），Finished 出队上报
        var inFlight = new Queue<string?>();
        var telemetryLock = new object();

        // 逐条目状态（D2）：Started 解析条目键并推入在途队列
        compr.FileCompressionStarted += (_, e) =>
        {
            string? resolved = null;
            var raw = e.FileName;
            var leaf = string.IsNullOrEmpty(raw) ? "" : Path.GetFileName(raw);
            lock (telemetryLock)
            {
                if (leaf.Length > 0 && keyByLeaf.TryGetValue(leaf, out var queue) && queue.Count > 0)
                    resolved = queue.Dequeue();
                inFlight.Enqueue(resolved);
            }
        };

        // 逐条目状态（D2）：Finished 出队 → 上报 Completed（锁外 Report，避免锁竞争）
        compr.FileCompressionFinished += (_, _) =>
        {
            string? finishedKey;
            lock (telemetryLock)
            {
                finishedKey = inFlight.Count > 0 ? inFlight.Dequeue() : null;
            }
            if (finishedKey == null)
                return;
            progress.Report(new ArchiveProgress
            {
                EntryKey = finishedKey,
                EntryStatus = ArchiveEntryStatus.Completed,
            });
        };
    }

    /// <summary>
    /// 按 FileScanner 约定计算源相对路径条目键（目录源 = 目录名\相对路径，散文件 = 文件名），
    /// 与 ZipEngine 的 relativePath 语义一致；不匹配任何源时回退完整路径（保持唯一，绝不按文件名截断）。
    /// </summary>
    private static string BuildSourceRelativeKey(string fullPath, string[] sourcePaths)
    {
        foreach (var source in sourcePaths)
        {
            if (Directory.Exists(source))
            {
                var relative = Path.GetRelativePath(source, fullPath);
                if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                    continue;
                return Path.Combine(Path.GetFileName(source), relative);
            }
            if (string.Equals(source, fullPath, StringComparison.OrdinalIgnoreCase))
                return Path.GetFileName(fullPath);
        }
        return fullPath;
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

        // 多线程压缩（mt=on）— 利用多核 CPU 并行压缩，提升压缩速度
        compr.CustomParameters["mt"] = options.SevenZipMultithreaded ? "on" : "off";

        // 字典大小（仅 LZMA/LZMA2 有效）— 走 CustomParameters["d"] 直传 7z.dll，
        // 与固实块 ["s"] 同路径；字符串格式支持 >2GB（LzmaDictionarySize 静态 int 属性上限约 2GB，已弃用）
        if (!string.IsNullOrEmpty(options.SevenZipDictionarySize))
            compr.CustomParameters["d"] = options.SevenZipDictionarySize;

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

    /// <summary>
    /// 分卷集的锚点路径（issue #70①）：7z.dll 只认第一卷 —— 中间卷没有完整头部与卷间链接，
    /// 传入 <c>.002</c> 之后的卷会直接打不开。非分卷路径原样返回。
    /// </summary>
    private static string AnchorVolumeSet(string archivePath)
        => ArchiveVolumeSet.FindFirstVolume(archivePath) ?? archivePath;


    #endregion

    #region ExtractAsync（SharpSevenZipExtractor）

    /// <summary>
    /// solid 批量解压总开关（回退用）：置 false 时整包退回逐条目路径，行为与改造前完全一致。
    /// 用 static readonly 而非 const —— const 会让「开关关闭」分支被编译器判为不可达（CS0162）。
    /// </summary>
    private static readonly bool EnableBulkSolidExtract = true;

    /// <summary>
    /// 批量段的大文件阈值：≥ 64MB 的条目走逐条目，保住文件内逐块进度与单条目失败粒度。
    /// 固定常量、不新增设置项（计划 §5.8）。
    /// </summary>
    private const long BigFileThresholdBytes = 64L * 1024 * 1024;

    /// <summary>
    /// 批量段暂存目录前缀。放在目标目录内 ⇒ 与最终落点同卷 ⇒ 改名桶的 File.Move 是 rename、不搬数据。
    /// </summary>
    private const string StagingDirPrefix = ".mantiszip-staging-";

    /// <summary>预解析阶段每检查这么多条目上报一次进度（两万条 stat 期间不能没有反馈）。</summary>
    private const int ConflictScanReportEvery = 200;

    /// <summary>
    /// 批量计划里的一条：归档条目 + 预解析算出的落点（<see cref="ResolvedPath"/> 为 null 表示只进跳过桶）。
    /// </summary>
    internal sealed class BulkEntryPlan
    {
        public BulkEntryPlan(ArchiveFileInfo entry, string fileName, string outputPath, string? resolvedPath, bool wasExisting)
        {
            Entry = entry;
            FileName = fileName;
            OutputPath = outputPath;
            ResolvedPath = resolvedPath;
            WasExisting = wasExisting;
        }

        /// <summary>解压前目标是否已存在（「覆盖」计数与状态上报要用，口径与逐条目路径一致）。</summary>
        public bool WasExisting { get; }

        public ArchiveFileInfo Entry { get; }
        public string FileName { get; }
        public string OutputPath { get; }

        /// <summary>冲突策略算出的最终落点（Rename 时与 <see cref="OutputPath"/> 不同）。</summary>
        public string? ResolvedPath { get; }
    }

    /// <summary>
    /// 冲突预解析的分桶结果（计划 §5.3）。各桶落地方式不同：
    /// <c>Direct</c> 直接进主批量；<c>Rename</c> 二次批量到暂存目录再逐个改名；
    /// <c>BigFile</c>/<c>Unsafe</c>/<c>Duplicate</c> 走逐条目；<c>Skipped</c> 不进索引只上报状态；
    /// <c>Directories</c> 只补建空目录（有文件的父目录由 7z.dll 自己建）。
    /// </summary>
    internal sealed class BulkExtractPlan
    {
        public List<BulkEntryPlan> Direct { get; } = new();
        public List<BulkEntryPlan> Rename { get; } = new();
        public List<BulkEntryPlan> BigFile { get; } = new();
        public List<BulkEntryPlan> Unsafe { get; } = new();
        public List<BulkEntryPlan> Duplicate { get; } = new();
        public List<ArchiveFileInfo> Skipped { get; } = new();
        public List<ArchiveFileInfo> Directories { get; } = new();

        /// <summary>预解析覆盖到的条目数（用于判断是否值得走批量）。</summary>
        public int Count => Direct.Count + Rename.Count + BigFile.Count + Unsafe.Count + Duplicate.Count + Skipped.Count;

        /// <summary>需要提交给 7z 批量接口的条目数。</summary>
        public int BulkCount => Direct.Count + Rename.Count;
    }

    public async Task<ExtractResult> ExtractAsync(
        string archivePath, string destinationPath,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default,
        ArchiveOptions? options = null)
    {
        CoreLog.Entry();
        archivePath = AnchorVolumeSet(archivePath);
        CoreLog.Info($"ExtractAsync: {archivePath} -> {destinationPath}, password={(password != null ? "***" : "null")}");
        var sw = Stopwatch.StartNew();

        // 阶段 1：读取归档头（7z 打开归档并载入条目表）
        progress.ReportPhase(ArchivePhase.ReadingArchive);

        EnsureLibraryPath();

        var result = await Task.Run(async () =>
        {
            using var extractor = SevenZipExtractorFactory.Create(archivePath, password);

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
            int handledFiles = 0;   // 已处理（成功+跳过）文件数，仅用于百分比推进；跳过不计入 ProcessedFiles（已处理显示）
            int failedEntries = 0;
            var conflictStats = new ConflictStatsCounter();
            var lastReportTime = DateTime.Now;
            var reportInterval = TimeSpan.FromMilliseconds(100);
            long totalBytes = allEntries.Where(e => !e.IsDirectory).Sum(e => (long)e.Size);
            long processedBytes = 0;

            // 批量段的文件内进度累积（Extracting 只给 PercentDelta，逐文件累加后归零）
            double bulkFileProgress = 0;

            // 主进度报告：跳过/失败条目也要刷新（连续跳过时窗口长时间无更新、看似卡死）。
            // 7z 百分比为文件计数基准 → 用 handledFiles 推进；跳过不计入 ProcessedFiles（已处理）。
            void ReportMainProgress(string fileName, int filePercent, long fileTotalBytes)
            {
                progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                {
                    CurrentFile = fileName,
                    PercentComplete = totalFiles > 0 ? (double)handledFiles / totalFiles * 100 : 0,
                    FilePercentComplete = filePercent,
                    TotalFiles = totalFiles,
                    ProcessedFiles = processedFiles,
                    TotalBytes = totalBytes,
                    ProcessedBytes = processedBytes,
                    FileTotalBytes = fileTotalBytes,
                }));
            }

            // ── 单条目路径：改造前的整段逻辑，逐条兜底与批量段的残留回退都复用它 ──
            async Task ExtractSingleEntryAsync(ArchiveFileInfo entry)
            {
                if (entry.IsDirectory)
                {
                    // 创建目录结构
                    var dirPath = FileConflictHelper.GetSafePath(destinationPath, entry.FileName);
                    if (!Directory.Exists(dirPath))
                        Directory.CreateDirectory(dirPath);
                    return;
                }

                string fileName = ArchivePath.Normalize(entry.FileName);
                var outputPath = FileConflictHelper.GetSafePath(destinationPath, fileName);
                var outDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                    Directory.CreateDirectory(outDir);

                var existedBefore = File.Exists(outputPath);
                var resolvedPath = await FileConflictHelper.ResolvePathAsync(outputPath, options, entry.LastWriteTime, (long)entry.Size);
                if (resolvedPath == null)
                {
                    // 跳过（跳过/覆盖旧/覆盖小）
                    conflictStats.RecordSkipped();
                    // 逐条目状态（D2）
                    progress?.Report(new ArchiveProgress { EntryKey = fileName, EntryStatus = ArchiveEntryStatus.Skipped });
                    processedBytes += (long)entry.Size;
                    handledFiles++;
                    // 主进度报告：跳过条目也刷新进度（连续大量跳过时窗口长时间无更新、看似卡死）。
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = fileName,
                        PercentComplete = totalFiles > 0 ? (double)handledFiles / totalFiles * 100 : 0,
                        FilePercentComplete = 100,
                        TotalFiles = totalFiles,
                        ProcessedFiles = processedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = processedBytes,
                        FileTotalBytes = (long)entry.Size,
                    }));
                    return;
                }
                if (existedBefore && resolvedPath == outputPath)
                {
                    conflictStats.RecordOverwritten();
                    // 逐条目状态（D2）
                    progress?.Report(new ArchiveProgress { EntryKey = fileName, EntryStatus = ArchiveEntryStatus.Overwritten });
                }

                var entrySize = (long)entry.Size;

                progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                {
                    CurrentFile = fileName,
                    PercentComplete = totalFiles > 0 ? (double)handledFiles / totalFiles * 100 : 0,
                    FilePercentComplete = 0,
                    TotalFiles = totalFiles,
                    ProcessedFiles = processedFiles,
                    TotalBytes = totalBytes,
                    ProcessedBytes = processedBytes,
                }));

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
                            ? (double)(handledFiles + (double)bytesWritten / entrySize) / totalFiles * 100
                            : 0;

                        progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                        {
                            CurrentFile = fileName,
                            PercentComplete = Math.Min(overallPct, 100),
                            FilePercentComplete = Math.Min(filePct, 100),
                            TotalFiles = totalFiles,
                            ProcessedFiles = processedFiles,
                            ProcessedBytes = processedBytes + Math.Min(bytesWritten, entrySize),
                        }));
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
                    handledFiles++;
                    processedBytes += entrySize;

                    // 逐条目状态（D2）：写盘成功即上报 Completed（Overwritten 已在覆盖判定处上报，不重复覆盖）
                    if (!(existedBefore && resolvedPath == outputPath))
                        progress?.Report(new ArchiveProgress { EntryKey = fileName, EntryStatus = ArchiveEntryStatus.Completed });

                    var now = DateTime.Now;
                    if (now - lastReportTime >= reportInterval || processedFiles == totalFiles)
                    {
                        progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                        {
                            CurrentFile = fileName,
                            PercentComplete = totalFiles > 0 ? (double)handledFiles / totalFiles * 100 : 100,
                            FilePercentComplete = 100,
                            TotalFiles = totalFiles,
                            ProcessedFiles = processedFiles,
                            TotalBytes = totalBytes,
                            ProcessedBytes = processedBytes,
                        }));
                        lastReportTime = now;
                    }
                }
                catch (UnauthorizedAccessException uax)
                {
                    CoreLog.Info($"ExtractAsync: permission denied for '{fileName}': {uax.Message}");
                    conflictStats.RecordFailed();
                    // 逐条目状态（D2）
                    progress?.Report(new ArchiveProgress { EntryKey = fileName, EntryStatus = ArchiveEntryStatus.Failed, EntryFailureReason = EntryFailureReason.PermissionDenied, FileTotalBytes = (long)entry.Size, EntryErrorDetail = uax.Message, EntryErrorHResult = uax.HResult });
                    failedEntries++;
                    handledFiles++;
                    processedBytes += entrySize;
                    // 主进度报告：失败条目也刷新进度；失败不计入 ProcessedFiles（已处理）。
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = fileName,
                        PercentComplete = totalFiles > 0 ? (double)handledFiles / totalFiles * 100 : 0,
                        FilePercentComplete = 100,
                        TotalFiles = totalFiles,
                        ProcessedFiles = processedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = processedBytes,
                        FileTotalBytes = (long)entry.Size,
                    }));
                }
                catch (IOException iox)
                {
                    // 目标文件被其他进程占用等 IO 失败：跳过该条目继续，避免单个文件中止整个解压
                    CoreLog.Info($"ExtractAsync: write failed for '{fileName}': {iox.Message}");
                    conflictStats.RecordFailed();
                    // 逐条目状态（D2）
                    progress?.Report(new ArchiveProgress { EntryKey = fileName, EntryStatus = ArchiveEntryStatus.Failed, EntryFailureReason = EntryFailureReason.IoFailed, FileTotalBytes = (long)entry.Size, EntryErrorDetail = iox.Message, EntryErrorHResult = iox.HResult });
                    failedEntries++;
                    handledFiles++;
                    processedBytes += entrySize;
                    // 主进度报告：失败条目也刷新进度；失败不计入 ProcessedFiles（已处理）。
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = fileName,
                        PercentComplete = totalFiles > 0 ? (double)handledFiles / totalFiles * 100 : 0,
                        FilePercentComplete = 100,
                        TotalFiles = totalFiles,
                        ProcessedFiles = processedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = processedBytes,
                        FileTotalBytes = (long)entry.Size,
                    }));
                }
            }

            async Task ExtractEntriesOneByOneAsync(IEnumerable<ArchiveFileInfo> entries)
            {
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ExtractSingleEntryAsync(entry);
                }
            }

            // 跳过桶：不进批量索引，但状态与进度必须照逐条目路径上报
            void ReportSkippedEntries(IReadOnlyList<ArchiveFileInfo> skipped)
            {
                foreach (var entry in skipped)
                {
                    var fileName = ArchivePath.Normalize(entry.FileName);
                    conflictStats.RecordSkipped();
                    progress?.Report(new ArchiveProgress { EntryKey = fileName, EntryStatus = ArchiveEntryStatus.Skipped });
                    processedBytes += (long)entry.Size;
                    handledFiles++;
                    ReportMainProgress(fileName, 100, (long)entry.Size);
                }
            }

            // ── 阶段 2a：冲突预解析（100% 前置，批量段不再做任何冲突判断）──
            async Task<BulkExtractPlan?> BuildBulkPlanAsync()
            {
                if (!EnableBulkSolidExtract)
                    return null;

                var resolvedFormat = SfxProbe.TryGetPayloadFormat(archivePath)
                    ?? ArchiveEngineFactory.GetFormatByExtension(archivePath);
                if (resolvedFormat != ArchiveFormat.SevenZip)
                    return null;

                // 加密包、非 solid 包、自定义 ErrorResolver 一律不碰（计划 §5.1/§5.10）
                if (hasEncrypted || !string.IsNullOrEmpty(password))
                    return null;
                if (options?.ErrorResolver != null)
                    return null;
                if (!extractor.IsSolid)
                    return null;

                var plan = new BulkExtractPlan();

                // Overwrite 且没有任何 resolver ⇒ 7z.dll 本身就是覆盖写，不需要为冲突多做一次 stat
                var statFree = options == null
                    || (options.ConflictAction == FileConflictAction.Overwrite
                        && options.ConflictResolver == null
                        && options.ConflictResolverAsync == null);

                var claimedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int fileCount = 0;

                progress.ReportPhase(ArchivePhase.CheckingConflicts);

                foreach (var entry in allEntries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (entry.IsDirectory)
                    {
                        plan.Directories.Add(entry);
                        continue;
                    }

                    fileCount++;

                    // 预解析进度：两万条 stat 期间界面必须有反馈（每 N 条一次，百分比按条目数）
                    if (fileCount % ConflictScanReportEvery == 0)
                    {
                        progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                        {
                            CurrentFile = ArchivePath.Normalize(entry.FileName),
                            PercentComplete = totalFiles > 0 ? (double)fileCount / totalFiles * 100 : 0,
                            FilePercentComplete = 100,
                            TotalFiles = totalFiles,
                            ProcessedFiles = processedFiles,
                            TotalBytes = totalBytes,
                            ProcessedBytes = processedBytes,
                        }));
                    }

                    var fileName = ArchivePath.Normalize(entry.FileName);
                    var outputPath = FileConflictHelper.GetSafePath(destinationPath, fileName);

                    // 路径安全：越界/绝对路径不进批量，退回逐条目（逐条目会走同一套 GetSafePath 与冲突判定）
                    if (!IsPathInsideDirectory(destinationPath, outputPath))
                    {
                        plan.Unsafe.Add(new BulkEntryPlan(entry, fileName, outputPath, null, wasExisting: false));
                        continue;
                    }

                    // 覆盖计数需要知道「解压前是否已存在」—— 这是唯一必须保留的存在性探测
                    var existedBefore = File.Exists(outputPath);

                    if (statFree)
                    {
                        // 覆盖语义：归档内重名条目按顺序覆盖，与逐条目路径一致，无需额外判定
                        if ((long)entry.Size >= BigFileThresholdBytes)
                            plan.BigFile.Add(new BulkEntryPlan(entry, fileName, outputPath, outputPath, existedBefore));
                        else
                            plan.Direct.Add(new BulkEntryPlan(entry, fileName, outputPath, outputPath, existedBefore));
                        continue;
                    }

                    var resolvedPath = await FileConflictHelper
                        .ResolvePathAsync(outputPath, options, entry.LastWriteTime, (long)entry.Size)
                        .ConfigureAwait(false);

                    if (resolvedPath == null)
                    {
                        plan.Skipped.Add(entry);
                        continue;
                    }

                    if (!claimedPaths.Add(resolvedPath))
                    {
                        // 归档内重名条目：第二个落点已被占用 ⇒ 交回逐条目路径（写盘时会重新判定冲突）
                        plan.Duplicate.Add(new BulkEntryPlan(entry, fileName, outputPath, resolvedPath, existedBefore));
                        continue;
                    }

                    if ((long)entry.Size >= BigFileThresholdBytes)
                        plan.BigFile.Add(new BulkEntryPlan(entry, fileName, outputPath, resolvedPath, existedBefore));
                    else if (!string.Equals(resolvedPath, outputPath, StringComparison.OrdinalIgnoreCase))
                        plan.Rename.Add(new BulkEntryPlan(entry, fileName, outputPath, resolvedPath, existedBefore));
                    else
                        plan.Direct.Add(new BulkEntryPlan(entry, fileName, outputPath, resolvedPath, existedBefore));
                }

                if (plan.BulkCount == 0)
                    return null;

                return plan;
            }

            // ── 阶段 2b/2c：批量段（solid 只解码一遍）──
            async Task RunBulkSegmentAsync(IReadOnlyList<BulkEntryPlan> segment, string targetRoot, bool staging)
            {
                var indexMap = new Dictionary<int, BulkEntryPlan>();
                foreach (var item in segment)
                    indexMap[item.Entry.Index] = item;

                var finished = new HashSet<int>();
                bulkFileProgress = 0;

                void OnStarted(object? sender, FileInfoEventArgs e)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        e.Cancel = true;
                        return;
                    }

                    bulkFileProgress = 0;
                    var name = indexMap.TryGetValue(e.FileInfo.Index, out var planned)
                        ? planned.FileName
                        : ArchivePath.Normalize(e.FileInfo.FileName ?? string.Empty);
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = name,
                        PercentComplete = totalFiles > 0 ? (double)handledFiles / totalFiles * 100 : 0,
                        FilePercentComplete = 0,
                        TotalFiles = totalFiles,
                        ProcessedFiles = processedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = processedBytes,
                    }));
                }

                void OnExtracting(object? sender, ProgressEventArgs e)
                {
                    bulkFileProgress = Math.Min(bulkFileProgress + e.PercentDelta, 100);
                    var overall = totalFiles > 0
                        ? (handledFiles + bulkFileProgress / 100) / totalFiles * 100
                        : 0;
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        PercentComplete = Math.Min(overall, 100),
                        FilePercentComplete = bulkFileProgress,
                        TotalFiles = totalFiles,
                        ProcessedFiles = processedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = processedBytes,
                    }));
                }

                void OnFinished(object? sender, FileInfoEventArgs e)
                {
                    if (!indexMap.TryGetValue(e.FileInfo.Index, out var planned))
                        return;

                    finished.Add(planned.Entry.Index);
                    bulkFileProgress = 0;

                    if (staging)
                    {
                        // 暂存段：计数与 Completed 都留到「改名成功」处，失败退回逐条目时不重复计数
                        return;
                    }

                    processedFiles++;
                    handledFiles++;
                    processedBytes += (long)planned.Entry.Size;

                    // 覆盖判定与逐条目路径一致：解压前已存在且落点未改名 ⇒ 上报 Overwritten（不重复报 Completed）
                    if (planned.WasExisting && string.Equals(planned.ResolvedPath, planned.OutputPath, StringComparison.OrdinalIgnoreCase))
                    {
                        conflictStats.RecordOverwritten();
                        progress?.Report(new ArchiveProgress { EntryKey = planned.FileName, EntryStatus = ArchiveEntryStatus.Overwritten });
                    }
                    else
                    {
                        progress?.Report(new ArchiveProgress { EntryKey = planned.FileName, EntryStatus = ArchiveEntryStatus.Completed });
                    }

                    ReportMainProgress(planned.FileName, 100, (long)planned.Entry.Size);
                    lastReportTime = DateTime.Now;
                }

                extractor.FileExtractionStarted += OnStarted;
                extractor.Extracting += OnExtracting;
                extractor.FileExtractionFinished += OnFinished;
                try
                {
                    CoreLog.Info($"ExtractAsync: bulk segment {(staging ? "rename" : "direct")} {segment.Count} entries -> {targetRoot}");
                    extractor.ExtractFiles(targetRoot, segment.Select(x => x.Entry.Index).ToArray());
                }
                catch (Exception ex)
                {
                    // 批量失败：未完成的条目交回逐条目（已落盘的保留、不重解）
                    CoreLog.Info($"ExtractAsync: bulk segment failed ({ex.GetType().Name}: {ex.Message}), falling back to per-entry");
                    foreach (var item in segment)
                    {
                        if (finished.Contains(item.Entry.Index))
                            continue;
                        cancellationToken.ThrowIfCancellationRequested();
                        await ExtractSingleEntryAsync(item.Entry);
                    }
                    return;
                }
                finally
                {
                    extractor.FileExtractionStarted -= OnStarted;
                    extractor.Extracting -= OnExtracting;
                    extractor.FileExtractionFinished -= OnFinished;
                }

                if (staging)
                    return;

                // 7z 静默跳过的条目（例如目标被占用）不会触发 Finished：按失败上报，口径与逐条目一致
                foreach (var item in segment)
                {
                    if (finished.Contains(item.Entry.Index))
                        continue;
                    CoreLog.Info($"ExtractAsync: bulk entry not finished: '{item.FileName}'");
                    conflictStats.RecordFailed();
                    progress?.Report(new ArchiveProgress { EntryKey = item.FileName, EntryStatus = ArchiveEntryStatus.Failed, EntryFailureReason = EntryFailureReason.IoFailed, FileTotalBytes = (long)item.Entry.Size });
                    failedEntries++;
                    handledFiles++;
                    processedBytes += (long)item.Entry.Size;
                    ReportMainProgress(item.FileName, 100, (long)item.Entry.Size);
                }
            }

            async Task RunBulkAsync(BulkExtractPlan plan)
            {
                CoreLog.Info($"ExtractAsync: bulk solid {plan.BulkCount} entries, per-entry {plan.BigFile.Count + plan.Unsafe.Count + plan.Duplicate.Count}, skipped {plan.Skipped.Count}");

                progress.ReportPhase(ArchivePhase.Extracting);

                // 跳过桶：状态与进度照常上报
                ReportSkippedEntries(plan.Skipped);

                // 空目录：有文件的父目录由 7z.dll 建，纯空目录单独补
                foreach (var dir in plan.Directories)
                {
                    var dirPath = FileConflictHelper.GetSafePath(destinationPath, ArchivePath.Normalize(dir.FileName));
                    if (!Directory.Exists(dirPath))
                        Directory.CreateDirectory(dirPath);
                }

                if (plan.Direct.Count > 0)
                    await RunBulkSegmentAsync(plan.Direct, destinationPath, staging: false);

                if (plan.Rename.Count > 0)
                {
                    var stagingDir = Path.Combine(destinationPath, StagingDirPrefix + Guid.NewGuid().ToString("N"));
                    CleanupStaleStagingDirectories(destinationPath);
                    Directory.CreateDirectory(stagingDir);
                    try
                    {
                        await RunBulkSegmentAsync(plan.Rename, stagingDir, staging: true);

                        foreach (var item in plan.Rename)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var stagedPath = Path.Combine(stagingDir, item.FileName);
                            if (!File.Exists(stagedPath))
                            {
                                // 暂存段没落地（7z 跳过或被占用）⇒ 退回逐条目，由它重新判定冲突并计数
                                await ExtractSingleEntryAsync(item.Entry);
                                continue;
                            }

                            var targetPath = item.ResolvedPath ?? item.OutputPath;
                            try
                            {
                                var targetDir = Path.GetDirectoryName(targetPath);
                                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                                    Directory.CreateDirectory(targetDir);

                                if (File.Exists(targetPath))
                                    File.Delete(targetPath);
                                File.Move(stagedPath, targetPath);

                                // 到这里才算真正落地：计数与状态在此上报（暂存段本身不计数）
                                processedFiles++;
                                handledFiles++;
                                processedBytes += (long)item.Entry.Size;
                                progress?.Report(new ArchiveProgress { EntryKey = item.FileName, EntryStatus = ArchiveEntryStatus.Completed });
                                ReportMainProgress(item.FileName, 100, (long)item.Entry.Size);
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                            {
                                // 撞占用/无权限：退回逐条目（它会重新选名并计数），暂存文件交给 finally 清掉
                                CoreLog.Info($"ExtractAsync: staging move failed for '{item.FileName}' ({ex.GetType().Name}), falling back to per-entry");
                                await ExtractSingleEntryAsync(item.Entry);
                            }
                        }
                    }
                    finally
                    {
                        DeleteDirectoryQuietly(stagingDir);
                    }
                }

                // 逐条目桶：大文件 / 不安全路径 / 归档内重名
                await ExtractEntriesOneByOneAsync(plan.BigFile.Concat(plan.Unsafe).Concat(plan.Duplicate).Select(x => x.Entry));
            }

            // ── 阶段 2：正式解出条目（solid 走批量、其余走改造前的逐条目路径）──
            var bulkPlan = await BuildBulkPlanAsync();
            if (bulkPlan == null)
                await ExtractEntriesOneByOneAsync(allEntries);
            else
                await RunBulkAsync(bulkPlan);

            progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100,
                TotalFiles = totalFiles,
                ProcessedFiles = processedFiles,
                TotalBytes = totalBytes,
                ProcessedBytes = totalBytes,
            }));

            // 阶段 3：收尾（汇总冲突统计）
            progress.ReportPhase(ArchivePhase.Finalizing);

            CoreLog.Info($"ExtractAsync: done, {sw.ElapsedMilliseconds}ms, failedEntries={failedEntries}");
            var extractStats = conflictStats.Snapshot;
            return new ExtractResult { SucceededEntries = processedFiles, FailedEntries = failedEntries, SkippedEntries = extractStats.Skipped, OverwrittenEntries = extractStats.Overwritten };
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
        return result;
    }

    /// <summary>
    /// 判断落点是否仍在目标目录内（批量段的安全闸门：越界条目退回逐条目，由它再走一遍 GetSafePath）。
    /// </summary>
    private static bool IsPathInsideDirectory(string directory, string candidate)
    {
        try
        {
            var root = Path.GetFullPath(directory);
            if (!root.EndsWith(Path.DirectorySeparatorChar))
                root += Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(candidate);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// 清扫历史遗留的暂存目录（上次取消/崩溃留下的 `.mantiszip-staging-*`）。
    /// </summary>
    private static void CleanupStaleStagingDirectories(string destinationPath)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(destinationPath, StagingDirPrefix + "*"))
            {
                DeleteDirectoryQuietly(dir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CoreLog.Info($"ExtractAsync: stale staging cleanup skipped: {ex.Message}");
        }
    }

    private static void DeleteDirectoryQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CoreLog.Info($"ExtractAsync: failed to delete staging dir '{path}': {ex.Message}");
        }
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

        progress.ReportPhase(ArchivePhase.Scanning);

        EnsureLibraryPath();

        await Task.Run(() =>
        {
            try
            {
                var compr = new SharpSevenZipCompressor();
                ConfigureCompressor(compr, options);

                // 预检：7z 原生压缩是单次调用，无法逐文件恢复。先展开待压缩文件集，
                // 统一做读权限预检（文件被占用/权限不足 → ErrorResolver 弹窗 重试/跳过/中止），
                // 跳过则从文件集中剔除，避免单个不可读文件导致整个压缩直接中止。
                // 剔除的同时经 CreateSkippedReporter 上报逐条目 Skipped + 原因（spec §5.3）。
                var files = ExpandSourcePaths(sourcePaths);
                if (options.FileWhitelist != null)
                    files = files.Where(f => options.FileWhitelist.Contains(f)).ToArray();
                var validated = ReadErrorHandler.FilterUnreadableFiles(files, options, cancellationToken,
                    ReadErrorHandler.CreateSkippedReporter(progress, files));

                long totalSourceBytes = SumSourceFileSizes(validated);
                AttachCompressorProgress(compr, progress, totalSourceBytes);

                // 阶段 2：正式压缩（7z 原生单次调用，进度来自压缩器事件）
                progress.ReportPhase(ArchivePhase.Compressing);

                // 单一目录·无白名单·且无文件被跳过 → 保留 CompressDirectory 的 PreserveDirectoryRoot 语义
                bool singleDirClean = sourcePaths.Length == 1
                    && Directory.Exists(sourcePaths[0])
                    && options.FileWhitelist == null
                    && validated.Count == files.Length;

                // 逐条目状态（D2）：按源相对路径预填条目键，压缩器逐文件完成事件上报 Completed
                AttachCompressorEntryTelemetry(compr, progress, validated, sourcePaths);

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
                    TotalBytes = totalSourceBytes,
                    ProcessedBytes = totalSourceBytes,
                });

                CoreLog.Info($"CompressAsync: done, {sw.ElapsedMilliseconds}ms");
                progress.ReportPhase(ArchivePhase.Finalizing);
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
        archivePath = AnchorVolumeSet(archivePath);
        CoreLog.Info($"ListEntriesAsync: {archivePath}");
        var sw = Stopwatch.StartNew();

        EnsureLibraryPath();

        var result = await Task.Run(() =>
        {
            using var extractor = SevenZipExtractorFactory.Create(archivePath, password);

            // 预先确定格式，供 lambda 中使用
            var format = ArchiveEngineFactory.GetFormatByExtension(archivePath);

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

                    // ISO 格式不支持加密，entry.Encrypted 可能误报（如加密分区），强制置 false
                    bool isEncrypted = isDir ? false : (entry.Encrypted && format != ArchiveFormat.Iso);

                    return new ArchiveItem
                    {
                        Name = fileName,
                        FullPath = isDir ? fileName.TrimEnd('/') : fileName,
                        Size = isDir ? 0 : (long)entry.Size,
                        CompressedSize = 0, // SharpSevenZip 不提供逐项压缩后大小
                        LastModified = entry.LastWriteTime,
                        IsDirectory = isDir,
                        IsEncrypted = isEncrypted,
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
        archivePath = AnchorVolumeSet(archivePath);
        CoreLog.Info($"TestArchiveAsync: {archivePath}");

        progress.ReportPhase(ArchivePhase.Testing);

        EnsureLibraryPath();

        var result = await Task.Run(() =>
        {
            try
            {
                using var extractor = SevenZipExtractorFactory.Create(archivePath, password);

                // 1. 校验压缩包完整性（7z.dll 的 Check = TestArchive 语义，逐条目解压并校验 CRC）。
                //    全程订阅 FileExtractionFinished 事件上报实时进度：
                //    Check() 阶段会为每个条目触发一次该事件并携带整体 PercentDone（实测确认），
                //    否则大包校验期间进度条完全不动（旧实现 0 进度事件直到循环结束）
                string currentFile = "";
                extractor.FileExtractionFinished += (_, e) =>
                {
                    currentFile = e.FileInfo.FileName ?? currentFile;
                    var pct = Math.Clamp((double)e.PercentDone, 0, 100);
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = currentFile,
                        PercentComplete = pct,
                        FilePercentComplete = pct,
                    });
                };

                bool valid;
                Exception? checkError = null;
                try
                {
                    valid = extractor.Check();
                }
                catch (Exception ex)
                {
                    valid = false;
                    checkError = ex;
                }

                var fileEntries = extractor.ArchiveFileData.Where(e => !e.IsDirectory).ToList();

                // 2. RAR5 块链结构校验（issue #69 在 RAR 上的残留）：7z.dll 的 Check 只校验数据区与块头，
                //    不校验卷尾 end-of-archive 块的头 CRC —— 末卷尾部被改坏时 Check 仍返回 true
                //    （WinRAR 真分卷实测），而 7-Zip / UnRAR 命令行都报「Headers Error」。
                //    这里按 RAR5 规范独立走一遍块链：头 CRC 不符 / 块越界 / 缺结束块 / 缺末卷都判无效。
                //    非 RAR5（含 RAR4 与头部加密）返回 Unknown，行为与之前保持一致。
                if (valid
                    && RarHeaderValidator.Validate(archivePath, out var structureReason) == RarStructureStatus.Broken)
                {
                    valid = false;
                    checkError = new InvalidDataException(
                        string.IsNullOrEmpty(structureReason)
                            ? "RAR 分卷结构损坏"
                            : $"RAR 分卷结构损坏：{structureReason}");
                    CoreLog.Error($"TestArchiveAsync: RAR 结构校验未通过 {archivePath}: {structureReason}");
                }

                if (valid)
                {
                    // 快路径：整包校验通过 → 逐条目上报 Completed（供进度窗口「列表」显示全部条目已校验）。
                    // 与 ZIP/TAR 一致；不走 Check 的 O(n²) 逐条目解压。
                    int done = 0;
                    foreach (var e in fileEntries)
                    {
                        done++;
                        var key = e.FileName ?? "";
                        progress?.Report(new ArchiveProgress
                        {
                            EntryKey = key,
                            EntryStatus = ArchiveEntryStatus.Completed,
                            CurrentFile = key,
                            FileTotalBytes = (long)e.Size,
                            PercentComplete = fileEntries.Count > 0 ? (double)done / fileEntries.Count * 100 : 100,
                            FilePercentComplete = 100,
                        });
                    }
                    CoreLog.Info($"TestArchiveAsync: passed, archive verified, {fileEntries.Count} entries");
                    return true;
                }

                // 失败路径：逐条目提取定位坏条目（spec §12 Follow-up #6）。
                // 仅在校验失败时执行，避免常态固实包 O(n²) 开销。
                int failedFiles = 0;
                int idx = 0;
                foreach (var e in fileEntries)
                {
                    idx++;
                    var key = e.FileName ?? "";
                    try
                    {
                        extractor.ExtractFile(e.Index, Stream.Null);
                        progress?.Report(new ArchiveProgress
                        {
                            EntryKey = key,
                            EntryStatus = ArchiveEntryStatus.Completed,
                            CurrentFile = key,
                            FileTotalBytes = (long)e.Size,
                            PercentComplete = fileEntries.Count > 0 ? (double)idx / fileEntries.Count * 100 : 100,
                            FilePercentComplete = 100,
                        });
                    }
                    catch (Exception ex)
                    {
                        failedFiles++;
                        CoreLog.Error($"TestArchiveAsync: entry '{key}' failed", ex);
                        progress?.Report(new ArchiveProgress
                        {
                            EntryKey = key,
                            EntryStatus = ArchiveEntryStatus.Failed,
                            EntryFailureReason = FailureReasonMapper.FromException(ex),
                            CurrentFile = key,
                            FileTotalBytes = (long)e.Size,
                            PercentComplete = fileEntries.Count > 0 ? (double)idx / fileEntries.Count * 100 : 100,
                            FilePercentComplete = 100,
                            EntryErrorDetail = ex.Message,
                            EntryErrorHResult = ex.HResult,
                        });
                    }
                }

                if (failedFiles == 0)
                {
                    // 逐条目提取均成功但 Check() 判定无效（如整包级异常）→ 上报一条整包失败（无 EntryKey）
                    var ce = checkError ?? new InvalidDataException("7z integrity check failed");
                    CoreLog.Error($"TestArchiveAsync: archive-level failure with no per-entry failure", ce);
                    progress?.Report(new ArchiveProgress
                    {
                        EntryStatus = ArchiveEntryStatus.Failed,
                        EntryFailureReason = FailureReasonMapper.FromException(ce),
                        CurrentFile = currentFile,
                        PercentComplete = 100,
                        EntryErrorDetail = ce.Message,
                        EntryErrorHResult = ce.HResult,
                    });
                }

                CoreLog.Info($"TestArchiveAsync: failed, {failedFiles} entries failed");
                return false;
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
        archivePath = AnchorVolumeSet(archivePath);
        CoreLog.Info($"ExtractEntriesAsync: {archivePath}, {entryKeys.Count} entries -> {destinationPath}");
        var sw = Stopwatch.StartNew();

        var keySet = new HashSet<string>(entryKeys.Select(k => ArchivePath.Normalize(k)), StringComparer.OrdinalIgnoreCase);

        EnsureLibraryPath();

        await Task.Run(async () =>
        {
            using var extractor = SevenZipExtractorFactory.Create(archivePath, password);

            var allEntries = extractor.ArchiveFileData.ToList();

            bool IsSelected(ArchiveFileInfo e) => keySet.Contains(ArchivePath.Normalize(e.FileName));

            var selectedFiles = allEntries.Where(e => !e.IsDirectory && IsSelected(e)).ToList();
            int totalTarget = selectedFiles.Count;
            int processed = 0;   // 真正落地的文件数（ProcessedFiles / SucceededEntries）
            int handled = 0;     // 已处理（成功 + 跳过 + 失败）文件数
            int failedEntries = 0;
            var conflictStats = new ConflictStatsCounter();
            var lastReportTime = DateTime.Now;
            var reportInterval = TimeSpan.FromMilliseconds(100);
            long totalBytes = selectedFiles.Sum(e => (long)e.Size);
            long processedBytes = 0;

            // 进度推进单位：跳过 / 失败 / 已解码待移动 / 已移动 / 逐条目各计 1。
            // 批量阶段「解码完但还没移动」的条目也要推进百分比，否则大批量解码期间窗口看起来卡住。
            int unitsDone = 0;

            // 落点：优先用调用方算好的 override（扁平化 / 保留完整路径 / 重命名的唯一口径）
            string ResolveTarget(ArchiveFileInfo entry, string fileName) =>
                outputPathOverrides?.GetValueOrDefault(fileName)
                ?? FileConflictHelper.GetSafePath(destinationPath, fileName);

            double CurrentPercent() => totalTarget > 0 ? Math.Min((double)unitsDone / totalTarget * 100, 100) : 100;

            void ReportMainProgress(string fileName, int filePercent, long fileTotalBytes)
            {
                progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                {
                    CurrentFile = fileName,
                    PercentComplete = CurrentPercent(),
                    FilePercentComplete = filePercent,
                    TotalFiles = totalTarget,
                    ProcessedFiles = processed,
                    TotalBytes = totalBytes,
                    ProcessedBytes = processedBytes,
                    FileTotalBytes = fileTotalBytes,
                }));
                lastReportTime = DateTime.Now;
            }

            void ReportEntryFailure(BulkEntryPlan plan, string detail, int hresult, EntryFailureReason reason)
            {
                conflictStats.RecordFailed();
                progress?.Report(new ArchiveProgress
                {
                    EntryKey = plan.FileName,
                    EntryStatus = ArchiveEntryStatus.Failed,
                    EntryFailureReason = reason,
                    FileTotalBytes = (long)plan.Entry.Size,
                    EntryErrorDetail = detail,
                    EntryErrorHResult = hresult,
                });
                failedEntries++;
                handled++;
                unitsDone++;
                processedBytes += (long)plan.Entry.Size;
                ReportMainProgress(plan.FileName, 100, (long)plan.Entry.Size);
            }

            void ReportSkippedEntries(IReadOnlyList<ArchiveFileInfo> skipped)
            {
                foreach (var entry in skipped)
                {
                    var fileName = ArchivePath.Normalize(entry.FileName);
                    progress?.Report(new ArchiveProgress { EntryKey = fileName, EntryStatus = ArchiveEntryStatus.Skipped });
                    ReportMainProgress(fileName, 100, (long)entry.Size);
                }
            }

            /// 逐条目路径（改造前的实现，原样保留）：单条选中、非 solid、加密、
            /// 有 ErrorResolver、大文件、越界路径、归档内重名都走这里。
            async Task ExtractSingleEntryAsync(ArchiveFileInfo entry)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fileName = ArchivePath.Normalize(entry.FileName);
                if (!keySet.Contains(fileName))
                    return;

                if (entry.IsDirectory)
                {
                    // 目录也走 override：扁平化 / 保留完整路径对选中的空目录同样生效（修既有不一致）
                    var dirPath = ResolveTarget(entry, fileName);
                    if (!Directory.Exists(dirPath))
                        Directory.CreateDirectory(dirPath);
                    return;
                }

                var outputPath = ResolveTarget(entry, fileName);
                var outDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                    Directory.CreateDirectory(outDir);

                var existedBefore = File.Exists(outputPath);
                var resolvedPath = await FileConflictHelper.ResolvePathAsync(outputPath, options, entry.LastWriteTime, (long)entry.Size);
                if (resolvedPath == null)
                {
                    conflictStats.RecordSkipped();
                    progress?.Report(new ArchiveProgress { EntryKey = fileName, EntryStatus = ArchiveEntryStatus.Skipped });
                    processedBytes += (long)entry.Size;
                    handled++;
                    unitsDone++;
                    ReportMainProgress(fileName, 100, (long)entry.Size);
                    return;
                }
                if (existedBefore && resolvedPath == outputPath)
                {
                    conflictStats.RecordOverwritten();
                    progress?.Report(new ArchiveProgress { EntryKey = fileName, EntryStatus = ArchiveEntryStatus.Overwritten });
                }

                var entrySize = (long)entry.Size;

                progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                {
                    CurrentFile = fileName,
                    PercentComplete = CurrentPercent(),
                    TotalFiles = totalTarget,
                    ProcessedFiles = processed,
                    TotalBytes = totalBytes,
                    ProcessedBytes = processedBytes,
                }));

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

                        progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                        {
                            CurrentFile = fileName,
                            PercentComplete = CurrentPercent(),
                            FilePercentComplete = Math.Min(filePct, 100),
                            TotalFiles = totalTarget,
                            ProcessedFiles = processed,
                            ProcessedBytes = processedBytes + Math.Min(bytesWritten, entrySize),
                        }));
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
                    handled++;
                    unitsDone++;
                    processedBytes += entrySize;

                    // 逐条目状态（D2）：写盘成功即上报 Completed（Overwritten 已在覆盖判定处上报，不重复覆盖）
                    if (!(existedBefore && resolvedPath == outputPath))
                        progress?.Report(new ArchiveProgress { EntryKey = fileName, EntryStatus = ArchiveEntryStatus.Completed });

                    ReportMainProgress(fileName, 100, entrySize);
                }
                catch (OperationCanceledException) { throw; }
                catch (UnauthorizedAccessException uax)
                {
                    CoreLog.Info($"ExtractEntriesAsync: permission denied for '{fileName}': {uax.Message}");
                    conflictStats.RecordFailed();
                    progress?.Report(new ArchiveProgress { EntryKey = fileName, EntryStatus = ArchiveEntryStatus.Failed, EntryFailureReason = EntryFailureReason.PermissionDenied, FileTotalBytes = (long)entry.Size, EntryErrorDetail = uax.Message, EntryErrorHResult = uax.HResult });
                    failedEntries++;
                    handled++;
                    unitsDone++;
                    processedBytes += entrySize;
                    // 主进度报告：失败条目也刷新进度（连续失败时窗口长时间无更新、看似卡死）；失败不计入 ProcessedFiles（已处理）。
                    ReportMainProgress(fileName, 100, entrySize);
                }
                catch (IOException iox)
                {
                    // 目标文件被其他进程占用等 IO 失败：跳过该条目继续，避免单个文件中止整个解压
                    CoreLog.Info($"ExtractEntriesAsync: write failed for '{fileName}': {iox.Message}");
                    conflictStats.RecordFailed();
                    progress?.Report(new ArchiveProgress { EntryKey = fileName, EntryStatus = ArchiveEntryStatus.Failed, EntryFailureReason = EntryFailureReason.IoFailed, FileTotalBytes = (long)entry.Size, EntryErrorDetail = iox.Message, EntryErrorHResult = iox.HResult });
                    failedEntries++;
                    handled++;
                    unitsDone++;
                    processedBytes += entrySize;
                    ReportMainProgress(fileName, 100, entrySize);
                }
            }

            async Task ExtractEntriesOneByOneAsync(IEnumerable<ArchiveFileInfo> entries)
            {
                foreach (var entry in entries)
                {
                    await ExtractSingleEntryAsync(entry);
                }
            }

            /// 选中条目的批量可行性闸门（计划 §4.1）：全部满足才批量。
            bool CanUseBulk() =>
                EnableBulkSolidExtract
                && extractor.IsSolid
                && password == null
                && options?.ErrorResolver == null
                && selectedFiles.Count >= 2                       // 单条：建暂存 + 移动纯亏
                && !selectedFiles.Any(e => e.Encrypted);          // 加密 solid 的批量重载行为未验证

            /// 冲突预解析（计划 §4.2 第 2 步）：落点已由 override 算好，这里只做「判冲突 + 分桶」。
            async Task<BulkExtractPlan> BuildBulkPlanAsync()
            {
                var plan = new BulkExtractPlan();
                var claimedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int scanned = 0;

                progress.ReportPhase(ArchivePhase.CheckingConflicts);

                foreach (var entry in allEntries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var fileName = ArchivePath.Normalize(entry.FileName);
                    if (!keySet.Contains(fileName))
                        continue;

                    if (entry.IsDirectory)
                    {
                        plan.Directories.Add(entry);
                        continue;
                    }

                    if (++scanned % ConflictScanReportEvery == 0)
                    {
                        progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                        {
                            CurrentFile = fileName,
                            PercentComplete = totalTarget > 0 ? Math.Min((double)scanned / totalTarget * 100, 100) : 0,
                            TotalFiles = totalTarget,
                            ProcessedFiles = processed,
                            TotalBytes = totalBytes,
                            ProcessedBytes = processedBytes,
                        }));
                    }

                    var targetPath = ResolveTarget(entry, fileName);

                    // 越界落点（override 指到目标目录外）与暂存布局不安全的条目一律逐条目
                    if (!IsPathInsideDirectory(destinationPath, targetPath)
                        || !IsPathInsideDirectory(destinationPath, Path.Combine(destinationPath, fileName)))
                    {
                        plan.Unsafe.Add(new BulkEntryPlan(entry, fileName, targetPath, targetPath, File.Exists(targetPath)));
                        continue;
                    }

                    var existedBefore = File.Exists(targetPath);
                    var resolvedPath = await FileConflictHelper.ResolvePathAsync(targetPath, options, entry.LastWriteTime, (long)entry.Size);
                    if (resolvedPath == null)
                    {
                        conflictStats.RecordSkipped();
                        plan.Skipped.Add(entry);
                        unitsDone++;
                        continue;
                    }

                    var planned = new BulkEntryPlan(entry, fileName, targetPath, resolvedPath, existedBefore);

                    // 归档内重名 / 多条落到同一路径：交给逐条目（它会重新判冲突并选名，语义与今天一致）
                    if (!claimedPaths.Add(resolvedPath))
                    {
                        plan.Duplicate.Add(planned);
                        continue;
                    }

                    // 大文件走逐条目：避免 staging + 最终路径同时占两份盘
                    if ((long)entry.Size >= BigFileThresholdBytes)
                    {
                        plan.BigFile.Add(planned);
                        continue;
                    }

                    plan.Direct.Add(planned);
                }

                return plan;
            }

            /// 暂存段：一次 ExtractFiles 把整批条目解码到 stagingRoot（保持归档内相对布局）。
            async Task RunBulkSegmentAsync(IReadOnlyList<BulkEntryPlan> segment, string stagingRoot)
            {
                var indexMap = segment.ToDictionary(x => x.Entry.Index, x => x);
                var finished = new HashSet<int>();
                double entryProgress = 0;

                void OnStarted(object? sender, FileInfoEventArgs e)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        e.Cancel = true;
                        return;
                    }
                    entryProgress = 0;
                    if (indexMap.TryGetValue(e.FileInfo.Index, out var planned))
                    {
                        progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                        {
                            CurrentFile = planned.FileName,
                            PercentComplete = CurrentPercent(),
                            FilePercentComplete = 0,
                            TotalFiles = totalTarget,
                            ProcessedFiles = processed,
                            TotalBytes = totalBytes,
                            ProcessedBytes = processedBytes,
                            FileTotalBytes = (long)planned.Entry.Size,
                        }));
                    }
                }

                void OnExtracting(object? sender, ProgressEventArgs e)
                {
                    entryProgress = Math.Min(entryProgress + e.PercentDelta, 100);
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        PercentComplete = CurrentPercent(),
                        FilePercentComplete = entryProgress,
                        TotalFiles = totalTarget,
                        ProcessedFiles = processed,
                        TotalBytes = totalBytes,
                        ProcessedBytes = processedBytes,
                    }));
                }

                void OnFinished(object? sender, FileInfoEventArgs e)
                {
                    // 暂存段不落地到最终路径：只计「已解码」，状态与计数留到移动成功之后上报
                    finished.Add(e.FileInfo.Index);
                    unitsDone++;
                    ReportMainProgress(e.FileInfo.FileName, 100, (long)e.FileInfo.Size);
                }

                extractor.FileExtractionStarted += OnStarted;
                extractor.Extracting += OnExtracting;
                extractor.FileExtractionFinished += OnFinished;
                try
                {
                    CoreLog.Info($"ExtractEntriesAsync: bulk segment {segment.Count} entries -> {stagingRoot}");
                    extractor.ExtractFiles(stagingRoot, segment.Select(x => x.Entry.Index).ToArray());
                }
                finally
                {
                    extractor.FileExtractionStarted -= OnStarted;
                    extractor.Extracting -= OnExtracting;
                    extractor.FileExtractionFinished -= OnFinished;
                }

                // 7z 静默跳过的条目（目标被占用等）不会触发 Finished：不在这里记失败，
                // 由调用方发现「暂存文件不存在」后退回逐条目，由逐条目给出准确原因。
                foreach (var item in segment)
                {
                    if (!finished.Contains(item.Entry.Index))
                        CoreLog.Info($"ExtractEntriesAsync: bulk entry not decoded: '{item.FileName}'");
                }
            }

            async Task RunSelectedBulkAsync(BulkExtractPlan plan)
            {
                CoreLog.Info($"ExtractEntriesAsync: bulk solid selected {plan.BulkCount} entries, per-entry {plan.BigFile.Count + plan.Unsafe.Count + plan.Duplicate.Count}, skipped {plan.Skipped.Count}");

                progress.ReportPhase(ArchivePhase.Extracting);

                // 选中空目录：按 override 落点补建（修「目录条目忽略 override」的既有缺陷）
                foreach (var dir in plan.Directories)
                {
                    var dirName = ArchivePath.Normalize(dir.FileName);
                    var dirPath = ResolveTarget(dir, dirName);
                    if (!Directory.Exists(dirPath))
                        Directory.CreateDirectory(dirPath);
                }

                ReportSkippedEntries(plan.Skipped);

                var stagingRoot = Path.Combine(destinationPath, StagingDirPrefix + Guid.NewGuid().ToString("N"));
                CleanupStaleStagingDirectories(destinationPath);

                var manualFallback = new List<BulkEntryPlan>();
                try
                {
                    Directory.CreateDirectory(stagingRoot);
                    await RunBulkSegmentAsync(plan.Direct, stagingRoot);

                    foreach (var item in plan.Direct)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var stagedPath = Path.Combine(stagingRoot, item.FileName);
                        if (!File.Exists(stagedPath))
                        {
                            // 没解码出来：退回逐条目（它会重新判冲突、给出失败原因）
                            manualFallback.Add(item);
                            continue;
                        }

                        var finalPath = item.ResolvedPath ?? item.OutputPath;
                        try
                        {
                            var finalDir = Path.GetDirectoryName(finalPath);
                            if (!string.IsNullOrEmpty(finalDir) && !Directory.Exists(finalDir))
                                Directory.CreateDirectory(finalDir);

                            File.Move(stagedPath, finalPath, overwrite: true);

                            try { File.SetLastWriteTime(finalPath, item.Entry.LastWriteTime); }
                            catch (Exception tsEx)
                            {
                                CoreLog.Info($"ExtractEntriesAsync: failed to set timestamp on {finalPath}: {tsEx.Message}");
                            }

                            processed++;
                            handled++;
                            processedBytes += (long)item.Entry.Size;

                            if (item.WasExisting && finalPath == item.OutputPath)
                            {
                                conflictStats.RecordOverwritten();
                                progress?.Report(new ArchiveProgress { EntryKey = item.FileName, EntryStatus = ArchiveEntryStatus.Overwritten });
                            }
                            else
                            {
                                progress?.Report(new ArchiveProgress { EntryKey = item.FileName, EntryStatus = ArchiveEntryStatus.Completed });
                            }

                            ReportMainProgress(item.FileName, 100, (long)item.Entry.Size);
                        }
                        catch (UnauthorizedAccessException uax)
                        {
                            CoreLog.Info($"ExtractEntriesAsync: staging move denied for '{item.FileName}': {uax.Message}");
                            ReportEntryFailure(item, uax.Message, uax.HResult, EntryFailureReason.PermissionDenied);
                        }
                        catch (IOException iox)
                        {
                            CoreLog.Info($"ExtractEntriesAsync: staging move failed for '{item.FileName}': {iox.Message}");
                            ReportEntryFailure(item, iox.Message, iox.HResult, EntryFailureReason.IoFailed);
                        }
                    }
                }
                finally
                {
                    DeleteDirectoryQuietly(stagingRoot);
                }

                // 逐条目桶：大文件 / 不安全路径 / 归档内重名 / 暂存没落地的
                foreach (var item in plan.BigFile.Concat(plan.Unsafe).Concat(plan.Duplicate).Concat(manualFallback))
                {
                    await ExtractSingleEntryAsync(item.Entry);
                }
            }

            // ── 分流：solid 且选中 ≥2 条时批量解码到暂存再逐个移动，其余走逐条目 ──
            if (!CanUseBulk())
            {
                await ExtractEntriesOneByOneAsync(allEntries);
            }
            else
            {
                var plan = await BuildBulkPlanAsync();
                if (plan.BulkCount < 2)
                {
                    // 能批量的不足两条（全被跳过 / 全是大文件）：直接退回逐条目，
                    // 由逐条目重新判定（这里不能再上报跳过的条目，否则同一条目会被上报两次）
                    progress.ReportPhase(ArchivePhase.Extracting);
                    await ExtractEntriesOneByOneAsync(allEntries);
                }
                else
                {
                    await RunSelectedBulkAsync(plan);
                }
            }

            progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100,
                TotalFiles = totalTarget,
                ProcessedFiles = processed,
                TotalBytes = totalBytes,
                ProcessedBytes = totalBytes,
            }));

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
        CancellationToken cancellationToken = default,
        ArchiveOptions? options = null)
    {
        CoreLog.Entry();
        archivePath = AnchorVolumeSet(archivePath);
        // 分卷集只能读：重建全部卷超出「增删条目」的范围（issue #70①），明确报错而不是写坏分卷。
        if (ArchiveVolumeSet.HasVolumeSuffix(archivePath))
            throw new NotSupportedException("分卷压缩包不支持增删文件，请解压后重新压缩");
        CoreLog.Info($"DeleteEntriesAsync: {archivePath}, entries=[{string.Join("; ", entryPaths)}]");
        var sw = Stopwatch.StartNew();

        EnsureLibraryPath();

        await Task.Run(() =>
        {
            if (entryPaths.Length == 0)
            {
                CoreLog.Info("DeleteEntriesAsync: no entries to delete");
                return;
            }

            // 1. 列出所有条目，排除要删除项
            var keepEntries = new List<(string path, bool isDir, long size)>();
            string? missingEntry = null;
            using (var extractor = string.IsNullOrEmpty(password)
                       ? new SharpSevenZipExtractor(archivePath)
                       : new SharpSevenZipExtractor(archivePath, password))
            {
                // 目录目标要连带其下所有后代，命中语义与 Zip 侧同源（ArchivePath.MatchesEntryOrDescendant）。
                var allNames = extractor.ArchiveFileData.Select(e => e.FileName).ToList();

                // 零命中必须报错，且要在建临时目录之前抛出：
                // 此前零命中会落到「解压保留项 + 原样重打包」分支，白跑一遍整包重写（耗时陡增、可能丢属性）。
                missingEntry = entryPaths.FirstOrDefault(
                    t => !allNames.Any(n => ArchivePath.MatchesEntryOrDescendant(n, t)));

                if (missingEntry == null)
                {
                    foreach (var entry in extractor.ArchiveFileData)
                    {
                        var normalized = ArchivePath.Normalize(entry.FileName);
                        if (!entryPaths.Any(t => ArchivePath.MatchesEntryOrDescendant(normalized, t)))
                        {
                            keepEntries.Add((normalized, entry.IsDirectory, entry.IsDirectory ? 0 : (long)entry.Size));
                        }
                    }
                }
            }

            if (missingEntry != null)
            {
                CoreLog.Error($"DeleteEntriesAsync: entry not found: {missingEntry}");
                throw new ArchiveEntryNotFoundException(missingEntry);
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
                long totalSourceBytes = keepEntries.Where(t => !t.isDir).Sum(t => t.size);

                using (var extractor = string.IsNullOrEmpty(password)
                           ? new SharpSevenZipExtractor(archivePath)
                           : new SharpSevenZipExtractor(archivePath, password))
                {
                    // 建立 fileName → ArchiveFileInfo 索引
                    var entryMap = extractor.ArchiveFileData
                        .ToDictionary(e => ArchivePath.Normalize(e.FileName), e => e, StringComparer.OrdinalIgnoreCase);

                    int total = keepEntries.Count;
                    int processed = 0;
                    foreach (var (path, isDir, _) in keepEntries)
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
                        var extractPct = pct * 0.5; // 提取阶段占 50%
                        progress?.Report(new ArchiveProgress
                        {
                            CurrentFile = path,
                            PercentComplete = extractPct,
                            TotalBytes = totalSourceBytes,
                            ProcessedBytes = (long)(totalSourceBytes * extractPct / 100.0),
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

                    AttachCompressorProgress(compr, progress, totalSourceBytes,
                        percentBase: 50, percentScale: 0.5); // 压缩阶段占后 50%

                    compr.PreserveDirectoryRoot = true;
                    compr.CompressDirectory(tempDir, tempArchive, password ?? "", "*", true);

                    // 4. 替换原归档
                    File.Delete(archivePath);
                    File.Move(tempArchive, archivePath);

                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = string.Empty,
                        PercentComplete = 100,
                        TotalBytes = totalSourceBytes,
                        ProcessedBytes = totalSourceBytes,
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
        archivePath = AnchorVolumeSet(archivePath);
        // 分卷集只能读：重建全部卷超出「增删条目」的范围（issue #70①），明确报错而不是写坏分卷。
        if (ArchiveVolumeSet.HasVolumeSuffix(archivePath))
            throw new NotSupportedException("分卷压缩包不支持增删文件，请解压后重新压缩");
        CoreLog.Info($"AddToArchiveAsync: {archivePath}, sources=[{string.Join("; ", sourcePaths)}]");
        var sw = Stopwatch.StartNew();

        EnsureLibraryPath();

        await Task.Run(async () =>
        {
            var compr = new SharpSevenZipCompressor();
            ConfigureCompressor(compr, options);
            compr.CompressionMode = CompressionMode.Append; // 追加到已有归档

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
            var deleteIndexes = new Dictionary<int, string?>();
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
                    deleteIndexes[existing.Index] = null; // 覆盖：ModifyArchive 传 null 值 = 删除该索引条目
                finalDict[finalName] = sourcePath;
            }

            if (finalDict.Count == 0)
            {
                CoreLog.Info("AddToArchiveAsync: all files skipped by conflict handling");
                return;
            }

            long totalSourceBytes = SumSourceFileSizes(finalDict.Keys);
            AttachCompressorProgress(compr, progress, totalSourceBytes);

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
                TotalBytes = totalSourceBytes,
                ProcessedBytes = totalSourceBytes,
            });

            CoreLog.Info($"AddToArchiveAsync: done, {sw.ElapsedMilliseconds}ms");
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }

    #endregion
}
