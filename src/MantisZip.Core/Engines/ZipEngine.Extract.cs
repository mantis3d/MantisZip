using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SharpSevenZip;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Models;
using MantisZip.Core.Utils;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Readers;
using SharpCompress.Writers.Zip;

namespace MantisZip.Core.Engines;

/// <summary>
/// ZipEngine 的 解压族部分（全量 + 过滤 + 并行，自 ZipEngine.cs 拆分，纯移动）。
/// </summary>
public partial class ZipEngine
{
    public async Task<ExtractResult> ExtractAsync(string archivePath, string destinationPath, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default, ArchiveOptions? options = null)
    {
        CoreLog.Entry();
        CoreLog.Info($"ExtractAsync: {archivePath} -> {destinationPath}, password={(password != null ? "***" : "null")}");

        // 读取并行度设置（0 = 默认 Environment.ProcessorCount，1 = 串行，>1 = 并行）
        int maxParallelism = options?.ParallelExtractDegree ?? 0;
        if (maxParallelism <= 0) maxParallelism = Environment.ProcessorCount;
        if (maxParallelism > 16) maxParallelism = 16;

        // 先快速检查文件数量决定是否走并行
        int fileCount;
        using (var quickArchive = OpenArchiveWithEncodingFallback(archivePath, password))
        {
            fileCount = quickArchive.Entries.Count(e => !e.IsDirectory);
        }

        // 文件数少于2或并行度为1时走串行
        if (fileCount < 2 || maxParallelism <= 1)
        {
            CoreLog.Info($"ExtractAsync: fileCount={fileCount}, parallelism={maxParallelism} -> using sequential mode");
            return await ExtractAsyncSequential(archivePath, destinationPath, password, progress, cancellationToken, options);
        }

        CoreLog.Info($"ExtractAsync: fileCount={fileCount}, parallelism={maxParallelism} -> using parallel mode");
        return await ExtractAsyncParallel(archivePath, destinationPath, password, progress, cancellationToken, options, maxParallelism);
    }

    /// <summary>
    /// 串行解压实现（原有逻辑，保留作为回退）
    /// </summary>
    private async Task<ExtractResult> ExtractAsyncSequential(string archivePath, string destinationPath, string? password, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken, ArchiveOptions? options)
    {
        CoreLog.Entry();
        CoreLog.Info($"ExtractAsyncSequential: {archivePath} -> {destinationPath}, password={(password != null ? "***" : "null")}");
        var sw = Stopwatch.StartNew();

        var result = await Task.Run(async () =>
        {
            using var archive = OpenArchiveWithEncodingFallback(archivePath, password);

            // 检查是否有加密条目但未提供密码
            var hasEncrypted = archive.Entries.Any(e => e.IsEncrypted);
            if (hasEncrypted && string.IsNullOrEmpty(password))
            {
                CoreLog.Info("ExtractAsync: archive has encrypted entries but no password provided");
                throw new InvalidOperationException("此压缩包已加密，请输入密码 (This archive is encrypted, password required)");
            }

            var allEntries = archive.Entries.ToList();
            var entries = allEntries.Where(e => !e.IsDirectory).ToList();
            var totalBytes = entries.Sum(e => e.Size);
            var processedBytes = 0L;
            var processedFiles = 0;
            int failedEntries = 0;
            var conflictStats = new ConflictStatsCounter();

            CoreLog.Info($"ExtractAsyncSequential: {entries.Count} entries, {totalBytes} total bytes");

            foreach (var entry in allEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entryKey = entry.Key ?? string.Empty;

                if (entry.IsDirectory)
                {
                    var dirPath = FileConflictHelper.GetSafePath(destinationPath, entryKey);
                    if (!Directory.Exists(dirPath))
                        Directory.CreateDirectory(dirPath);
                    continue;
                }

                var outputPath = FileConflictHelper.GetSafePath(destinationPath, entryKey);
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                var entryModified = entry.LastModifiedTime ?? DateTime.MinValue;
                var existedBefore = File.Exists(outputPath);
                var resolvedPath = await FileConflictHelper.ResolvePathAsync(outputPath, options, entryModified, entry.Size);
                if (resolvedPath == null)
                {
                    conflictStats.RecordSkipped();
                    // 逐条目状态（D2）：冲突跳过 → Skipped
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Skipped, FileTotalBytes = entry.Size });
                    processedBytes += entry.Size;
                    // 主进度报告：跳过条目也刷新进度（连续大量跳过时窗口长时间无更新、看似卡死）。
                    // 跳过不计入 ProcessedFiles（已处理），但计入字节进度，使总进度条推进。
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        PercentComplete = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : 0,
                        FilePercentComplete = 100,
                        TotalFiles = entries.Count,
                        ProcessedFiles = processedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = processedBytes,
                        FileTotalBytes = entry.Size,
                    }));
                    continue;
                }
                // 逐条目状态（D2）：本条目写入结果按是否覆盖上报 Completed/Overwritten
                var entryOverwritten = false;
                if (existedBefore && resolvedPath == outputPath)
                {
                    conflictStats.RecordOverwritten();
                    entryOverwritten = true;
                }

                var entrySize = entry.Size;

                try
                {
                    using (var outputStream = File.Create(resolvedPath))
                    {
                        // 官方 CRC 校验路径（见 CreateCrcExtractionOptions）：读完即比对 CRC，
                        // 损坏则抛 InvalidFormatException，由下方 catch 逐条目上报失败（不中止整批）。
                        entry.WriteTo(outputStream, CreateCrcExtractionOptions(), new InlineProgress<ProgressReport>(rep =>
                        {
                            var entryProcessed = rep.BytesTransferred;
                            var filePct = entrySize > 0 ? (double)entryProcessed / entrySize * 100 : 100;
                            var overallPct = totalBytes > 0 ? (double)(processedBytes + entryProcessed) / totalBytes * 100 : 0;
                            progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                            {
                                CurrentFile = entryKey,
                                TotalFiles = entries.Count,
                                ProcessedFiles = processedFiles,
                                TotalBytes = totalBytes,
                                ProcessedBytes = processedBytes + entryProcessed,
                                PercentComplete = overallPct,
                                FilePercentComplete = filePct,
                                FileTotalBytes = entrySize,
                                BatchProcessedBytes = processedBytes + entryProcessed,
                                BatchTotalBytes = totalBytes
                            }));
                        }));
                    }
                    // 恢复文件原始修改时间
                    try { File.SetLastWriteTime(resolvedPath, entryModified); } catch (Exception tsEx) { CoreLog.Info($"ExtractAsyncSequential: failed to set timestamp on {resolvedPath}: {tsEx.Message}"); }

                    processedBytes += entrySize;
                    processedFiles++;
                    // 逐条目状态（D2）：写入成功 → Completed 或 Overwritten
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = entryOverwritten ? ArchiveEntryStatus.Overwritten : ArchiveEntryStatus.Completed, FileTotalBytes = entrySize });
                }
                catch (InvalidFormatException ifx)
                {
                    // 官方 CRC 校验失败（数据损坏）→ 逐条目失败、不中止整批；删除已写出的损坏文件。
                    CoreLog.Info($"ExtractAsyncSequential: integrity check failed for '{entryKey}': {ifx.Message}");
                    TryDeleteCorruptOutput(resolvedPath);
                    failedEntries++;
                    conflictStats.RecordFailed();
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed, FileTotalBytes = entrySize, EntryFailureReason = FailureReasonMapper.FromException(ifx), EntryErrorDetail = ifx.Message, EntryErrorHResult = ifx.HResult });
                    processedBytes += entrySize;
                    // 主进度报告：失败条目也刷新进度（连续失败时窗口长时间无更新、看似卡死）；失败不计入 ProcessedFiles（已处理）。
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        PercentComplete = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : 0,
                        FilePercentComplete = 100,
                        TotalFiles = entries.Count,
                        ProcessedFiles = processedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = processedBytes,
                        FileTotalBytes = entrySize,
                    }));
                }
                catch (UnauthorizedAccessException uax)
                {
                    CoreLog.Info($"ExtractAsyncSequential: permission denied for '{entryKey}': {uax.Message}");
                    failedEntries++;
                    conflictStats.RecordFailed();
                    // 逐条目状态（D2）：权限失败 → Failed
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed, FileTotalBytes = entrySize, EntryFailureReason = EntryFailureReason.PermissionDenied, EntryErrorDetail = uax.Message, EntryErrorHResult = uax.HResult });
                    processedBytes += entrySize;
                    // 主进度报告：失败条目也刷新进度（连续失败时窗口长时间无更新、看似卡死）；失败不计入 ProcessedFiles（已处理）。
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        PercentComplete = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : 0,
                        FilePercentComplete = 100,
                        TotalFiles = entries.Count,
                        ProcessedFiles = processedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = processedBytes,
                        FileTotalBytes = entrySize,
                    }));
                }
                catch (IOException iox)
                {
                    // 目标文件被其他进程占用（如正被 Word 打开）等 IO 失败：
                    // 跳过该条目继续，避免单个文件导致整个解压中止（对齐 UnauthorizedAccessException 分支）
                    CoreLog.Info($"ExtractAsyncSequential: write failed for '{entryKey}': {iox.Message}");
                    failedEntries++;
                    conflictStats.RecordFailed();
                    // 逐条目状态（D2）：IO 写入失败 → Failed
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed, FileTotalBytes = entrySize, EntryFailureReason = EntryFailureReason.IoFailed, EntryErrorDetail = iox.Message, EntryErrorHResult = iox.HResult });
                    processedBytes += entrySize;
                    // 主进度报告：失败条目也刷新进度（连续失败时窗口长时间无更新、看似卡死）；失败不计入 ProcessedFiles（已处理）。
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        PercentComplete = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : 0,
                        FilePercentComplete = 100,
                        TotalFiles = entries.Count,
                        ProcessedFiles = processedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = processedBytes,
                        FileTotalBytes = entrySize,
                    }));
                }
            }

            progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100
            }));

            CoreLog.Info($"ExtractAsyncSequential: done, {processedFiles} files, {processedBytes} bytes, {sw.ElapsedMilliseconds}ms, failedEntries={failedEntries}");
            var seqStats = conflictStats.Snapshot;
            return new ExtractResult
            {
                SucceededEntries = processedFiles,
                FailedEntries = failedEntries,
                SkippedEntries = seqStats.Skipped,
                OverwrittenEntries = seqStats.Overwritten
            };
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
        return result;
    }

    /// <summary>
    /// 并行解压实现（批次复用 archive 实例，Round-Robin 分批，线程安全）。
    /// </summary>
    private async Task<ExtractResult> ExtractAsyncParallel(
        string archivePath,
        string destinationPath,
        string? password,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken,
        ArchiveOptions? options,
        int maxParallelism)
    {
        CoreLog.Entry();
        CoreLog.Info($"ExtractAsyncParallel: {archivePath} -> {destinationPath}, parallelism={maxParallelism}");
        var sw = Stopwatch.StartNew();
        var conflictStats = new ConflictStatsCounter();

        // 主进度单调包装：EntryStatus 状态报告直通；主进度报告在专用锁内「钳位 + 上报」原子化，
        // 保证 PercentComplete 单调不减（并行批次线程在锁外取共享计数快照后上报，快照顺序与
        // 上报顺序可能交错，导致消费端看到进度回退）。专用锁不与 syncLock（字节计数热路径）
        // 共用 —— 禁止在 syncLock 内 Report，避免历史 25x 锁竞争回退（见 AGENTS.md 并行解压架构）。
        progress = progress is null ? null : new MonotonicProgress(progress);

        // 1. 获取所有条目键和文件大小（单线程打开 archive 一次）
        var entryInfos = new List<(string Key, long Size, DateTime? Modified)>();
        long totalBytes = 0;

        using (var archive = OpenArchiveWithEncodingFallback(archivePath, password))
        {
            var hasEncrypted = archive.Entries.Any(e => e.IsEncrypted);
            if (hasEncrypted && string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException("此压缩包已加密，请输入密码 (This archive is encrypted, password required)");
            }

            var allEntries = archive.Entries.ToList();
            foreach (var entry in allEntries)
            {
                if (!entry.IsDirectory)
                {
                    var key = entry.Key ?? string.Empty;
                    var size = entry.Size;
                    var modified = entry.LastModifiedTime ?? DateTime.MinValue;
                    entryInfos.Add((key, size, modified));
                    totalBytes += size;
                }
            }
        }

        if (entryInfos.Count == 0)
        {
            progress?.Report(conflictStats.ApplyTo(new ArchiveProgress { PercentComplete = 100 }));
            return new ExtractResult { SucceededEntries = 0, FailedEntries = 0 };
        }

        // 2. 创建所有目标目录（单线程，避免竞态）
        var dirs = entryInfos
            .Select(info => Path.GetDirectoryName(FileConflictHelper.GetSafePath(destinationPath, info.Key)))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct();

        foreach (var dir in dirs)
        {
            Directory.CreateDirectory(dir!);
        }

        // 3. 按文件大小降序排列（大文件优先，避免长尾效应）
        var sortedInfos = entryInfos
            .OrderByDescending(info => info.Size)
            .ToList();

        // 4. Round-Robin 分批：将排序后的文件均匀分配到 N 个批次
        //    大文件自动分散到不同批次，实现天然负载均衡
        var batches = new List<(string Key, long Size, DateTime? Modified)>[maxParallelism];
        for (int i = 0; i < maxParallelism; i++)
            batches[i] = new List<(string Key, long Size, DateTime? Modified)>();

        for (int i = 0; i < sortedInfos.Count; i++)
        {
            batches[i % maxParallelism].Add(sortedInfos[i]);
        }

        // 过滤空批次（文件数少于并行度时），附 0-based 批次序号供进度上报
        var nonEmptyBatches = batches
            .Where(b => b.Count > 0)
            .Select((b, idx) => (Index: idx, Items: b))
            .ToList();
        int actualParallelism = nonEmptyBatches.Count;

        // 5. 并行解压：每批次一个线程，复用 1 个 archive 实例
        int processedFiles = 0;
        long processedBytes = 0;
        int failedEntries = 0;
        var syncLock = new object();
        var reportInterval = TimeSpan.FromMilliseconds(100);
        // 冲突弹窗信号量：Ask 弹窗回调（异步）不可并发，需全局串行（异步不能持 lock 跨 await）
        using var conflictGate = new SemaphoreSlim(1, 1);

        await Parallel.ForEachAsync(nonEmptyBatches, new ParallelOptions
        {
            MaxDegreeOfParallelism = actualParallelism,
            CancellationToken = cancellationToken
        }, async (batchInfo, ct) =>
        {
            var batchIndex = batchInfo.Index;
            var batch = batchInfo.Items;
            // 批内局部进度计数（批次内单线程顺序处理，无需加锁；成功/跳过/失败均递增以收敛至 100%）
            long batchTotalFiles = batch.Count;
            long batchTotalBytes = batch.Sum(x => x.Size);
            long batchProcessedFiles = 0;
            long batchProcessedBytes = 0;

            // ★ 每线程只打开 1 次 archive，处理整批文件
            using var archive = OpenArchiveWithEncodingFallback(archivePath, password);

            foreach (var (entryKey, entrySize, entryModified) in batch)
            {
                ct.ThrowIfCancellationRequested();

                var outputPath = FileConflictHelper.GetSafePath(destinationPath, entryKey);

                // 冲突处理：快速路径（文件不存在）不弹窗；Ask 异步弹窗经信号量全局串行（异步不能持 lock 跨 await）
                string? resolvedPath;
                var existedBefore = File.Exists(outputPath);
                if (!existedBefore)
                {
                    resolvedPath = outputPath;
                }
                else
                {
                    await conflictGate.WaitAsync(ct);
                    try
                    {
                        resolvedPath = await FileConflictHelper.ResolvePathAsync(
                            outputPath, options, entryModified, entrySize);
                    }
                    finally
                    {
                        conflictGate.Release();
                    }
                }

                if (resolvedPath == null)
                {
                    conflictStats.RecordSkipped();
                    // 逐条目状态（D2）：冲突跳过 → Skipped（锁外上报，避免锁竞争）
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Skipped, FileTotalBytes = entrySize });
                    long skipProcessedBytes;
                    int skipProcessedFiles;
                    lock (syncLock)
                    {
                        processedBytes += entrySize;
                        skipProcessedBytes = processedBytes;
                        skipProcessedFiles = processedFiles;
                    }
                    batchProcessedFiles++;
                    batchProcessedBytes += entrySize;
                    // 主进度报告：跳过条目也刷新进度（连续大量跳过时窗口长时间无更新、看似卡死）。
                    // 跳过不计入 ProcessedFiles（已处理），但计入字节进度，使总进度条与批次行推进。
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        TotalFiles = entryInfos.Count,
                        ProcessedFiles = skipProcessedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = skipProcessedBytes,
                        PercentComplete = totalBytes > 0 ? (double)skipProcessedBytes / totalBytes * 100 : 0,
                        FilePercentComplete = 100,
                        FileTotalBytes = entrySize,
                        BatchProcessedBytes = batchProcessedBytes,
                        BatchTotalBytes = batchTotalBytes,
                        BatchIndex = batchIndex,
                        BatchCount = actualParallelism,
                        BatchPercentComplete = batchTotalBytes > 0 ? (double)batchProcessedBytes / batchTotalBytes * 100 : 100,
                        BatchProcessedFiles = batchProcessedFiles,
                        BatchTotalFiles = batchTotalFiles
                    }));
                    continue;
                }
                // 逐条目状态（D2）：本条目写入结果按是否覆盖上报 Completed/Overwritten
                var entryOverwritten = false;
                if (existedBefore && resolvedPath == outputPath)
                {
                    conflictStats.RecordOverwritten();
                    entryOverwritten = true;
                }

                try
                {
                    var entry = archive.Entries.FirstOrDefault(e => e.Key == entryKey);
                    if (entry == null)
                    {
                        conflictStats.RecordFailed();
                        // 逐条目状态（D2）：条目丢失 → Failed（锁外上报，避免锁竞争）
                        progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed, FileTotalBytes = entrySize, EntryFailureReason = EntryFailureReason.Other }); // 条目丢失（entry == null），无异常对象，归 Other（spec §5.2）
                        lock (syncLock) Interlocked.Increment(ref failedEntries);
                        batchProcessedFiles++;
                        batchProcessedBytes += entrySize;
                        long failProcessedBytes;
                        int failProcessedFiles;
                        lock (syncLock)
                        {
                            processedBytes += entrySize;
                            failProcessedBytes = processedBytes;
                            failProcessedFiles = processedFiles;
                        }
                        // 主进度报告：失败条目也刷新进度（连续失败时窗口长时间无更新、看似卡死）；失败不计入 ProcessedFiles（已处理）。
                        progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                        {
                            CurrentFile = entryKey,
                            TotalFiles = entryInfos.Count,
                            ProcessedFiles = failProcessedFiles,
                            TotalBytes = totalBytes,
                            ProcessedBytes = failProcessedBytes,
                            PercentComplete = totalBytes > 0 ? (double)failProcessedBytes / totalBytes * 100 : 0,
                            FilePercentComplete = 100,
                            FileTotalBytes = entrySize,
                            BatchProcessedBytes = batchProcessedBytes,
                            BatchTotalBytes = batchTotalBytes,
                            BatchIndex = batchIndex,
                            BatchCount = actualParallelism,
                            BatchPercentComplete = batchTotalBytes > 0 ? (double)batchProcessedBytes / batchTotalBytes * 100 : 100,
                            BatchProcessedFiles = batchProcessedFiles,
                            BatchTotalFiles = batchTotalFiles
                        }));
                        continue;
                    }

                    using var outputStream = File.Create(resolvedPath);
                    // 官方 CRC 校验路径（见 CreateCrcExtractionOptions）：损坏则抛 InvalidFormatException，由下方 catch 逐条目上报。
                    await entry.WriteToAsync(outputStream, CreateCrcExtractionOptions(), new InlineProgress<ProgressReport>(rep =>
                    {
                        var entryProcessed = rep.BytesTransferred;
                        // 先在锁内拷贝共享变量，释放锁后再上报进度，避免锁竞争
                        int localProcessedFiles;
                        long localProcessedBytes;
                        lock (syncLock)
                        {
                            localProcessedFiles = processedFiles;
                            localProcessedBytes = processedBytes;
                        }

                        var filePct = entrySize > 0 ? (double)entryProcessed / entrySize * 100 : 100;
                        var overallPct = totalBytes > 0 ? (double)(localProcessedBytes + entryProcessed) / totalBytes * 100 : 0;
                        progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                        {
                            CurrentFile = entryKey,
                            TotalFiles = entryInfos.Count,
                            ProcessedFiles = localProcessedFiles,
                            TotalBytes = totalBytes,
                            ProcessedBytes = localProcessedBytes + entryProcessed,
                            PercentComplete = overallPct,
                            FilePercentComplete = filePct,
                            FileTotalBytes = entrySize,
                            BatchProcessedBytes = batchProcessedBytes + entryProcessed,
                            BatchTotalBytes = batchTotalBytes,
                            BatchIndex = batchIndex,
                            BatchCount = actualParallelism,
                            BatchPercentComplete = batchTotalBytes > 0 ? (double)(batchProcessedBytes + entryProcessed) / batchTotalBytes * 100 : 100,
                            BatchProcessedFiles = batchProcessedFiles,
                            BatchTotalFiles = batchTotalFiles
                        }));
                    }), ct);

                    // 恢复文件原始修改时间
                    try { File.SetLastWriteTime(resolvedPath, entryModified ?? DateTime.MinValue); }
                    catch (Exception tsEx) { CoreLog.Info($"ExtractAsyncParallel: failed to set timestamp on {resolvedPath}: {tsEx.Message}"); }

                    lock (syncLock)
                    {
                        processedBytes += entrySize;
                        Interlocked.Increment(ref processedFiles);
                    }
                    batchProcessedFiles++;
                    batchProcessedBytes += entrySize;
                    // 逐条目状态（D2）：写入成功 → Completed 或 Overwritten（锁外上报，避免锁竞争）
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = entryOverwritten ? ArchiveEntryStatus.Overwritten : ArchiveEntryStatus.Completed, FileTotalBytes = entrySize });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (InvalidFormatException ifx)
                {
                    // 官方 CRC 校验失败（数据损坏）→ 逐条目失败、不中止整批；删除已写出的损坏文件。
                    CoreLog.Info($"ExtractAsyncParallel: integrity check failed for '{entryKey}': {ifx.Message}");
                    TryDeleteCorruptOutput(resolvedPath);
                    conflictStats.RecordFailed();
                    lock (syncLock) Interlocked.Increment(ref failedEntries);
                    batchProcessedFiles++;
                    batchProcessedBytes += entrySize;
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed, FileTotalBytes = entrySize, EntryFailureReason = FailureReasonMapper.FromException(ifx), EntryErrorDetail = ifx.Message, EntryErrorHResult = ifx.HResult });
                    long ifxProcessedBytes;
                    int ifxProcessedFiles;
                    lock (syncLock)
                    {
                        processedBytes += entrySize;
                        ifxProcessedBytes = processedBytes;
                        ifxProcessedFiles = processedFiles;
                    }
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        TotalFiles = entryInfos.Count,
                        ProcessedFiles = ifxProcessedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = ifxProcessedBytes,
                        PercentComplete = totalBytes > 0 ? (double)ifxProcessedBytes / totalBytes * 100 : 0,
                        FilePercentComplete = 100,
                        FileTotalBytes = entrySize,
                        BatchProcessedBytes = batchProcessedBytes,
                        BatchTotalBytes = batchTotalBytes,
                        BatchIndex = batchIndex,
                        BatchCount = actualParallelism,
                        BatchPercentComplete = batchTotalBytes > 0 ? (double)batchProcessedBytes / batchTotalBytes * 100 : 100,
                        BatchProcessedFiles = batchProcessedFiles,
                        BatchTotalFiles = batchTotalFiles
                    }));
                }
                catch (UnauthorizedAccessException uax)
                {
                    CoreLog.Info($"ExtractAsyncParallel: permission denied for '{entryKey}': {uax.Message}");
                    conflictStats.RecordFailed();
                    lock (syncLock) Interlocked.Increment(ref failedEntries);
                    batchProcessedFiles++;
                    batchProcessedBytes += entrySize;
                    // 逐条目状态（D2）：权限失败 → Failed（锁外上报，避免锁竞争）
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed, FileTotalBytes = entrySize, EntryFailureReason = EntryFailureReason.PermissionDenied, EntryErrorDetail = uax.Message, EntryErrorHResult = uax.HResult });
                    long failProcessedBytes;
                    int failProcessedFiles;
                    lock (syncLock)
                    {
                        processedBytes += entrySize;
                        failProcessedBytes = processedBytes;
                        failProcessedFiles = processedFiles;
                    }
                    // 主进度报告：失败条目也刷新进度（连续失败时窗口长时间无更新、看似卡死）；失败不计入 ProcessedFiles（已处理）。
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        TotalFiles = entryInfos.Count,
                        ProcessedFiles = failProcessedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = failProcessedBytes,
                        PercentComplete = totalBytes > 0 ? (double)failProcessedBytes / totalBytes * 100 : 0,
                        FilePercentComplete = 100,
                        FileTotalBytes = entrySize,
                        BatchProcessedBytes = batchProcessedBytes,
                        BatchTotalBytes = batchTotalBytes,
                        BatchIndex = batchIndex,
                        BatchCount = actualParallelism,
                        BatchPercentComplete = batchTotalBytes > 0 ? (double)batchProcessedBytes / batchTotalBytes * 100 : 100,
                        BatchProcessedFiles = batchProcessedFiles,
                        BatchTotalFiles = batchTotalFiles
                    }));
                }
                catch (IOException iox)
                {
                    CoreLog.Info($"ExtractAsyncParallel: write failed for '{entryKey}': {iox.Message}");
                    conflictStats.RecordFailed();
                    lock (syncLock) Interlocked.Increment(ref failedEntries);
                    batchProcessedFiles++;
                    batchProcessedBytes += entrySize;
                    // 逐条目状态（D2）：IO 写入失败 → Failed（锁外上报，避免锁竞争）
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed, FileTotalBytes = entrySize, EntryFailureReason = EntryFailureReason.IoFailed, EntryErrorDetail = iox.Message, EntryErrorHResult = iox.HResult });
                    long failProcessedBytes;
                    int failProcessedFiles;
                    lock (syncLock)
                    {
                        processedBytes += entrySize;
                        failProcessedBytes = processedBytes;
                        failProcessedFiles = processedFiles;
                    }
                    // 主进度报告：失败条目也刷新进度（连续失败时窗口长时间无更新、看似卡死）；失败不计入 ProcessedFiles（已处理）。
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        TotalFiles = entryInfos.Count,
                        ProcessedFiles = failProcessedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = failProcessedBytes,
                        PercentComplete = totalBytes > 0 ? (double)failProcessedBytes / totalBytes * 100 : 0,
                        FilePercentComplete = 100,
                        FileTotalBytes = entrySize,
                        BatchProcessedBytes = batchProcessedBytes,
                        BatchTotalBytes = batchTotalBytes,
                        BatchIndex = batchIndex,
                        BatchCount = actualParallelism,
                        BatchPercentComplete = batchTotalBytes > 0 ? (double)batchProcessedBytes / batchTotalBytes * 100 : 100,
                        BatchProcessedFiles = batchProcessedFiles,
                        BatchTotalFiles = batchTotalFiles
                    }));
                }
                catch (Exception ex)
                {
                    CoreLog.Info($"ExtractAsyncParallel: unexpected error for '{entryKey}': {ex.Message}");
                    conflictStats.RecordFailed();
                    lock (syncLock) Interlocked.Increment(ref failedEntries);
                    batchProcessedFiles++;
                    batchProcessedBytes += entrySize;
                    // 逐条目状态（D2）：意外异常 → Failed（锁外上报，避免锁竞争）
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed, FileTotalBytes = entrySize, EntryFailureReason = EntryFailureReason.Other, EntryErrorDetail = ex.Message, EntryErrorHResult = ex.HResult });
                    long failProcessedBytes;
                    int failProcessedFiles;
                    lock (syncLock)
                    {
                        processedBytes += entrySize;
                        failProcessedBytes = processedBytes;
                        failProcessedFiles = processedFiles;
                    }
                    // 主进度报告：失败条目也刷新进度（连续失败时窗口长时间无更新、看似卡死）；失败不计入 ProcessedFiles（已处理）。
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        TotalFiles = entryInfos.Count,
                        ProcessedFiles = failProcessedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = failProcessedBytes,
                        PercentComplete = totalBytes > 0 ? (double)failProcessedBytes / totalBytes * 100 : 0,
                        FilePercentComplete = 100,
                        FileTotalBytes = entrySize,
                        BatchProcessedBytes = batchProcessedBytes,
                        BatchTotalBytes = batchTotalBytes,
                        BatchIndex = batchIndex,
                        BatchCount = actualParallelism,
                        BatchPercentComplete = batchTotalBytes > 0 ? (double)batchProcessedBytes / batchTotalBytes * 100 : 100,
                        BatchProcessedFiles = batchProcessedFiles,
                        BatchTotalFiles = batchTotalFiles
                    }));
                }
            }

            // 批次完成：上报批次 100%（即使本批无节流报告，也让批次行状态收敛）；
            // 同时携带全局进度快照，避免 UI 端计数器被空字段清零
            int batchDoneFiles;
            long batchDoneBytes;
            lock (syncLock)
            {
                batchDoneFiles = processedFiles;
                batchDoneBytes = processedBytes;
            }
            progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                TotalFiles = entryInfos.Count,
                ProcessedFiles = batchDoneFiles,
                TotalBytes = totalBytes,
                ProcessedBytes = batchDoneBytes,
                PercentComplete = totalBytes > 0 ? (double)batchDoneBytes / totalBytes * 100 : 100,
                FileTotalBytes = 0,
                BatchProcessedBytes = batchTotalBytes,
                BatchTotalBytes = batchTotalBytes,
                BatchIndex = batchIndex,
                BatchCount = actualParallelism,
                BatchPercentComplete = 100,
                BatchProcessedFiles = batchTotalFiles,
                BatchTotalFiles = batchTotalFiles
            }));
        });

        progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
        {
            CurrentFile = string.Empty,
            PercentComplete = 100
        }));

        CoreLog.Info($"ExtractAsyncParallel: done, {processedFiles} files, {processedBytes} bytes, {sw.ElapsedMilliseconds}ms, failedEntries={failedEntries}");
        var parStats = conflictStats.Snapshot;
        return new ExtractResult
        {
            SucceededEntries = processedFiles,
            FailedEntries = failedEntries,
            SkippedEntries = parStats.Skipped,
            OverwrittenEntries = parStats.Overwritten
        };
    }

    /// <summary>
    /// 按条目键解压指定文件。根据命中文件数与并行度设置自动选择串行/并行实现。
    /// </summary>
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
        CoreLog.Info($"ExtractEntriesAsync: {archivePath}, {entryKeys.Count} entries -> {destinationPath}, overrides={(outputPathOverrides?.Count ?? 0)}");

        // 读取并行度设置（0 = 默认 Environment.ProcessorCount，1 = 串行，>1 = 并行）
        int maxParallelism = options?.ParallelExtractDegree ?? 0;
        if (maxParallelism <= 0) maxParallelism = Environment.ProcessorCount;
        if (maxParallelism > 16) maxParallelism = 16;

        // 先快速检查命中文件数决定是否走并行（与 ExtractAsync 决策逻辑一致）
        int fileCount;
        using (var quickArchive = OpenArchiveWithEncodingFallback(archivePath, password))
        {
            fileCount = quickArchive.Entries.Count(e => !e.IsDirectory && entryKeys.Contains(ArchivePath.Normalize(e.Key)));
        }

        // 命中文件数少于2或并行度为1时走串行（拖拽/右键解压与全量解压共用同一决策）
        if (fileCount < 2 || maxParallelism <= 1)
        {
            CoreLog.Info($"ExtractEntriesAsync: fileCount={fileCount}, parallelism={maxParallelism} -> using sequential mode");
            await ExtractEntriesAsyncSequential(archivePath, entryKeys, destinationPath, password, progress, cancellationToken, options, outputPathOverrides);
        }
        else
        {
            CoreLog.Info($"ExtractEntriesAsync: fileCount={fileCount}, parallelism={maxParallelism} -> using parallel mode");
            await ExtractEntriesAsyncParallel(archivePath, entryKeys, destinationPath, password, progress, cancellationToken, options, outputPathOverrides, maxParallelism);
        }

        CoreLog.Exit();
    }

    /// <summary>
    /// 串行解压实现（原有逻辑，保留作为回退）。
    /// </summary>
    private async Task ExtractEntriesAsyncSequential(
        string archivePath,
        IReadOnlyList<string> entryKeys,
        string destinationPath,
        string? password,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken,
        ArchiveOptions? options,
        IReadOnlyDictionary<string, string>? outputPathOverrides)
    {
        CoreLog.Entry();
        CoreLog.Info($"ExtractEntriesAsyncSequential: {archivePath}, {entryKeys.Count} entries -> {destinationPath}");
        var sw = Stopwatch.StartNew();

        await Task.Run(async () =>
        {
            using var archive = OpenArchiveWithEncodingFallback(archivePath, password);
            var entries = archive.Entries.ToList();
            // entryKeys（来自预览树 FilteredEntryKeys）以 '/' 分隔；SharpCompress 在 Windows 下
            // 可能返回 '\' 分隔的 Key，统一归一化后再匹配（预览 = 实际 的保证）
            var totalBytes = entries.Where(e => entryKeys.Contains(ArchivePath.Normalize(e.Key))).Sum(e => e.Size);
            var processedBytes = 0L;
            var processedFiles = 0;
            var failedEntries = 0;
            var conflictStats = new ConflictStatsCounter();
            var filteredEntries = entries.Where(e => entryKeys.Contains(ArchivePath.Normalize(e.Key))).ToList();

            CoreLog.Info($"ExtractEntriesAsync: {filteredEntries.Count} matching entries");

            foreach (var entry in filteredEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entryKey = entry.Key ?? string.Empty;
                var normalizedKey = ArchivePath.Normalize(entryKey);

                if (entry.IsDirectory)
                {
                    var dirPath = FileConflictHelper.GetSafePath(destinationPath, normalizedKey);
                    if (!Directory.Exists(dirPath))
                        Directory.CreateDirectory(dirPath);
                    continue;
                }

                var outputPath = outputPathOverrides?.GetValueOrDefault(normalizedKey)
                    ?? FileConflictHelper.GetSafePath(destinationPath, normalizedKey);
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                var entryModified = entry.LastModifiedTime ?? DateTime.MinValue;
                var existedBefore = File.Exists(outputPath);
                var resolvedPath = await FileConflictHelper.ResolvePathAsync(outputPath, options, entryModified, entry.Size);
                if (resolvedPath == null)
                {
                    conflictStats.RecordSkipped();
                    // 逐条目状态（D2）：冲突跳过 → Skipped
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Skipped, FileTotalBytes = entry.Size });
                    processedBytes += entry.Size;
                    // 主进度报告：跳过条目也刷新进度（连续大量跳过时窗口长时间无更新、看似卡死）。
                    // 跳过不计入 ProcessedFiles（已处理），但计入字节进度，使总进度条推进。
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        PercentComplete = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : 0,
                        FilePercentComplete = 100,
                        TotalFiles = filteredEntries.Count,
                        ProcessedFiles = processedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = processedBytes,
                        FileTotalBytes = entry.Size,
                    }));
                    continue;
                }
                // 逐条目状态（D2）：本条目写入结果按是否覆盖上报 Completed/Overwritten
                var entryOverwritten = false;
                if (existedBefore && resolvedPath == outputPath)
                {
                    conflictStats.RecordOverwritten();
                    entryOverwritten = true;
                }

                var entrySize = entry.Size;
                try
                {
                    using (var outputStream = File.Create(resolvedPath))
                    {
                        // 官方 CRC 校验路径（见 CreateCrcExtractionOptions）：损坏则抛 InvalidFormatException，由下方 catch 逐条目上报。
                        entry.WriteTo(outputStream, CreateCrcExtractionOptions(), new InlineProgress<ProgressReport>(rep =>
                        {
                            var entryProcessed = rep.BytesTransferred;
                            var filePct = entrySize > 0 ? (double)entryProcessed / entrySize * 100 : 100;
                            var overallPct = totalBytes > 0 ? (double)(processedBytes + entryProcessed) / totalBytes * 100 : 0;
                            progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                            {
                                CurrentFile = entryKey,
                                TotalFiles = filteredEntries.Count,
                                ProcessedFiles = processedFiles,
                                TotalBytes = totalBytes,
                                ProcessedBytes = processedBytes + entryProcessed,
                                PercentComplete = overallPct,
                                FilePercentComplete = filePct,
                                FileTotalBytes = entrySize,
                                BatchProcessedBytes = processedBytes + entryProcessed,
                                BatchTotalBytes = totalBytes
                            }));
                        }));
                    }

                    try { File.SetLastWriteTime(resolvedPath, entryModified); } catch { CoreLog.Trace("ZipEngine.ExtractAsync: failed to set last write time for '{0}'", resolvedPath); }

                    processedBytes += entrySize;
                    processedFiles++;
                    // 逐条目状态（D2）：写入成功 → Completed 或 Overwritten
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = entryOverwritten ? ArchiveEntryStatus.Overwritten : ArchiveEntryStatus.Completed, FileTotalBytes = entrySize });
                }
                catch (OperationCanceledException) { throw; }
                catch (InvalidFormatException ifx)
                {
                    // 官方 CRC 校验失败（数据损坏）→ 逐条目失败、不中止整批；删除已写出的损坏文件。
                    CoreLog.Info($"ExtractEntriesAsyncSequential: integrity check failed for '{entryKey}': {ifx.Message}");
                    TryDeleteCorruptOutput(resolvedPath);
                    failedEntries++;
                    conflictStats.RecordFailed();
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed, FileTotalBytes = entrySize, EntryFailureReason = FailureReasonMapper.FromException(ifx), EntryErrorDetail = ifx.Message, EntryErrorHResult = ifx.HResult });
                    processedBytes += entrySize;
                    // 主进度报告：失败条目也刷新进度（连续失败时窗口长时间无更新、看似卡死）；失败不计入 ProcessedFiles（已处理）。
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        PercentComplete = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : 0,
                        FilePercentComplete = 100,
                        TotalFiles = filteredEntries.Count,
                        ProcessedFiles = processedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = processedBytes,
                        FileTotalBytes = entrySize,
                    }));
                }
                catch (UnauthorizedAccessException uax)
                {
                    CoreLog.Info($"ExtractEntriesAsyncSequential: permission denied for '{entryKey}': {uax.Message}");
                    failedEntries++;
                    conflictStats.RecordFailed();
                    // 逐条目状态（D2）：权限失败 → Failed
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed, FileTotalBytes = entrySize, EntryFailureReason = EntryFailureReason.PermissionDenied, EntryErrorDetail = uax.Message, EntryErrorHResult = uax.HResult });
                    processedBytes += entrySize;
                    // 主进度报告：失败条目也刷新进度（连续失败时窗口长时间无更新、看似卡死）；失败不计入 ProcessedFiles（已处理）。
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        PercentComplete = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : 0,
                        FilePercentComplete = 100,
                        TotalFiles = filteredEntries.Count,
                        ProcessedFiles = processedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = processedBytes,
                        FileTotalBytes = entrySize,
                    }));
                }
                catch (IOException iox)
                {
                    // 目标文件被其他进程占用等 IO 失败：跳过该条目继续，避免单个文件中止整个解压
                    CoreLog.Info($"ExtractEntriesAsyncSequential: write failed for '{entryKey}': {iox.Message}");
                    failedEntries++;
                    conflictStats.RecordFailed();
                    // 逐条目状态（D2）：IO 写入失败 → Failed
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed, FileTotalBytes = entrySize, EntryFailureReason = EntryFailureReason.IoFailed, EntryErrorDetail = iox.Message, EntryErrorHResult = iox.HResult });
                    processedBytes += entrySize;
                    // 主进度报告：失败条目也刷新进度（连续失败时窗口长时间无更新、看似卡死）；失败不计入 ProcessedFiles（已处理）。
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        PercentComplete = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : 0,
                        FilePercentComplete = 100,
                        TotalFiles = filteredEntries.Count,
                        ProcessedFiles = processedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = processedBytes,
                        FileTotalBytes = entrySize,
                    }));
                }
            }

            progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                PercentComplete = 100
            }));

            CoreLog.Info($"ExtractEntriesAsyncSequential: done, {processedFiles} files, failedEntries={failedEntries}, {sw.ElapsedMilliseconds}ms");
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }

    /// <summary>
    /// 并行解压实现（多实例并行 + 批次复用），处理 entryKeys 指定条目。
    /// 输出路径尊重 outputPathOverrides（与串行一致）；冲突处理经信号量全局串行。
    /// </summary>
    private async Task ExtractEntriesAsyncParallel(
        string archivePath,
        IReadOnlyList<string> entryKeys,
        string destinationPath,
        string? password,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken,
        ArchiveOptions? options,
        IReadOnlyDictionary<string, string>? outputPathOverrides,
        int maxParallelism)
    {
        CoreLog.Entry();
        CoreLog.Info($"ExtractEntriesAsyncParallel: {archivePath}, {entryKeys.Count} entries -> {destinationPath}, parallelism={maxParallelism}");
        var sw = Stopwatch.StartNew();
        var conflictStats = new ConflictStatsCounter();

        // 主进度单调包装：与 ExtractAsyncParallel 相同，根治并行批次快照乱序导致的进度回退
        progress = progress is null ? null : new MonotonicProgress(progress);

        // 1. 预读所有命中条目（单线程打开 archive 一次）：文件（Key/Size/Modified/输出路径）+ 目录 key
        var entryInfos = new List<(string Key, long Size, DateTime? Modified, string OutputPath)>();
        var directoryPaths = new List<string>();
        long totalBytes = 0;

        using (var archive = OpenArchiveWithEncodingFallback(archivePath, password))
        {
            var hasEncrypted = archive.Entries.Any(e => e.IsEncrypted);
            if (hasEncrypted && string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException("此压缩包已加密，请输入密码 (This archive is encrypted, password required)");
            }

            foreach (var entry in archive.Entries)
            {
                var entryKey = entry.Key ?? string.Empty;
                var normalizedKey = ArchivePath.Normalize(entryKey);
                if (!entryKeys.Contains(normalizedKey)) continue;

                if (entry.IsDirectory)
                {
                    directoryPaths.Add(FileConflictHelper.GetSafePath(destinationPath, normalizedKey));
                    continue;
                }

                var outputPath = outputPathOverrides?.GetValueOrDefault(normalizedKey)
                    ?? FileConflictHelper.GetSafePath(destinationPath, normalizedKey);
                entryInfos.Add((entryKey, entry.Size, entry.LastModifiedTime ?? DateTime.MinValue, outputPath));
                totalBytes += entry.Size;
            }
        }

        if (entryInfos.Count == 0)
        {
            progress?.Report(conflictStats.ApplyTo(new ArchiveProgress { PercentComplete = 100 }));
            CoreLog.Info("ExtractEntriesAsyncParallel: no matching entries, returning");
            CoreLog.Exit();
            return;
        }

        // 2. 创建所有目标目录（单线程，避免竞态）
        var dirs = entryInfos
            .Select(info => Path.GetDirectoryName(info.OutputPath))
            .Where(d => !string.IsNullOrEmpty(d))
            .Concat(directoryPaths)
            .Distinct();
        foreach (var dir in dirs)
        {
            Directory.CreateDirectory(dir!);
        }

        // 3. 按文件大小降序排列（大文件优先，避免长尾效应）
        var sortedInfos = entryInfos
            .OrderByDescending(info => info.Size)
            .ToList();

        // 4. Round-Robin 分批：将排序后的文件均匀分配到 N 个批次
        //    大文件自动分散到不同批次，实现天然负载均衡
        var batches = new List<(string Key, long Size, DateTime? Modified, string OutputPath)>[maxParallelism];
        for (int i = 0; i < maxParallelism; i++)
            batches[i] = new List<(string Key, long Size, DateTime? Modified, string OutputPath)>();

        for (int i = 0; i < sortedInfos.Count; i++)
        {
            batches[i % maxParallelism].Add(sortedInfos[i]);
        }

        // 过滤空批次（文件数少于并行度时），附 0-based 批次序号供进度上报
        var nonEmptyBatches = batches
            .Where(b => b.Count > 0)
            .Select((b, idx) => (Index: idx, Items: b))
            .ToList();
        int actualParallelism = nonEmptyBatches.Count;

        // 5. 并行解压：每批次一个线程，复用 1 个 archive 实例
        int processedFiles = 0;
        long processedBytes = 0;
        int failedEntries = 0;
        var syncLock = new object();
        // 冲突弹窗信号量：Ask 弹窗回调（异步）不可并发，需全局串行（异步不能持 lock 跨 await）
        using var conflictGate = new SemaphoreSlim(1, 1);

        await Parallel.ForEachAsync(nonEmptyBatches, new ParallelOptions
        {
            MaxDegreeOfParallelism = actualParallelism,
            CancellationToken = cancellationToken
        }, async (batchInfo, ct) =>
        {
            var batchIndex = batchInfo.Index;
            var batch = batchInfo.Items;
            // 批局部计数（批内单线程顺序处理，无需加锁）；成功/跳过/失败均递增收敛至 100%
            long batchTotalFiles = batch.Count;
            long batchTotalBytes = batch.Sum(x => x.Size);
            long batchProcessedFiles = 0;
            long batchProcessedBytes = 0;

            // ★ 每线程只打开 1 次 archive，处理整批文件
            using var archive = OpenArchiveWithEncodingFallback(archivePath, password);

            foreach (var (entryKey, entrySize, entryModified, outputPath) in batch)
            {
                ct.ThrowIfCancellationRequested();

                // 冲突处理：快速路径（文件不存在）不弹窗；Ask 异步弹窗经信号量全局串行
                string? resolvedPath;
                var existedBefore = File.Exists(outputPath);
                if (!existedBefore)
                {
                    resolvedPath = outputPath;
                }
                else
                {
                    await conflictGate.WaitAsync(ct);
                    try
                    {
                        resolvedPath = await FileConflictHelper.ResolvePathAsync(
                            outputPath, options, entryModified, entrySize);
                    }
                    finally
                    {
                        conflictGate.Release();
                    }
                }

                if (resolvedPath == null)
                {
                    conflictStats.RecordSkipped();
                    // 逐条目状态（D2）：冲突跳过 → Skipped（锁外上报，避免锁竞争）
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Skipped, FileTotalBytes = entrySize });
                    long skipProcessedBytes;
                    int skipProcessedFiles;
                    lock (syncLock)
                    {
                        processedBytes += entrySize;
                        skipProcessedBytes = processedBytes;
                        skipProcessedFiles = processedFiles;
                    }
                    batchProcessedFiles++;
                    batchProcessedBytes += entrySize;
                    // 主进度报告：跳过条目也刷新进度（连续大量跳过时窗口长时间无更新、看似卡死）。
                    // 跳过不计入 ProcessedFiles（已处理），但计入字节进度，使总进度条与批次行推进。
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        TotalFiles = entryInfos.Count,
                        ProcessedFiles = skipProcessedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = skipProcessedBytes,
                        PercentComplete = totalBytes > 0 ? (double)skipProcessedBytes / totalBytes * 100 : 0,
                        FilePercentComplete = 100,
                        FileTotalBytes = entrySize,
                        BatchProcessedBytes = batchProcessedBytes,
                        BatchTotalBytes = batchTotalBytes,
                        BatchIndex = batchIndex,
                        BatchCount = actualParallelism,
                        BatchPercentComplete = batchTotalBytes > 0 ? (double)batchProcessedBytes / batchTotalBytes * 100 : 100,
                        BatchProcessedFiles = batchProcessedFiles,
                        BatchTotalFiles = batchTotalFiles
                    }));
                    continue;
                }

                // 逐条目状态（D2）：本条目写入结果按是否覆盖上报 Completed/Overwritten
                var entryOverwritten = false;
                if (existedBefore && resolvedPath == outputPath)
                {
                    conflictStats.RecordOverwritten();
                    entryOverwritten = true;
                }

                try
                {
                    var entry = archive.Entries.FirstOrDefault(e => e.Key == entryKey);
                    if (entry == null)
                    {
                        conflictStats.RecordFailed();
                        // 逐条目状态（D2）：条目丢失 → Failed（锁外上报，避免锁竞争）
                        progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed, FileTotalBytes = entrySize, EntryFailureReason = EntryFailureReason.Other }); // 条目丢失（entry == null），无异常对象，归 Other（spec §5.2）
                        lock (syncLock) Interlocked.Increment(ref failedEntries);
                        batchProcessedFiles++;
                        batchProcessedBytes += entrySize;
                        long failProcessedBytes;
                        int failProcessedFiles;
                        lock (syncLock)
                        {
                            processedBytes += entrySize;
                            failProcessedBytes = processedBytes;
                            failProcessedFiles = processedFiles;
                        }
                        // 主进度报告：失败条目也刷新进度（连续失败时窗口长时间无更新、看似卡死）；失败不计入 ProcessedFiles（已处理）。
                        progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                        {
                            CurrentFile = entryKey,
                            TotalFiles = entryInfos.Count,
                            ProcessedFiles = failProcessedFiles,
                            TotalBytes = totalBytes,
                            ProcessedBytes = failProcessedBytes,
                            PercentComplete = totalBytes > 0 ? (double)failProcessedBytes / totalBytes * 100 : 0,
                            FilePercentComplete = 100,
                            FileTotalBytes = entrySize,
                            BatchProcessedBytes = batchProcessedBytes,
                            BatchTotalBytes = batchTotalBytes,
                            BatchIndex = batchIndex,
                            BatchCount = actualParallelism,
                            BatchPercentComplete = batchTotalBytes > 0 ? (double)batchProcessedBytes / batchTotalBytes * 100 : 100,
                            BatchProcessedFiles = batchProcessedFiles,
                            BatchTotalFiles = batchTotalFiles
                        }));
                        continue;
                    }

                    using var outputStream = File.Create(resolvedPath);
                    // 官方 CRC 校验路径（见 CreateCrcExtractionOptions）：损坏则抛 InvalidFormatException，由下方 catch 逐条目上报。
                    await entry.WriteToAsync(outputStream, CreateCrcExtractionOptions(), new InlineProgress<ProgressReport>(rep =>
                    {
                        var entryProcessed = rep.BytesTransferred;
                        // 先在锁内拷贝共享变量，释放锁后再上报进度，避免锁竞争
                        int localProcessedFiles;
                        long localProcessedBytes;
                        lock (syncLock)
                        {
                            localProcessedFiles = processedFiles;
                            localProcessedBytes = processedBytes;
                        }

                        var filePct = entrySize > 0 ? (double)entryProcessed / entrySize * 100 : 100;
                        var overallPct = totalBytes > 0 ? (double)(localProcessedBytes + entryProcessed) / totalBytes * 100 : 0;
                        progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                        {
                            CurrentFile = entryKey,
                            TotalFiles = entryInfos.Count,
                            ProcessedFiles = localProcessedFiles,
                            TotalBytes = totalBytes,
                            ProcessedBytes = localProcessedBytes + entryProcessed,
                            PercentComplete = overallPct,
                            FilePercentComplete = filePct,
                            FileTotalBytes = entrySize,
                            BatchProcessedBytes = batchProcessedBytes + entryProcessed,
                            BatchTotalBytes = batchTotalBytes,
                            BatchIndex = batchIndex,
                            BatchCount = actualParallelism,
                            BatchPercentComplete = batchTotalBytes > 0 ? (double)(batchProcessedBytes + entryProcessed) / batchTotalBytes * 100 : 100,
                            BatchProcessedFiles = batchProcessedFiles,
                            BatchTotalFiles = batchTotalFiles
                        }));
                    }), ct);

                    // 恢复文件原始修改时间
                    try { File.SetLastWriteTime(resolvedPath, entryModified ?? DateTime.MinValue); }
                    catch (Exception tsEx) { CoreLog.Info($"ExtractEntriesAsyncParallel: failed to set timestamp on {resolvedPath}: {tsEx.Message}"); }

                    lock (syncLock)
                    {
                        processedBytes += entrySize;
                        Interlocked.Increment(ref processedFiles);
                    }
                    batchProcessedFiles++;
                    batchProcessedBytes += entrySize;
                    // 逐条目状态（D2）：写入成功 → Completed 或 Overwritten（锁外上报，避免锁竞争）
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = entryOverwritten ? ArchiveEntryStatus.Overwritten : ArchiveEntryStatus.Completed, FileTotalBytes = entrySize });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (InvalidFormatException ifx)
                {
                    // 官方 CRC 校验失败（数据损坏）→ 逐条目失败、不中止整批；删除已写出的损坏文件。
                    CoreLog.Info($"ExtractEntriesAsyncParallel: integrity check failed for '{entryKey}': {ifx.Message}");
                    TryDeleteCorruptOutput(resolvedPath);
                    conflictStats.RecordFailed();
                    lock (syncLock) Interlocked.Increment(ref failedEntries);
                    batchProcessedFiles++;
                    batchProcessedBytes += entrySize;
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed, FileTotalBytes = entrySize, EntryFailureReason = FailureReasonMapper.FromException(ifx), EntryErrorDetail = ifx.Message, EntryErrorHResult = ifx.HResult });
                    long ifxProcessedBytes;
                    int ifxProcessedFiles;
                    lock (syncLock)
                    {
                        processedBytes += entrySize;
                        ifxProcessedBytes = processedBytes;
                        ifxProcessedFiles = processedFiles;
                    }
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        TotalFiles = entryInfos.Count,
                        ProcessedFiles = ifxProcessedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = ifxProcessedBytes,
                        PercentComplete = totalBytes > 0 ? (double)ifxProcessedBytes / totalBytes * 100 : 0,
                        FilePercentComplete = 100,
                        FileTotalBytes = entrySize,
                        BatchProcessedBytes = batchProcessedBytes,
                        BatchTotalBytes = batchTotalBytes,
                        BatchIndex = batchIndex,
                        BatchCount = actualParallelism,
                        BatchPercentComplete = batchTotalBytes > 0 ? (double)batchProcessedBytes / batchTotalBytes * 100 : 100,
                        BatchProcessedFiles = batchProcessedFiles,
                        BatchTotalFiles = batchTotalFiles
                    }));
                }
                catch (UnauthorizedAccessException uax)
                {
                    CoreLog.Info($"ExtractEntriesAsyncParallel: permission denied for '{entryKey}': {uax.Message}");
                    conflictStats.RecordFailed();
                    lock (syncLock) Interlocked.Increment(ref failedEntries);
                    batchProcessedFiles++;
                    batchProcessedBytes += entrySize;
                    // 逐条目状态（D2）：权限失败 → Failed（锁外上报，避免锁竞争）
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed, FileTotalBytes = entrySize, EntryFailureReason = EntryFailureReason.PermissionDenied, EntryErrorDetail = uax.Message, EntryErrorHResult = uax.HResult });
                    long failProcessedBytes;
                    int failProcessedFiles;
                    lock (syncLock)
                    {
                        processedBytes += entrySize;
                        failProcessedBytes = processedBytes;
                        failProcessedFiles = processedFiles;
                    }
                    // 主进度报告：失败条目也刷新进度（连续失败时窗口长时间无更新、看似卡死）；失败不计入 ProcessedFiles（已处理）。
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        TotalFiles = entryInfos.Count,
                        ProcessedFiles = failProcessedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = failProcessedBytes,
                        PercentComplete = totalBytes > 0 ? (double)failProcessedBytes / totalBytes * 100 : 0,
                        FilePercentComplete = 100,
                        FileTotalBytes = entrySize,
                        BatchProcessedBytes = batchProcessedBytes,
                        BatchTotalBytes = batchTotalBytes,
                        BatchIndex = batchIndex,
                        BatchCount = actualParallelism,
                        BatchPercentComplete = batchTotalBytes > 0 ? (double)batchProcessedBytes / batchTotalBytes * 100 : 100,
                        BatchProcessedFiles = batchProcessedFiles,
                        BatchTotalFiles = batchTotalFiles
                    }));
                }
                catch (IOException iox)
                {
                    // 目标文件被其他进程占用等 IO 失败：跳过该条目继续，避免单个文件中止整个解压
                    CoreLog.Info($"ExtractEntriesAsyncParallel: write failed for '{entryKey}': {iox.Message}");
                    conflictStats.RecordFailed();
                    lock (syncLock) Interlocked.Increment(ref failedEntries);
                    batchProcessedFiles++;
                    batchProcessedBytes += entrySize;
                    // 逐条目状态（D2）：IO 写入失败 → Failed（锁外上报，避免锁竞争）
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed, FileTotalBytes = entrySize, EntryFailureReason = EntryFailureReason.IoFailed, EntryErrorDetail = iox.Message, EntryErrorHResult = iox.HResult });
                    long failProcessedBytes;
                    int failProcessedFiles;
                    lock (syncLock)
                    {
                        processedBytes += entrySize;
                        failProcessedBytes = processedBytes;
                        failProcessedFiles = processedFiles;
                    }
                    // 主进度报告：失败条目也刷新进度（连续失败时窗口长时间无更新、看似卡死）；失败不计入 ProcessedFiles（已处理）。
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        TotalFiles = entryInfos.Count,
                        ProcessedFiles = failProcessedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = failProcessedBytes,
                        PercentComplete = totalBytes > 0 ? (double)failProcessedBytes / totalBytes * 100 : 0,
                        FilePercentComplete = 100,
                        FileTotalBytes = entrySize,
                        BatchProcessedBytes = batchProcessedBytes,
                        BatchTotalBytes = batchTotalBytes,
                        BatchIndex = batchIndex,
                        BatchCount = actualParallelism,
                        BatchPercentComplete = batchTotalBytes > 0 ? (double)batchProcessedBytes / batchTotalBytes * 100 : 100,
                        BatchProcessedFiles = batchProcessedFiles,
                        BatchTotalFiles = batchTotalFiles
                    }));
                }
                catch (Exception ex)
                {
                    CoreLog.Info($"ExtractEntriesAsyncParallel: unexpected error for '{entryKey}': {ex.Message}");
                    conflictStats.RecordFailed();
                    lock (syncLock) Interlocked.Increment(ref failedEntries);
                    batchProcessedFiles++;
                    batchProcessedBytes += entrySize;
                    // 逐条目状态（D2）：意外异常 → Failed（锁外上报，避免锁竞争）
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed, FileTotalBytes = entrySize, EntryFailureReason = EntryFailureReason.Other, EntryErrorDetail = ex.Message, EntryErrorHResult = ex.HResult });
                    long failProcessedBytes;
                    int failProcessedFiles;
                    lock (syncLock)
                    {
                        processedBytes += entrySize;
                        failProcessedBytes = processedBytes;
                        failProcessedFiles = processedFiles;
                    }
                    // 主进度报告：失败条目也刷新进度（连续失败时窗口长时间无更新、看似卡死）；失败不计入 ProcessedFiles（已处理）。
                    progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
                    {
                        CurrentFile = entryKey,
                        TotalFiles = entryInfos.Count,
                        ProcessedFiles = failProcessedFiles,
                        TotalBytes = totalBytes,
                        ProcessedBytes = failProcessedBytes,
                        PercentComplete = totalBytes > 0 ? (double)failProcessedBytes / totalBytes * 100 : 0,
                        FilePercentComplete = 100,
                        FileTotalBytes = entrySize,
                        BatchProcessedBytes = batchProcessedBytes,
                        BatchTotalBytes = batchTotalBytes,
                        BatchIndex = batchIndex,
                        BatchCount = actualParallelism,
                        BatchPercentComplete = batchTotalBytes > 0 ? (double)batchProcessedBytes / batchTotalBytes * 100 : 100,
                        BatchProcessedFiles = batchProcessedFiles,
                        BatchTotalFiles = batchTotalFiles
                    }));
                }
            }

            // 批次完成：上报批次 100%（即使本批无节流报告，也让批次行状态收敛）；
            // 同时携带全局进度快照，避免 UI 端计数器被空字段清零
            int batchDoneFiles;
            long batchDoneBytes;
            lock (syncLock)
            {
                batchDoneFiles = processedFiles;
                batchDoneBytes = processedBytes;
            }
            progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                TotalFiles = entryInfos.Count,
                ProcessedFiles = batchDoneFiles,
                TotalBytes = totalBytes,
                ProcessedBytes = batchDoneBytes,
                PercentComplete = totalBytes > 0 ? (double)batchDoneBytes / totalBytes * 100 : 100,
                FileTotalBytes = 0,
                BatchProcessedBytes = batchTotalBytes,
                BatchTotalBytes = batchTotalBytes,
                BatchIndex = batchIndex,
                BatchCount = actualParallelism,
                BatchPercentComplete = 100,
                BatchProcessedFiles = batchTotalFiles,
                BatchTotalFiles = batchTotalFiles
            }));
        });

        progress?.Report(conflictStats.ApplyTo(new ArchiveProgress
        {
            CurrentFile = string.Empty,
            PercentComplete = 100
        }));

        CoreLog.Info($"ExtractEntriesAsyncParallel: done, {processedFiles} files, {processedBytes} bytes, {sw.ElapsedMilliseconds}ms, failedEntries={failedEntries}");
        CoreLog.Exit();
    }

    /// <summary>
    /// 主进度单调包装器：保证上报到内层 <see cref="IProgress{T}"/> 的主进度 <c>PercentComplete</c> 单调不减。
    /// 并行解压各批次线程在 syncLock 内取共享计数快照、释放锁后再上报，快照顺序与上报顺序可能交错，
    /// 使消费端（进度窗口 / 有序收集测试）看到百分比回退（进度条回跳）。
    /// 此处在专用锁内完成「钳位 + 上报」原子操作：过期的低百分比被钳到最近一次已上报值。
    /// EntryStatus 状态报告不携带全局百分比语义，直通不上锁。
    /// 专用锁与 syncLock 分离 —— 禁止在 syncLock 内 Report（历史 25x 锁竞争回退，见 AGENTS.md）。
    /// </summary>
    private sealed class MonotonicProgress(IProgress<ArchiveProgress> inner) : IProgress<ArchiveProgress>
    {
        private readonly object _reportLock = new();
        private double _lastPercent;

        public void Report(ArchiveProgress value)
        {
            if (value.EntryStatus is not null)
            {
                inner.Report(value);
                return;
            }

            lock (_reportLock)
            {
                if (value.PercentComplete < _lastPercent)
                {
                    value.PercentComplete = _lastPercent;
                }
                else
                {
                    _lastPercent = value.PercentComplete;
                }

                inner.Report(value);
            }
        }
    }

}
