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
/// ZIP 压缩引擎（基于 SharpCompress，加密使用 SharpSevenZip + OutArchiveFormat.Zip）
/// </summary>
public class ZipEngine : IArchiveEngine
{
    private const int CopyBufferSize = 4194304;

    /// <summary>
    /// 使用 SharpCompress 打开 ZIP 文件，自动检测编码（UTF-8 → GBK 回退）。
    /// SharpCompress 每实例设置编码，无全局副作用。
    /// 
    /// 回退逻辑：只有 ZIP 条目未设置 UTF-8 标志（bit 11）且内容含高位字符时，
    /// 才尝试 GBK 回退。如果 bit 11 已设置，说明条目名是 UTF-8 编码，不进行回退。
    /// </summary>
    internal static IArchive OpenArchiveWithEncodingFallback(string archivePath, string? password = null)
    {
        // 使用 FileShare.Delete 允许在 archive 仍持有流时删除原文件
        var fs = File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var options = new ReaderOptions { Password = password ?? string.Empty };

        IArchive OpenWithGbk()
        {
            fs.Dispose();
            var fs2 = File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var gbkOptions = new ReaderOptions
            {
                Password = password ?? string.Empty,
                ArchiveEncoding = new SharpCompress.Common.ArchiveEncoding
                {
                    Default = Encoding.GetEncoding("gbk")
                }
            };
            // 严格 ZIP 解析（不用 ArchiveFactory.OpenArchive 魔数嗅探——
            // 损坏/全零文件会被误判为 Tar 静默返回 0 条目，吞掉损坏信号）
            return ZipArchive.OpenArchive(fs2, gbkOptions);
        }

        IArchive archive;
        try
        {
            archive = ZipArchive.OpenArchive(fs, options);
        }
        catch (Exception ex)
        {
            CoreLog.Trace("OpenArchiveWithEncodingFallback: failed to open archive: {0}", ex.Message);
            fs.Dispose();
            throw;
        }

        try
        {
            var hasHighAscii = archive.Entries.Any(e =>
                !string.IsNullOrEmpty(e.Key) && e.Key.Any(c => c > 127));

            if (hasHighAscii)
            {
                // 检查 ZIP 中央目录中是否有条目设置了 UTF-8 标志（bit 11）。
                // 如果有，说明条目名本就是 UTF-8 编码，不应回退到 GBK。
                if (ZipHasUtf8Flag(archivePath))
                {
                    CoreLog.Info("OpenArchiveWithEncodingFallback: ZIP has UTF-8 flag (bit 11), keeping UTF-8 codec");
                    return archive;
                }

                // 即使没有 bit 11，如果解码后的文件名看起来像合法的 CJK 文本（而非
                // GBK→UTF-8 误解码的拉丁字符），也保留 UTF-8 解码结果。
                if (LooksLikeValidCjk(archive))
                {
                    CoreLog.Info("OpenArchiveWithEncodingFallback: decoded names appear as valid CJK, keeping UTF-8 codec");
                    return archive;
                }

                CoreLog.Info("OpenArchiveWithEncodingFallback: detected high-ASCII entry names without UTF-8 flag, retrying with GBK");
                archive.Dispose();
                return OpenWithGbk();
            }

            CoreLog.Info("OpenArchiveWithEncodingFallback: entries appear ASCII, keeping default codec");
            return archive;
        }
        catch (Exception ex)
        {
            CoreLog.Trace("OpenArchiveWithEncodingFallback: encoding detection failed, falling back to GBK: {0}", ex.Message);
            archive.Dispose();
            return OpenWithGbk();
        }
    }

    /// <summary>
    /// 读取 ZIP 文件的中央目录，检查是否有任何条目设置了 UTF-8 文件名标志（通用位标志 bit 11 = 0x0800）。
    /// </summary>
    private static bool ZipHasUtf8Flag(string archivePath)
    {
        try
        {
            using var fs = File.OpenRead(archivePath);
            if (fs.Length < 22) return false;

            // 在文件末尾搜索 EOCD（End of Central Directory）签名 0x06054b50
            long eocdPos = -1;
            // EOCD 最小固定长度 22，最大长度 65557（含注释）
            long searchStart = Math.Max(0, fs.Length - 65557);
            byte[] sig = [0x50, 0x4b, 0x05, 0x06]; // little-endian 0x06054b50

            fs.Seek(searchStart, SeekOrigin.Begin);
            byte[] buf = new byte[fs.Length - searchStart];
            int read = fs.Read(buf, 0, buf.Length);

            for (int i = read - 22; i >= 0; i--)
            {
                if (buf[i] == sig[0] && buf[i + 1] == sig[1] &&
                    buf[i + 2] == sig[2] && buf[i + 3] == sig[3])
                {
                    eocdPos = searchStart + i;
                    break;
                }
            }

            if (eocdPos < 0) return false;

            // 解析 EOCD：偏移 16 处为中央目录偏移量（4 bytes）
            uint centralDirOffset = BitConverter.ToUInt32(buf, (int)(eocdPos - searchStart + 16));
            uint centralDirSize = BitConverter.ToUInt32(buf, (int)(eocdPos - searchStart + 12));

            // 遍历中央目录条目
            fs.Seek(centralDirOffset, SeekOrigin.Begin);
            var reader = new BinaryReader(fs, Encoding.UTF8);
            long endPos = centralDirOffset + centralDirSize;

            while (fs.Position < endPos)
            {
                uint sig2 = reader.ReadUInt32();
                if (sig2 != 0x02014b50) break;

                // 跳过版本信息（4 bytes）
                reader.ReadBytes(4);
                // 通用位标志（2 bytes），偏移 8
                ushort flags = reader.ReadUInt16();

                if ((flags & 0x0800) != 0)
                    return true;

                // 跳过剩余的固定头部到达可变长度字段
                // 已读：签名(4) + 版本(2+2) + 标志(2) = 10
                // 再跳：压缩方法(2) + 时间(2) + 日期(2) + CRC(4) + 压缩大小(4) + 未压缩大小(4) = 18
                reader.ReadBytes(18);
                ushort nameLen = reader.ReadUInt16();  // 偏移 28
                ushort extraLen = reader.ReadUInt16(); // 偏移 30
                ushort commentLen = reader.ReadUInt16(); // 偏移 32
                reader.ReadBytes(12); // 磁盘号(2) + 内部属性(2) + 外部属性(4) + 本地偏移(4) = 12

                // 跳过可变长度字段
                reader.ReadBytes(nameLen + extraLen + commentLen);
            }

            return false;
        }
        catch (Exception ex)
        {
            CoreLog.Trace("ZipHasUtf8Flag: failed to read central directory: {0}", ex.Message);
            // 出错时回退到旧行为（回退 GBK）
            return false;
        }
    }

    /// <summary>
    /// 检查已用 UTF-8 解码的条目名是否看起来像合法的 CJK 文本。
    /// GBK→UTF-8 误解码会产生拉丁字符（如 é ① À 等），而合法 CJK 在 U+4E00+ 范围。
    /// </summary>
    private static bool LooksLikeValidCjk(IArchive archive)
    {
        int totalHigh = 0;
        int cjkCount = 0;

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Key)) continue;
            foreach (char c in entry.Key)
            {
                if (c <= 127) continue;
                totalHigh++;
                // CJK 及相关字符范围
                if ((c >= 0x4E00 && c <= 0x9FFF) ||      // CJK Unified Ideographs
                    (c >= 0x3400 && c <= 0x4DBF) ||      // CJK Extension A
                    (c >= 0x2B740 && c <= 0x2B81F) ||    // CJK Extension C
                    (c >= 0xF900 && c <= 0xFAFF) ||      // CJK Compatibility Ideographs
                    (c >= 0x3000 && c <= 0x303F) ||      // CJK Symbols and Punctuation
                    (c >= 0xFF00 && c <= 0xFFEF))        // Halfwidth and Fullwidth Forms
                {
                    cjkCount++;
                }
            }
        }

        return totalHigh > 0 && cjkCount >= totalHigh * 0.5;
    }

    public bool CanHandle(ArchiveFormat format) => format == ArchiveFormat.Zip;

    public bool CanAdd(ArchiveFormat format) => format == ArchiveFormat.Zip;

    public bool CanDelete(ArchiveFormat format) => format == ArchiveFormat.Zip;

    /// <summary>
    /// ZIP 引擎支持并行解压（多实例模式，每个线程独立打开 archive）。
    /// </summary>
    public bool SupportsParallelExtract => true;

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
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Skipped });
                    processedBytes += entry.Size;
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
                    using (var entryStream = entry.OpenEntryStream())
                    using (var outputStream = File.Create(resolvedPath))
                    {
                        var buffer = new byte[CopyBufferSize];
                        var entryProcessed = 0L;
                        var lastReportTime = DateTime.Now;
                        var reportInterval = TimeSpan.FromMilliseconds(100);

                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var read = entryStream.Read(buffer, 0, buffer.Length);
                            if (read <= 0) break;

                            outputStream.Write(buffer, 0, read);
                            entryProcessed += read;

                            var now = DateTime.Now;
                            if (now - lastReportTime >= reportInterval || entryProcessed >= entrySize)
                            {
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
                                    FilePercentComplete = filePct
                                }));
                                lastReportTime = now;
                            }
                        }
                    }
                    // 恢复文件原始修改时间
                    try { File.SetLastWriteTime(resolvedPath, entryModified); } catch (Exception tsEx) { CoreLog.Info($"ExtractAsyncSequential: failed to set timestamp on {resolvedPath}: {tsEx.Message}"); }

                    processedBytes += entrySize;
                    processedFiles++;
                    // 逐条目状态（D2）：写入成功 → Completed 或 Overwritten
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = entryOverwritten ? ArchiveEntryStatus.Overwritten : ArchiveEntryStatus.Completed });
                }
                catch (UnauthorizedAccessException uax)
                {
                    CoreLog.Info($"ExtractAsyncSequential: permission denied for '{entryKey}': {uax.Message}");
                    failedEntries++;
                    conflictStats.RecordFailed();
                    // 逐条目状态（D2）：权限失败 → Failed
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed });
                }
                catch (IOException iox)
                {
                    // 目标文件被其他进程占用（如正被 Word 打开）等 IO 失败：
                    // 跳过该条目继续，避免单个文件导致整个解压中止（对齐 UnauthorizedAccessException 分支）
                    CoreLog.Info($"ExtractAsyncSequential: write failed for '{entryKey}': {iox.Message}");
                    failedEntries++;
                    conflictStats.RecordFailed();
                    // 逐条目状态（D2）：IO 写入失败 → Failed
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed });
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
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Skipped });
                    lock (syncLock)
                    {
                        processedBytes += entrySize;
                    }
                    batchProcessedFiles++;
                    batchProcessedBytes += entrySize;
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
                        progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed });
                        lock (syncLock) Interlocked.Increment(ref failedEntries);
                        batchProcessedFiles++;
                        batchProcessedBytes += entrySize;
                        continue;
                    }

                    using var entryStream = entry.OpenEntryStream();
                    using var outputStream = File.Create(resolvedPath);
                    var buffer = new byte[CopyBufferSize];
                    long entryProcessed = 0;
                    var entryLastReportTime = DateTime.Now;

while (true)
                        {
                            ct.ThrowIfCancellationRequested();
                            var read = await entryStream.ReadAsync(buffer, 0, buffer.Length, ct);
                            if (read <= 0) break;

                            await outputStream.WriteAsync(buffer, 0, read, ct);
                            entryProcessed += read;

                            // 报告当前文件进度（节流）
                            var now = DateTime.Now;
                            if (now - entryLastReportTime >= TimeSpan.FromMilliseconds(100) || entryProcessed >= entrySize)
                            {
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
                                    BatchIndex = batchIndex,
                                    BatchCount = actualParallelism,
                                    BatchPercentComplete = batchTotalBytes > 0 ? (double)batchProcessedBytes / batchTotalBytes * 100 : 100,
                                    BatchProcessedFiles = batchProcessedFiles,
                                    BatchTotalFiles = batchTotalFiles
                                }));

                                entryLastReportTime = now;
                            }
                        }

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
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = entryOverwritten ? ArchiveEntryStatus.Overwritten : ArchiveEntryStatus.Completed });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (UnauthorizedAccessException uax)
                {
                    CoreLog.Info($"ExtractAsyncParallel: permission denied for '{entryKey}': {uax.Message}");
                    conflictStats.RecordFailed();
                    lock (syncLock) Interlocked.Increment(ref failedEntries);
                    batchProcessedFiles++;
                    batchProcessedBytes += entrySize;
                    // 逐条目状态（D2）：权限失败 → Failed（锁外上报，避免锁竞争）
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed });
                }
                catch (IOException iox)
                {
                    CoreLog.Info($"ExtractAsyncParallel: write failed for '{entryKey}': {iox.Message}");
                    conflictStats.RecordFailed();
                    lock (syncLock) Interlocked.Increment(ref failedEntries);
                    batchProcessedFiles++;
                    batchProcessedBytes += entrySize;
                    // 逐条目状态（D2）：IO 写入失败 → Failed（锁外上报，避免锁竞争）
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed });
                }
                catch (Exception ex)
                {
                    CoreLog.Info($"ExtractAsyncParallel: unexpected error for '{entryKey}': {ex.Message}");
                    conflictStats.RecordFailed();
                    lock (syncLock) Interlocked.Increment(ref failedEntries);
                    batchProcessedFiles++;
                    batchProcessedBytes += entrySize;
                    // 逐条目状态（D2）：意外异常 → Failed（锁外上报，避免锁竞争）
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed });
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
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Skipped });
                    processedBytes += entry.Size;
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
                    using (var entryStream = entry.OpenEntryStream())
                    using (var outputStream = File.Create(resolvedPath))
                    {
                        var buffer = new byte[CopyBufferSize];
                        long entryProcessed = 0;
                        var lastReportTime = DateTime.Now;
                        var reportInterval = TimeSpan.FromMilliseconds(100);

                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var read = entryStream.Read(buffer, 0, buffer.Length);
                            if (read <= 0) break;

                            outputStream.Write(buffer, 0, read);
                            entryProcessed += read;

                            var now = DateTime.Now;
                            if (now - lastReportTime >= reportInterval || entryProcessed >= entrySize)
                            {
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
                                    FilePercentComplete = filePct
                                }));
                                lastReportTime = now;
                            }
                        }
                    }

                    try { File.SetLastWriteTime(resolvedPath, entryModified); } catch { CoreLog.Trace("ZipEngine.ExtractAsync: failed to set last write time for '{0}'", resolvedPath); }

                    processedBytes += entrySize;
                    processedFiles++;
                    // 逐条目状态（D2）：写入成功 → Completed 或 Overwritten
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = entryOverwritten ? ArchiveEntryStatus.Overwritten : ArchiveEntryStatus.Completed });
                }
                catch (OperationCanceledException) { throw; }
                catch (UnauthorizedAccessException uax)
                {
                    CoreLog.Info($"ExtractEntriesAsyncSequential: permission denied for '{entryKey}': {uax.Message}");
                    failedEntries++;
                    conflictStats.RecordFailed();
                    // 逐条目状态（D2）：权限失败 → Failed
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed });
                }
                catch (IOException iox)
                {
                    // 目标文件被其他进程占用等 IO 失败：跳过该条目继续，避免单个文件中止整个解压
                    CoreLog.Info($"ExtractEntriesAsyncSequential: write failed for '{entryKey}': {iox.Message}");
                    failedEntries++;
                    conflictStats.RecordFailed();
                    // 逐条目状态（D2）：IO 写入失败 → Failed
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed });
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
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Skipped });
                    lock (syncLock)
                    {
                        processedBytes += entrySize;
                    }
                    batchProcessedFiles++;
                    batchProcessedBytes += entrySize;
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
                        progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed });
                        lock (syncLock) Interlocked.Increment(ref failedEntries);
                        batchProcessedFiles++;
                        batchProcessedBytes += entrySize;
                        continue;
                    }

                    using var entryStream = entry.OpenEntryStream();
                    using var outputStream = File.Create(resolvedPath);
                    var buffer = new byte[CopyBufferSize];
                    long entryProcessed = 0;
                    var entryLastReportTime = DateTime.Now;

                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();
                        var read = await entryStream.ReadAsync(buffer, 0, buffer.Length, ct);
                        if (read <= 0) break;

                        await outputStream.WriteAsync(buffer, 0, read, ct);
                        entryProcessed += read;

                        // 报告当前文件进度（节流）
                        var now = DateTime.Now;
                        if (now - entryLastReportTime >= TimeSpan.FromMilliseconds(100) || entryProcessed >= entrySize)
                        {
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
                                BatchIndex = batchIndex,
                                BatchCount = actualParallelism,
                                BatchPercentComplete = batchTotalBytes > 0 ? (double)batchProcessedBytes / batchTotalBytes * 100 : 100,
                                BatchProcessedFiles = batchProcessedFiles,
                                BatchTotalFiles = batchTotalFiles
                            }));

                            entryLastReportTime = now;
                        }
                    }

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
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = entryOverwritten ? ArchiveEntryStatus.Overwritten : ArchiveEntryStatus.Completed });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (UnauthorizedAccessException uax)
                {
                    CoreLog.Info($"ExtractEntriesAsyncParallel: permission denied for '{entryKey}': {uax.Message}");
                    conflictStats.RecordFailed();
                    lock (syncLock) Interlocked.Increment(ref failedEntries);
                    batchProcessedFiles++;
                    batchProcessedBytes += entrySize;
                    // 逐条目状态（D2）：权限失败 → Failed（锁外上报，避免锁竞争）
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed });
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
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed });
                }
                catch (Exception ex)
                {
                    CoreLog.Info($"ExtractEntriesAsyncParallel: unexpected error for '{entryKey}': {ex.Message}");
                    conflictStats.RecordFailed();
                    lock (syncLock) Interlocked.Increment(ref failedEntries);
                    batchProcessedFiles++;
                    batchProcessedBytes += entrySize;
                    // 逐条目状态（D2）：意外异常 → Failed（锁外上报，避免锁竞争）
                    progress?.Report(new ArchiveProgress { EntryKey = entryKey, EntryStatus = ArchiveEntryStatus.Failed });
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

    public async Task CompressAsync(string[] sourcePaths, string outputPath, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"CompressAsync: [{string.Join("; ", sourcePaths)}] -> {outputPath}, level={options.CompressionLevel}, split={options.SplitSize}");
        var sw = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            // 收集所有文件（使用 FileScanner 共享工具，边发现边报告进度）
            var (files, totalBytes) = FileScanner.CollectFiles(sourcePaths, progress, cancellationToken, options.FileWhitelist);

            if (files.Count == 0)
            {
                CoreLog.Info("CompressAsync: no files to compress, returning");
                return;
            }

            CoreLog.Info($"CompressAsync: {files.Count} files to compress, {totalBytes} bytes total");

            long processedBytes = 0;
            int totalFiles = files.Count;
            int processedFiles = 0;

            try
            {
                var outputStream = options.SplitSize > 0
                    ? (Stream)new SplitOutputStream(outputPath, options.SplitSize)
                    : File.Create(outputPath);
                using var fsOut = outputStream;

                var lastReportTime = DateTime.Now;
                var reportInterval = TimeSpan.FromMilliseconds(100);
                var isEncrypted = options.Encrypt && !string.IsNullOrEmpty(options.Password);

                if (isEncrypted)
                {
                    // SharpSevenZip 支持 ZIP + AES-256 加密（SharpCompress ZipWriter 不支持加密）
                    fsOut.Dispose();

                    SevenZipEngine.EnsureLibraryPath();

var zipMethod = MapZipMethodToS7Z(options.ZipCompressionMethod);
                    var zipEncrypt = options.ZipEncryptionMethod?.ToLowerInvariant() switch
                    {
                        "zipcrypto" => ZipEncryptionMethod.ZipCrypto,
                        "aes128" => ZipEncryptionMethod.Aes128,
                        "aes192" => ZipEncryptionMethod.Aes192,
                        _ => ZipEncryptionMethod.Aes256,
                    };

                    var s7zCompressor = new SharpSevenZipCompressor
                    {
                        ArchiveFormat = OutArchiveFormat.Zip,
                        ZipEncryptionMethod = zipEncrypt,
                        CompressionMethod = zipMethod,
                        CompressionLevel = MapCompressionLevelToS7Z(options.CompressionLevel),
                        IncludeEmptyDirectories = true,
                        DirectoryStructure = true,
                    };

                    if (options.SplitSize > 0)
                        s7zCompressor.VolumeSize = options.SplitSize;

                    if (!string.IsNullOrEmpty(options.Comment))
                    {
                        // SharpSevenZip 无 Comment 属性，注释在压缩后通过 EOCD 写入
                    }

                    var s7zAccumPct = 0.0;
                    // 单文件进度：PercentDelta 是「当前文件」的增量，故必须从文件起点单独累计。
                    // 不能复用 s7zAccumPct（那是整包累计），否则 FilePercentComplete 会等于总进度，
                    // UI 的行内底纹就退化成整包进度条。
                    var s7zFilePct = 0.0;
                    var s7zCurrentFile = "";
                    s7zCompressor.FileCompressionStarted += (_, e) =>
                    {
                        s7zCurrentFile = e.FileName ?? "";
                        s7zFilePct = 0;
                    };
                    s7zCompressor.Compressing += (_, e) =>
                    {
                        s7zAccumPct = Math.Min(100, s7zAccumPct + e.PercentDelta);
                        s7zFilePct = Math.Min(100, s7zFilePct + e.PercentDelta);
                        progress?.Report(new ArchiveProgress
                        {
                            CurrentFile = s7zCurrentFile,
                            PercentComplete = s7zAccumPct,
                            FilePercentComplete = s7zFilePct,
                            TotalFiles = totalFiles,
                            ProcessedFiles = processedFiles,
                        });
                    };

                    var sourceFilePaths = files.Select(f => f.FullPath).Distinct().ToArray();
                    CoreLog.Trace($"[TRACE] CompressAsync encrypted 7z: {sourceFilePaths.Length} files, outputPath={outputPath}");
                    foreach (var fp in sourceFilePaths)
                        CoreLog.Trace($"[TRACE]   7z input: {fp}");
                    // 加密 ZIP 走 SharpSevenZip 单次原生调用，无法逐文件恢复读取错误。
                    // 调用前预检读权限（被占用/权限 → ErrorResolver 弹窗 重试/跳过/中止），
                    // 跳过则剔除，避免单个不可读文件导致整个加密压缩直接中止。
                    var validated = ReadErrorHandler.FilterUnreadableFiles(sourceFilePaths, options, cancellationToken);
                    if (validated.Count > 0)
                    {
                        s7zCompressor.CompressFilesEncrypted(outputPath, options.Password ?? "", validated.ToArray());
                    }
                    else
                    {
                        CoreLog.Info("ZipEngine.CompressAsync: all files skipped due to read errors, nothing to compress");
                    }

                    // SharpSevenZip 的 Compressing 事件 delta 累积通常达不到 100，
                    // 压缩完成后必须补发最终报告，否则进度条停在最后一个文件的中间值。
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = string.Empty,
                        PercentComplete = 100,
                        FilePercentComplete = 100,
                        TotalFiles = totalFiles,
                        ProcessedFiles = totalFiles,
                    });

                    processedBytes = totalBytes;
                    processedFiles = totalFiles;

                    // ZIP 注释：SharpSevenZip 不支持压缩时写入，压缩后通过 EOCD 后写
                    if (!string.IsNullOrEmpty(options.Comment))
                    {
                        try { ZipCommentHelper.WriteComment(outputPath, options.Comment); }
                        catch (Exception commentEx) { CoreLog.Error("CompressAsync: failed to write ZIP comment", commentEx); }
                    }
                }
                else
                {
                    var encoding = (options.FileNameEncoding?.ToLowerInvariant()) switch
                    {
                        "gbk" => Encoding.GetEncoding("GBK"),
                        "default" => Encoding.Default,
                        _ => Encoding.UTF8,
                    };
                    var compressionType = options.ZipCompressionMethod?.ToLowerInvariant() switch
                    {
                        "deflate64" => CompressionType.Deflate64,
                        "bzip2" => CompressionType.BZip2,
                        "lzma" => CompressionType.LZMA,
                        "ppmd" => CompressionType.PPMd,
                        "store" => CompressionType.None,
                        _ => CompressionType.Deflate,
                    };
                    var writerOptions = new ZipWriterOptions(compressionType)
                    {
                        // SharpCompress 在 ZipWriterOptions 的 setter 内即校验级别
                        // （CompressionLevelValidation.Validate）：只有 Deflate / Deflate64
                        // 接受可配置级别，BZip2 / LZMA / PPMd / None 传入非 0 会直接抛
                        // ArgumentOutOfRangeException。默认级别为 5，用户一旦选择这些方法
                        // 就必然命中 → 统一降为 0（0 = 该方法的默认设置，不是「不压缩」）。
                        // 注意：本对象在下方 MT 判定之前构造，MT 路径虽完全不经过 ZipWriter，
                        // 也必须先通过这道校验，否则 MT 用户同样崩溃。
                        CompressionLevel = compressionType is CompressionType.Deflate or CompressionType.Deflate64
                            ? options.CompressionLevel
                            : 0,
                        ArchiveComment = options.Comment ?? "",
                        ArchiveEncoding = new ArchiveEncoding { Default = encoding },
                    };
                    // ── MultiThreaded 自适应压缩：已压缩文件 Store + 可压缩文件 7z mt=on ──
                    // 关键约束：MT 路径完全不使用 SharpCompress ZipWriter。7z 的压缩字节必须
                    // 原样进入最终 ZIP —— 一旦经过 ZipWriter.WriteToStream 就必然被解压后重新
                    // 压缩（历史双重压缩 bug：mt=on 的并行成果 100% 丢弃，且 CompressionLevel=null
                    // 使最终压缩级别与用户设置脱钩）。
                    // 分卷（SplitSize > 0）依赖 SplitOutputStream 包装，MT 路径不参与。
                    if (IsMultiThreadedEligible(options, files.Count, totalBytes))
                    {
                        var storeGroup = new List<(string FullPath, string RelativePath)>();
                        var compressGroup = new List<(string FullPath, string RelativePath)>();
                        foreach (var file in files)
                        {
                            var level = ZipEntryClassifier.GetAdaptiveLevel(file.FullPath, options.CompressionLevel, options.AdaptiveCompression, options.MultiThreadedStoreFormatIds);
                            if (level == 0) storeGroup.Add(file);
                            else compressGroup.Add(file);
                        }

                        CoreLog.Trace($"[TRACE] CompressAsync StoreGroup: {storeGroup.Count}, CompressGroup: {compressGroup.Count}");

                        // 全部条目都是 Store → 无可压缩内容，7z 阶段没有意义。
                        // 回落到标准串行路径（ReadFileWithRetry 内部同样应用自适应分类）。
                        if (compressGroup.Count == 0)
                        {
                            CoreLog.Info("CompressAsync: MultiThreaded skipped — no compressible entries, using serial path");
                        }
                        else if (storeGroup.Count > 0 && !CanCopyModeRewrite(options.ZipCompressionMethod))
                        {
                            // 混合场景必须经 copy-mode 重写，而该压缩方法不被重写器承载 → 回落串行
                            LogCopyModeUnsupported(options.ZipCompressionMethod);
                        }
                        else
                        {
                            fsOut.Dispose(); // 与加密路径同理：改由 7z + 二进制改写直接产出 outputPath

                            var tempZip = Path.Combine(Path.GetTempPath(), $"mantiszip_mt_{Guid.NewGuid():N}.zip");
                            try
                            {
                                CoreLog.Info($"CompressAsync: MultiThreaded mode — {storeGroup.Count} store, {compressGroup.Count} compress via 7z mt=on");
                                CompressGroupWithSevenZip(compressGroup, tempZip, options, progress, 0, totalBytes, totalFiles, 0, null, null, ref lastReportTime);

                                if (storeGroup.Count == 0)
                                {
                                    // 全部可压缩：7z 产物即最终产物 —— 零合并、零重压
                                    File.Move(tempZip, outputPath, overwrite: true);
                                    CoreLog.Info("CompressAsync: MultiThreaded — 7z archive is the final archive, no merge needed");
                                }
                                else
                                {
                                    // 混合：tempZip 作为 copy-mode 源，逐条目原样复制 LFH + 压缩数据
                                    //（7z 字节完整保留）；storeGroup 以 method 0 (Store) 追加。
                                    var storeStreams = new List<Stream>(storeGroup.Count);
                                    try
                                    {
                                        var storeEntries = new List<NewEntry>(storeGroup.Count);
                                        foreach (var (fullPath, relativePath) in storeGroup)
                                        {
                                            cancellationToken.ThrowIfCancellationRequested();
                                            var storeStream = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                                            storeStreams.Add(storeStream);
                                            storeEntries.Add(new NewEntry(
                                                EntryName: ArchivePath.Normalize(relativePath),
                                                Data: storeStream,
                                                LastModified: File.GetLastWriteTime(fullPath),
                                                Size: storeStream.Length,
                                                Store: true));
                                        }

                                        // 条目名编码固定 UTF-8：与 7z 产物保持一致（混用编码会破坏条目名）
                                        ZipBinaryRewriter.RewriteAsync(
                                                sourcePath: tempZip,
                                                destPath: outputPath,
                                                keepEntryNames: null,   // null = 保留 tempZip 全部条目
                                                addEntries: storeEntries,
                                                encoding: Encoding.UTF8,
                                                comment: options.Comment,
                                                progress: progress,
                                                cancellationToken: cancellationToken)
                                            .GetAwaiter().GetResult();

                                        foreach (var (_, relativePath) in storeGroup)
                                        {
                                            progress?.Report(new ArchiveProgress
                                            {
                                                EntryKey = relativePath,
                                                EntryStatus = ArchiveEntryStatus.Completed
                                            });
                                        }
                                    }
                                    finally
                                    {
                                        foreach (var s in storeStreams) { try { s.Dispose(); } catch { } }
                                    }
                                }

                                // ZIP 注释：7z 与二进制改写路径都不写注释，产出后补写
                                if (!string.IsNullOrEmpty(options.Comment))
                                {
                                    try { ZipCommentHelper.WriteComment(outputPath, options.Comment); }
                                    catch (Exception commentEx) { CoreLog.Error("CompressAsync: failed to write ZIP comment", commentEx); }
                                }

                                processedBytes = totalBytes;
                                processedFiles = totalFiles;
                                progress?.Report(new ArchiveProgress
                                {
                                    CurrentFile = string.Empty,
                                    PercentComplete = 100,
                                    FilePercentComplete = 100,
                                    TotalFiles = totalFiles,
                                    ProcessedFiles = totalFiles,
                                });
                            }
                            finally
                            {
                                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                            }

                            return; // MultiThreaded 路径完成，跳过标准 foreach
                        }
                    }

                    using var zipWriter = new ZipWriter(fsOut, writerOptions);

                    foreach (var (fullPath, relativePath) in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!ReadFileWithRetry(fullPath, relativePath, options, zipWriter,
                                ref processedBytes, totalBytes, totalFiles, ref processedFiles,
                                cancellationToken, progress, ref lastReportTime))
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            // 逐条目状态（D2）：读取被跳过 → Skipped
                            progress?.Report(new ArchiveProgress { EntryKey = relativePath, EntryStatus = ArchiveEntryStatus.Skipped });
                            continue;
                        }

                        // 逐条目状态（D2）：写入成功 → Completed
                        progress?.Report(new ArchiveProgress { EntryKey = relativePath, EntryStatus = ArchiveEntryStatus.Completed });

                        var now = DateTime.Now;
                        if (now - lastReportTime >= reportInterval)
                        {
                            var pct = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : 0;
                            progress?.Report(new ArchiveProgress
                            {
                                CurrentFile = relativePath,
                                PercentComplete = pct,
                                FilePercentComplete = 100,
                                TotalFiles = totalFiles,
                                ProcessedFiles = processedFiles
                            });
                            lastReportTime = now;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                CoreLog.Info("CompressAsync: cancelled, cleaning up split files");
                if (options.SplitSize > 0)
                {
                    CleanupSplitFiles(outputPath);
                }
                else if (File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); } catch (Exception cleanupEx) { CoreLog.Error("CompressAsync: failed to clean up partial output", cleanupEx); }
                }
                throw;
            }
            catch (Exception ex)
            {
                CoreLog.Error($"CompressAsync failed", ex);
                throw;
            }

            CoreLog.Info($"CompressAsync: done, {processedBytes}/{totalBytes} bytes, {sw.ElapsedMilliseconds}ms");
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }

    public async Task<IReadOnlyList<ArchiveItem>> ListEntriesAsync(string archivePath, string? password = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"ListEntriesAsync: {archivePath}");
        var sw = Stopwatch.StartNew();

        var result = await Task.Run(() =>
        {
            using var archive = OpenArchiveWithEncodingFallback(archivePath, password);

            var items = archive.Entries.Select(entry =>
            {
                var entryKey = ArchivePath.Normalize(entry.Key);
                return new ArchiveItem
                {
                    Name = entryKey,
                    FullPath = entry.IsDirectory ? entryKey.TrimEnd('/') : entryKey,
                    Size = entry.Size,
                    CompressedSize = entry.CompressedSize,
                    LastModified = entry.LastModifiedTime ?? DateTime.MinValue,
                    IsDirectory = entry.IsDirectory,
                    IsEncrypted = entry.IsEncrypted,
                    Crc32 = (int)(entry.Crc & 0xFFFFFFFF)
                };
            }).ToList();

            CoreLog.Info($"ListEntriesAsync: {items.Count} entries, {sw.ElapsedMilliseconds}ms");

            // 交叉校验：SharpCompress 报告了加密条目 → 用二进制解析直接检查中央目录的 flags
            // 这是针对 SharpCompress 在某些环境（如 Win11 日文版）的假阳性 bug
            if (items.Any(i => i.IsEncrypted))
            {
                var actuallyEncrypted = VerifyZipEncryptionFlags(archivePath);
                if (!actuallyEncrypted)
                {
                    CoreLog.Info("WARN: ListEntriesAsync: SharpCompress reported encrypted entries but binary CD flags show none — overriding IsEncrypted to false (possible false positive on {0})", archivePath);
                    foreach (var item in items)
                    {
                        item.IsEncrypted = false;
                    }
                }
            }

            return (IReadOnlyList<ArchiveItem>)items;
        }, cancellationToken).ConfigureAwait(false);

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
                using var archive = OpenArchiveWithEncodingFallback(archivePath, password);

                var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
                int totalFiles = entries.Count;
                int processedFiles = 0;

                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 完全解压每个条目以验证数据完整性
                    // SharpCompress 在读取完整流时内部会检测 CRC 等错误
                    using var stream = entry.OpenEntryStream();

                    long entrySize = entry.Size;
                    long totalRead = 0;
                    var lastReportTime = DateTime.Now;
                    var reportInterval = TimeSpan.FromMilliseconds(100);

                    // 文件开始：文件进度条归零
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = entry.Key ?? "",
                        PercentComplete = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 100,
                        FilePercentComplete = 0,
                        TotalFiles = totalFiles,
                        ProcessedFiles = processedFiles,
                    });

                    // 带 per-file 进度的复制循环（100ms 节流，末尾强制上报 100%）
                    var buffer = new byte[CopyBufferSize];
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var read = stream.Read(buffer, 0, buffer.Length);
                        if (read <= 0) break;
                        totalRead += read;

                        var now = DateTime.Now;
                        if (now - lastReportTime >= reportInterval || totalRead >= entrySize)
                        {
                            var filePct = entrySize > 0 ? (double)totalRead / entrySize * 100 : 100;
                            progress?.Report(new ArchiveProgress
                            {
                                CurrentFile = entry.Key ?? "",
                                PercentComplete = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 100,
                                FilePercentComplete = filePct,
                                TotalFiles = totalFiles,
                                ProcessedFiles = processedFiles,
                            });
                            lastReportTime = now;
                        }
                    }

                    processedFiles++;

                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = entry.Key ?? "",
                        PercentComplete = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 100,
                        FilePercentComplete = 100,
                        TotalFiles = totalFiles,
                        ProcessedFiles = processedFiles,
                    });
                }

                CoreLog.Info($"TestArchiveAsync: passed, {totalFiles} entries verified");
                return true;
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

    /// <summary>
    /// 删除分卷压缩产生的所有分卷文件。
    /// </summary>
    private static void CleanupSplitFiles(string basePath)
    {
        var dir = Path.GetDirectoryName(basePath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(basePath);
        var ext = Path.GetExtension(basePath);
        for (int i = 1; i < 1000; i++)
        {
            var partPath = Path.Combine(dir, $"{name}{ext}.{i:D3}");
            if (File.Exists(partPath))
            {
                try { File.Delete(partPath); } catch (Exception cleanupEx) { CoreLog.Error("CleanupSplitFiles: failed to delete", cleanupEx); }
            }
            else
            {
                break; // 遇到断号即停止
            }
        }
    }

    /// <summary>
    /// 将 0–9 压缩级别映射到 SharpSevenZip.CompressionLevel 枚举。
    /// </summary>
    private static SharpSevenZip.CompressionLevel MapCompressionLevelToS7Z(int level) => level switch
    {
        0 => SharpSevenZip.CompressionLevel.None,
        1 or 2 => SharpSevenZip.CompressionLevel.Fast,
        3 or 4 => SharpSevenZip.CompressionLevel.Low,
        5 or 6 => SharpSevenZip.CompressionLevel.Normal,
        7 or 8 => SharpSevenZip.CompressionLevel.High,
        9 => SharpSevenZip.CompressionLevel.Ultra,
        _ => SharpSevenZip.CompressionLevel.Normal,
    };

    /// <summary>
    /// 把用户选择的 ZIP 压缩方法映射为 SharpSevenZip <see cref="CompressionMethod"/>。
    /// </summary>
    private static CompressionMethod MapZipMethodToS7Z(string? zipCompressionMethod) =>
        zipCompressionMethod?.ToLowerInvariant() switch
        {
            "deflate64" => CompressionMethod.Deflate64,
            "bzip2" => CompressionMethod.BZip2,
            "lzma" => CompressionMethod.Lzma,
            "ppmd" => CompressionMethod.Ppmd,
            "copy" or "store" => CompressionMethod.Copy,
            _ => CompressionMethod.Deflate,
        };

    public async Task AddToArchiveAsync(string archivePath, string[] sourcePaths, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default, string? entryBasePath = null)
    {
        CoreLog.Entry();
        CoreLog.Info($"AddToArchiveAsync: {archivePath}, sources=[{string.Join("; ", sourcePaths)}]");
        var sw = Stopwatch.StartNew();

        await Task.Run(async () =>
        {
            // 收集需要添加的新文件
            var newFiles = new List<(string FullPath, string EntryName)>();
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
                        var entryName = string.IsNullOrEmpty(entryBasePath) ? relativePath : entryBasePath + "/" + relativePath;
                        newFiles.Add((file, entryName));
                    }
                }
                else if (File.Exists(sourcePath))
                {
                    var entryName = string.IsNullOrEmpty(entryBasePath) ? Path.GetFileName(sourcePath) : entryBasePath + "/" + Path.GetFileName(sourcePath);
                    newFiles.Add((sourcePath, entryName));
                }
            }

            if (newFiles.Count == 0)
            {
                CoreLog.Info("AddToArchiveAsync: no files to add");
                return;
            }

            // 计算旧条目信息（使用 SharpCompress IArchive 读取）——同时收集条目名/大小/时间供冲突处理
            int oldEntryCount = 0;
            long oldTotalBytes = 0;
            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingRawNames = new List<string>();
            var existingEntryInfo = new Dictionary<string, (long Size, DateTime? Modified)>(StringComparer.OrdinalIgnoreCase);
            using (var archive = OpenArchiveWithEncodingFallback(archivePath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;
                    oldTotalBytes += entry.Size;
                    oldEntryCount++;
                    var rawName = entry.Key ?? string.Empty;
                    var normalized = ArchivePath.Normalize(rawName);
                    existingNames.Add(normalized);
                    existingRawNames.Add(rawName);
                    existingEntryInfo[normalized] = (entry.Size, entry.LastModifiedTime);
                }
            }

            // 解析条目名冲突（复用解压冲突策略；语义方向反转见 AddConflictHelper）
            var occupiedNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
            var resolvedFiles = new List<(string FullPath, string EntryName)>();
            var overwrittenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (fullPath, entryName) in newFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalized = ArchivePath.Normalize(entryName);
                existingEntryInfo.TryGetValue(normalized, out var existing);
                var fi = new FileInfo(fullPath);
                var finalName = await AddConflictHelper.ResolveEntryNameAsync(
                    normalized, options, existing.Modified, existing.Size, fi.LastWriteTime, fi.Length, occupiedNames);
                if (finalName == null)
                {
                    CoreLog.Info($"AddToArchiveAsync: skipped '{entryName}' (conflict action)");
                    continue;
                }
                if (existingNames.Contains(normalized) && finalName == normalized)
                    overwrittenNames.Add(normalized); // 覆盖：copy-mode 需从 keepEntryNames 排除旧条目
                resolvedFiles.Add((fullPath, finalName));
            }

            if (resolvedFiles.Count == 0)
            {
                CoreLog.Info("AddToArchiveAsync: all files skipped by conflict handling");
                return;
            }

            long newTotalBytes = resolvedFiles.Sum(f => new FileInfo(f.FullPath).Length);
            // 总工作量 = 提取旧条目字节 + 压缩全部字节
            long workTotal = oldTotalBytes + oldTotalBytes + newTotalBytes;
            if (workTotal == 0) workTotal = 1;

            // ── Optimized copy-mode path (binary rewrite, no decompress-recompress) ──
            if (!(options.Encrypt && !string.IsNullOrEmpty(options.Password)))
            {
                string? tempArchiveFast = null;
                try
                {
                    CoreLog.Info("AddToArchiveAsync: attempting copy-mode fast path");
                    tempArchiveFast = Path.GetTempFileName() + ".zip";

                    // Detect encoding: check UTF-8 flag to choose between UTF-8 and GBK
                    var encoding = ZipHasUtf8Flag(archivePath) ? Encoding.UTF8 : Encoding.GetEncoding("gbk");

                    // Build NewEntry list from source paths with auto-cleanup
                    var newEntries = new List<NewEntry>();
                    var streamsToDispose = new List<Stream>();
                    try
                    {
                        foreach (var (fullPath, entryName) in resolvedFiles)
                        {
                            // 共享读：源文件可能正被编辑器以写权限持有
                            var fileStream = SharedReadStream.OpenRead(fullPath);
                            streamsToDispose.Add(fileStream);
                            var fi = new FileInfo(fullPath);
                            newEntries.Add(new NewEntry(
                                EntryName: ArchivePath.Normalize(entryName),
                                Data: fileStream,
                                LastModified: fi.LastWriteTime,
                                Size: fi.Length));
                        }

                        // 覆盖重名条目时排除旧条目（keepSet 存原始名 + OrdinalIgnoreCase，与 DeleteEntriesAsync 一致）
                        HashSet<string>? keepEntryNames = null;
                        if (overwrittenNames.Count > 0)
                        {
                            keepEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var raw in existingRawNames)
                                if (!overwrittenNames.Contains(ArchivePath.Normalize(raw)))
                                    keepEntryNames.Add(raw);
                        }

                        var result = await ZipBinaryRewriter.RewriteAsync(
                            sourcePath: archivePath,
                            destPath: tempArchiveFast,
                            keepEntryNames: keepEntryNames,
                            addEntries: newEntries,
                            encoding: encoding,
                            comment: options.Comment,  // null = preserve original comment
                            progress: progress,
                            cancellationToken: cancellationToken);

                        // Atomic replace (same retry pattern as legacy path)
                        for (int retry = 0; ; retry++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            try
                            {
                                File.Delete(archivePath);
                                File.Move(tempArchiveFast, archivePath);
                                break;
                            }
                            catch (IOException) when (retry < 5)
                            {
                                Thread.Sleep(100);
                            }
                        }

                        progress?.Report(new ArchiveProgress
                        {
                            CurrentFile = string.Empty,
                            PercentComplete = 100,
                            FilePercentComplete = 100
                        });

                        CoreLog.Info($"AddToArchiveAsync: copy-mode fast path done, {result.EntriesCopied} entries copied, {result.EntriesAdded} entries added");
                        return;
                    }
                    finally
                    {
                        foreach (var s in streamsToDispose)
                            s.Dispose();
                    }
                }
                catch (ZipCopyModeException)
                {
                    CoreLog.Info("AddToArchiveAsync: copy-mode not available, falling back to legacy path");
                    if (tempArchiveFast != null)
                    {
                        try { if (File.Exists(tempArchiveFast)) File.Delete(tempArchiveFast); } catch { }
                    }
                }
            }

            // 创建临时目录
            var tempDir = Path.Combine(Path.GetTempPath(), "MantisZip", "Rebuild", Guid.NewGuid().ToString());
            var tempArchive = tempDir + ".new.zip";
            try
            {
                Directory.CreateDirectory(tempDir);

                // === Phase 1: 提取旧条目到临时目录（逐文件，字节加权进度） ===
                long processedBytes = 0;
                var lastReportTime = DateTime.Now;
                var reportInterval = TimeSpan.FromMilliseconds(100);

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = "正在提取旧条目...",
                    PercentComplete = 0,
                    FilePercentComplete = 0
                });
                CoreLog.Trace("[TRACE] ZipEngine.AddToArchiveAsync: Phase 1 — extracting old entries");

                using (var archive = OpenArchiveWithEncodingFallback(archivePath, options.Password))
                {
                    // Check for encrypted entries before starting extraction.
                    // If any non-directory entry is encrypted and no password is
                    // provided, fail early with a clear message instead of relying
                    // on SharpCompress to throw CryptographiceException (which is
                    // environment/version dependent).
                    if (string.IsNullOrEmpty(options.Password))
                    {
                        var hasEncryptedEntry = archive.Entries.Any(e => !e.IsDirectory && e.IsEncrypted);
                        if (hasEncryptedEntry)
                        {
                            CoreLog.Info("AddToArchiveAsync: archive has encrypted entries but no password provided");
                            throw new InvalidOperationException(
                                "此压缩包包含加密条目，需要密码才能添加文件。 (Archive contains encrypted entries, password required to add files.)");
                        }
                    }

                    foreach (var entry in archive.Entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entryName = entry.Key ?? string.Empty;

                        if (entry.IsDirectory)
                        {
                            var dirPath = Path.Combine(tempDir, entryName);
                            if (!Directory.Exists(dirPath))
                                Directory.CreateDirectory(dirPath);
                            continue;
                        }

                        var outPath = Path.Combine(tempDir, entryName);
                        var outDir = Path.GetDirectoryName(outPath);
                        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                            Directory.CreateDirectory(outDir);

                        var entrySize = entry.Size;
                        using (var entryStream = entry.OpenEntryStream())
                        using (var outStream = File.Create(outPath))
                        {
                            var buffer = new byte[CopyBufferSize];
                            long entryProcessed = 0;
                            while (true)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var read = entryStream.Read(buffer, 0, buffer.Length);
                                if (read <= 0) break;
                                outStream.Write(buffer, 0, read);
                                entryProcessed += read;

                                var now = DateTime.Now;
                                if (now - lastReportTime >= reportInterval || entryProcessed >= entrySize)
                                {
                                    var pct = (double)(processedBytes + entryProcessed) / workTotal * 100;
                                    var filePct = entrySize > 0 ? (double)entryProcessed / entrySize * 100 : 100;
                                    progress?.Report(new ArchiveProgress
                                    {
                                        CurrentFile = "提取: " + entryName,
                                        PercentComplete = Math.Min(pct, 100),
                                        FilePercentComplete = filePct
                                    });
                                    lastReportTime = now;
                                }
                            }
                        }

                        processedBytes += entrySize;
                        try { File.SetLastWriteTime(outPath, entry.LastModifiedTime ?? DateTime.MinValue); } catch { CoreLog.Trace("ZipEngine: failed to set last write time for '{0}'", outPath); }
                    }
                }

                CoreLog.Trace($"[TRACE] ZipEngine.AddToArchiveAsync: Phase 1 done, extracted {processedBytes} bytes");

                // === Phase 2: 复制新文件到临时目录 ===
                foreach (var (fullPath, entryName) in resolvedFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var outPath = Path.Combine(tempDir, entryName);
                    var outDir = Path.GetDirectoryName(outPath);
                    if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                        Directory.CreateDirectory(outDir);
                    File.Copy(fullPath, outPath, overwrite: true);
                }

                // 扫描临时目录用于压缩
                var compressFiles = new List<(string FullPath, string RelativePath)>();
                long compressTotalBytes = 0;
                foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                {
                    var relPath = Path.GetRelativePath(tempDir, file);
                    compressFiles.Add((file, relPath));
                    compressTotalBytes += new FileInfo(file).Length;
                }
                if (compressTotalBytes == 0) compressTotalBytes = 1;
                long compressProcessed = 0;

                // === Phase 3: 重压缩（字节加权平滑进度） ===
                CoreLog.Trace($"[TRACE] ZipEngine.AddToArchiveAsync: Phase 3 — recompressing {compressFiles.Count} files, {compressTotalBytes} bytes");
                using (var fsOut = File.Create(tempArchive))
                {
                    var isEncrypted = options.Encrypt && !string.IsNullOrEmpty(options.Password);

                    if (isEncrypted)
                    {
                        // SharpSevenZip 支持 ZIP + AES-256 加密
                        fsOut.Dispose();

                        SevenZipEngine.EnsureLibraryPath();

                        var s7zCompressor = new SharpSevenZipCompressor
                        {
                            ArchiveFormat = OutArchiveFormat.Zip,
                            ZipEncryptionMethod = ZipEncryptionMethod.Aes256,
                            CompressionMethod = CompressionMethod.Deflate,
                            CompressionLevel = MapCompressionLevelToS7Z(options.CompressionLevel),
                            IncludeEmptyDirectories = true,
                            DirectoryStructure = true,
                        };

                        // 所有文件在同⼀临时目录下，计算 commonRoot 以保留相对路径结构
                        var commonRoot = tempDir.Length;
                        if (!tempDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                            commonRoot++; // 包含分隔符

                        var s7zAccumPct = 0.0;
                        // 单文件进度：PercentDelta 是「当前文件」的增量，故必须从文件起点单独累计。
                        // 不能复用 s7zAccumPct（那是整包累计），否则 FilePercentComplete 会等于总进度，
                        // UI 的行内底纹就退化成整包进度条。
                        var s7zFilePct = 0.0;
                        var s7zCurrentFile = "";
                        s7zCompressor.FileCompressionStarted += (_, e) =>
                        {
                            s7zCurrentFile = e.FileName ?? "";
                            s7zFilePct = 0;
                        };
                        s7zCompressor.Compressing += (_, e) =>
                        {
                            s7zAccumPct = Math.Min(100, s7zAccumPct + e.PercentDelta);
                            s7zFilePct = Math.Min(100, s7zFilePct + e.PercentDelta);
                            var cumProcessed = processedBytes + (long)(compressTotalBytes * s7zAccumPct / 100);
                            var pct = (double)cumProcessed / workTotal * 100;
                            progress?.Report(new ArchiveProgress
                            {
                                CurrentFile = s7zCurrentFile,
                                PercentComplete = Math.Min(pct, 100),
                                FilePercentComplete = s7zFilePct,
                            });
                        };

                        var allFilePaths = compressFiles.Select(f => f.FullPath).ToArray();
                        if (allFilePaths.Length > 0)
                        {
                            s7zCompressor.CompressFilesEncrypted(
                                tempArchive, commonRoot,
                                options.Password ?? "", allFilePaths);
                        }

                        processedBytes += compressTotalBytes;

                        // ZIP 注释：SharpSevenZip 不支持压缩时写入，压缩后通过 EOCD 后写
                        if (!string.IsNullOrEmpty(options.Comment))
                        {
                            try { ZipCommentHelper.WriteComment(tempArchive, options.Comment); }
                            catch (Exception commentEx) { CoreLog.Error("AddToArchiveAsync: failed to write ZIP comment", commentEx); }
                        }
                    }
                    else
                    {
                        var zipEncoding = (options.FileNameEncoding?.ToLowerInvariant()) switch
                        {
                            "gbk" => Encoding.GetEncoding("GBK"),
                            "default" => Encoding.Default,
                            _ => Encoding.UTF8,
                        };
                        var writerOptions = new ZipWriterOptions(CompressionType.Deflate)
                        {
                            CompressionLevel = options.CompressionLevel,
                            ArchiveComment = options.Comment ?? "",
                            ArchiveEncoding = new ArchiveEncoding { Default = zipEncoding },
                        };

                        // ── MultiThreaded 自适应压缩分组 ──
                        // 必须在创建 ZipWriter 之前完成：方案 A 下最终 ZIP 以 7z 产物为准
                        // （必要时再追加 Store 组），fsOut 全程不应被 ZipWriter 触碰。
                        List<(string FullPath, string RelativePath)>? mtStoreGroup = null;
                        List<(string FullPath, string RelativePath)>? mtCompressGroup = null;
                        if (IsMultiThreadedEligible(options, compressFiles.Count, compressTotalBytes))
                        {
                            var sg = new List<(string FullPath, string RelativePath)>();
                            var cg = new List<(string FullPath, string RelativePath)>();
                            foreach (var file in compressFiles)
                            {
                                var level = ZipEntryClassifier.GetAdaptiveLevel(file.FullPath, options.CompressionLevel, options.AdaptiveCompression, options.MultiThreadedStoreFormatIds);
                                if (level == 0) sg.Add(file);
                                else cg.Add(file);
                            }

                            // 混合场景必须经 copy-mode 重写；该压缩方法不被重写器承载时整组回落串行。
                            // 不赋值 mtStoreGroup/mtCompressGroup 即让后续 if 走标准 ZipWriter 路径。
                            if (sg.Count > 0 && cg.Count > 0 && !CanCopyModeRewrite(options.ZipCompressionMethod))
                            {
                                LogCopyModeUnsupported(options.ZipCompressionMethod);
                            }
                            else
                            {
                                mtStoreGroup = sg;
                                mtCompressGroup = cg;
                            }
                        }

                        if (mtCompressGroup is { Count: > 0 })
                        {
                            // ══ 方案 A：ZIP 内容以 7z 产物为准，100% 保留其压缩字节 ══
                            // 修复前走 MergeTempZipToWriter：OpenEntryStream() 解压 →
                            // ZipWriter.WriteToStream() 用 .NET Deflate 重压，mt=on 成果被丢弃。
                            var storeGroup = mtStoreGroup!;
                            var compressGroup = mtCompressGroup;
                            var tempZip = Path.Combine(Path.GetTempPath(), $"mantiszip_mt_{Guid.NewGuid():N}.zip");
                            var storeStreams = new List<FileStream>();
                            int mtProcessedFiles = 0;
                            try
                            {
                                CoreLog.Trace($"[TRACE] AddToArchiveAsync CompressGroup: {compressGroup.Count} files");
                                foreach (var (fp, rp) in compressGroup)
                                    CoreLog.Trace($"[TRACE]   CompressGroup entry: FullPath={fp} → RelativePath={rp}");

                                CoreLog.Info($"AddToArchiveAsync: MultiThreaded mode — {storeGroup.Count} store, {compressGroup.Count} compress via 7z mt=on");
                                CompressGroupWithSevenZip(compressGroup, tempZip, options, progress, compressProcessed, compressTotalBytes, compressFiles.Count, mtProcessedFiles, null, null, ref lastReportTime);
                                compressProcessed = compressTotalBytes;

                                // fsOut 是 File.Create 出来的空文件；ZIP 内容完全来自 7z，必须先释放句柄
                                fsOut.Dispose();

                                if (storeGroup.Count == 0)
                                {
                                    // 全部可压缩：直接移动 7z 产物，一个字节都不重编码
                                    File.Move(tempZip, tempArchive, overwrite: true);
                                }
                                else
                                {
                                    // 混合：7z 产物作为 copy-mode 源，Store 组以 method 0 直存追加
                                    CoreLog.Trace($"[TRACE] AddToArchiveAsync StoreGroup: {storeGroup.Count} files");
                                    var storeEntries = new List<NewEntry>(storeGroup.Count);
                                    foreach (var (fullPath, relativePath) in storeGroup)
                                    {
                                        cancellationToken.ThrowIfCancellationRequested();
                                        var storeStream = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                                        storeStreams.Add(storeStream);
                                        storeEntries.Add(new NewEntry(
                                            EntryName: ArchivePath.Normalize(relativePath),
                                            Data: storeStream,
                                            LastModified: File.GetLastWriteTime(fullPath),
                                            Size: storeStream.Length,
                                            Store: true));
                                    }

                                    // 条目名编码固定 UTF-8：与 7z 产物保持一致（混用编码会破坏条目名）
                                    ZipBinaryRewriter.RewriteAsync(
                                        sourcePath: tempZip,
                                        destPath: tempArchive,
                                        keepEntryNames: null,
                                        addEntries: storeEntries,
                                        encoding: Encoding.UTF8,
                                        comment: options.Comment,
                                        progress: progress,
                                        cancellationToken: cancellationToken)
                                        .GetAwaiter().GetResult();
                                }

                                // ZIP 注释：SharpSevenZip 不支持压缩时写入，压缩后通过 EOCD 后写
                                if (!string.IsNullOrEmpty(options.Comment))
                                {
                                    try { ZipCommentHelper.WriteComment(tempArchive, options.Comment); }
                                    catch (Exception commentEx) { CoreLog.Error("AddToArchiveAsync: failed to write ZIP comment", commentEx); }
                                }

                                progress?.Report(new ArchiveProgress
                                {
                                    CurrentFile = string.Empty,
                                    PercentComplete = 100,
                                    FilePercentComplete = 100
                                });
                            }
                            finally
                            {
                                foreach (var s in storeStreams) { try { s.Dispose(); } catch { } }
                                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                            }
                        }
                        else
                        {
                        using var zipWriter = new ZipWriter(fsOut, writerOptions);

                        if (mtStoreGroup is not null)
                        {
                            // MT 生效但无文件需要压缩：全部 Store 直存（与既有行为一致）
                            foreach (var (fullPath, relPath) in mtStoreGroup)
                            {
                                CoreLog.Trace($"[TRACE]   StoreGroup entry: FullPath={fullPath} → relPath={relPath} → ArchivePath.Normalize={ArchivePath.Normalize(relPath)}");
                                cancellationToken.ThrowIfCancellationRequested();
                                var fi = new FileInfo(fullPath);
                                var entryPath = ArchivePath.Normalize(relPath);
                                var entryOptions = new ZipWriterEntryOptions
                                {
                                    ModificationDateTime = fi.LastWriteTime,
                                    CompressionLevel = 0, // Store
                                };
                                using (var entryStream = zipWriter.WriteToStream(entryPath, entryOptions))
                                using (var fsInput = File.OpenRead(fullPath))
                                {
                                    var buffer = new byte[CopyBufferSize];
                                    long totalRead = 0;
                                    var fiLen = fi.Length;
                                    while (totalRead < fiLen)
                                    {
                                        cancellationToken.ThrowIfCancellationRequested();
                                        var read = fsInput.Read(buffer, 0, buffer.Length);
                                        if (read <= 0) break;
                                        entryStream.Write(buffer, 0, read);
                                        totalRead += read;
                                        compressProcessed += read;
                                    }
                                }
                            }
                        }
                        else
                        {

                        foreach (var (fullPath, relPath) in compressFiles)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var fi = new FileInfo(fullPath);
                            var entryPath = ArchivePath.Normalize(relPath);
                            // 自适应压缩：已压缩文件自动 Store（仅 Deflate/Deflate64 归档适用）
                            int? entryLevel = null;
                            if (options != null && options.AdaptiveCompression && !options.Encrypt)
                            {
                                var method = options.ZipCompressionMethod?.ToLowerInvariant();
                                if (string.IsNullOrEmpty(method) || method == "deflate" || method == "deflate64")
                                {
                                    entryLevel = ZipEntryClassifier.GetAdaptiveLevel(fullPath, options.CompressionLevel, true);
                                    CoreLog.Trace("ZipEngine.CompressAsync: adaptive entry '{0}' level={1} (global={2}, adaptive={3})",
                                        entryPath, entryLevel ?? options.CompressionLevel, options.CompressionLevel, options.AdaptiveCompression);
                                }
                                else
                                {
                                    CoreLog.Trace("ZipEngine.CompressAsync: adaptive skipped for '{0}' (method={1}, not deflate)", entryPath, method);
                                }
                            }
                            var entryOptions = new ZipWriterEntryOptions
                            {
                                ModificationDateTime = fi.LastWriteTime,
                                CompressionLevel = entryLevel,
                            };

                            using (var entryStream = zipWriter.WriteToStream(entryPath, entryOptions))
                            using (var fsInput = File.OpenRead(fullPath))
                            {
                                var buffer = new byte[CopyBufferSize];
                                long totalRead = 0;
                                var fiLen = fi.Length;

                                while (totalRead < fiLen)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    var read = fsInput.Read(buffer, 0, buffer.Length);
                                    if (read <= 0) break;
                                    entryStream.Write(buffer, 0, read);
                                    totalRead += read;
                                    compressProcessed += read;

                                    var now = DateTime.Now;
                                    if (now - lastReportTime >= reportInterval || totalRead >= fiLen)
                                    {
                                        var cumProcessed = processedBytes + compressProcessed;
                                        var pct = (double)cumProcessed / workTotal * 100;
                                        var filePct = fiLen > 0 ? (double)totalRead / fiLen * 100 : 100;
                                        progress?.Report(new ArchiveProgress
                                        {
                                            CurrentFile = relPath,
                                            PercentComplete = Math.Min(pct, 100),
                                            FilePercentComplete = filePct
                                        });
                                        lastReportTime = now;
                                    }
                                }
                            }
                        }
                        }
                        }
                    }
                }

                // === Phase 4: 原子替换（带重试，应对 SharpCompress 文件句柄释放延迟） ===
                for (int retry = 0; ; retry++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        File.Delete(archivePath);
                        File.Move(tempArchive, archivePath);
                        break;
                    }
                    catch (IOException) when (retry < 5)
                    {
                        Thread.Sleep(100);
                    }
                }

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = string.Empty,
                    PercentComplete = 100,
                    FilePercentComplete = 100
                });

                CoreLog.Info($"AddToArchiveAsync: done, {newFiles.Count} files added ({oldEntryCount} old entries kept), {sw.ElapsedMilliseconds}ms");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    try { Directory.Delete(tempDir, recursive: true); } catch (Exception ex) { CoreLog.Error("AddToArchiveAsync: failed to clean up temp dir", ex); }
                if (File.Exists(tempArchive))
                    try { File.Delete(tempArchive); } catch { CoreLog.Trace("ZipEngine: failed to delete temp archive '{0}'", tempArchive); }
            }
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }

    public async Task DeleteEntriesAsync(string archivePath, string[] entryPaths, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"DeleteEntriesAsync: {archivePath}, entries=[{string.Join("; ", entryPaths)}]");
        var sw = Stopwatch.StartNew();

        await Task.Run(async () =>
        {
            var deletedSet = new HashSet<string>(entryPaths.Select(p => ArchivePath.Normalize(p)), StringComparer.OrdinalIgnoreCase);
            if (entryPaths.Length == 0)
            {
                CoreLog.Info("DeleteEntriesAsync: no entries to delete");
                return;
            }

            // ── Optimized copy-mode path (binary rewrite, no decompress-recompress) ──
            {
                string? tempArchiveFast = null;
                try
                {
                    CoreLog.Info("DeleteEntriesAsync: attempting copy-mode fast path");
                    tempArchiveFast = Path.GetTempFileName() + ".zip";

                    // Detect encoding
                    var encoding = ZipHasUtf8Flag(archivePath) ? Encoding.UTF8 : Encoding.GetEncoding("gbk");

                    // Build keep set: all entries NOT in entryPaths
                    var deletedNormalized = new HashSet<string>(entryPaths.Select(p => ArchivePath.Normalize(p)), StringComparer.OrdinalIgnoreCase);
                    var keepSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    using (var archive = OpenArchiveWithEncodingFallback(archivePath, password))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            var name = entry.Key ?? string.Empty;
                            if (!deletedNormalized.Contains(ArchivePath.Normalize(name)))
                                keepSet.Add(name);
                        }
                    }

                    if (keepSet.Count == 0)
                    {
                        // All entries to be deleted — just delete the archive
                        try { File.Delete(archivePath); } catch { }
                        CoreLog.Info("DeleteEntriesAsync: all entries deleted via copy-mode, removed archive");
                        return;
                    }

                    var result = await ZipBinaryRewriter.RewriteAsync(
                        sourcePath: archivePath,
                        destPath: tempArchiveFast,
                        keepEntryNames: keepSet,
                        addEntries: null,
                        encoding: encoding,
                        comment: null,  // preserve original comment
                        progress: progress,
                        cancellationToken: cancellationToken);

                    // Atomic replace
                    for (int retry = 0; ; retry++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            File.Delete(archivePath);
                            File.Move(tempArchiveFast, archivePath);
                            break;
                        }
                        catch (IOException) when (retry < 5)
                        {
                            Thread.Sleep(100);
                        }
                    }

                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = string.Empty,
                        PercentComplete = 100,
                        FilePercentComplete = 100
                    });

                    CoreLog.Info($"DeleteEntriesAsync: copy-mode fast path done, {result.EntriesCopied} entries kept");
                    return;
                }
                catch (ZipCopyModeException)
                {
                    CoreLog.Info("DeleteEntriesAsync: copy-mode not available, falling back to legacy path");
                    if (tempArchiveFast != null)
                    {
                        try { if (File.Exists(tempArchiveFast)) File.Delete(tempArchiveFast); } catch { }
                    }
                }
            }

            // 创建临时目录和工作文件
            var tempDir = Path.Combine(Path.GetTempPath(), "MantisZip", "DeleteTemp", Guid.NewGuid().ToString());
            var tempArchive = tempDir + ".new.zip";

            // 使用 SharpCompress IArchive 完成验证 + 确定保留项 + 提取
            long totalKeepBytes = 0;
            int keepEntryCount = 0;
            long workTotal = 1;
            long processedBytes = 0;
            var lastReportTime = DateTime.Now;
            var reportInterval = TimeSpan.FromMilliseconds(100);

            try
            {
                Directory.CreateDirectory(tempDir);

                // Pass 1: 验证 + 确定保留项
                var keepNames = new List<string>();
                using (var archive = OpenArchiveWithEncodingFallback(archivePath, password))
                {
                    var allNames = new List<string>();
                    foreach (var entry in archive.Entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var name = entry.Key ?? string.Empty;
                        allNames.Add(name);
                    }

                    // 验证要删除的条目都存在
                    var entryNameSet = new HashSet<string>(allNames);
                    foreach (var entryPath in entryPaths)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var normalized = ArchivePath.Normalize(entryPath);
                        if (!entryNameSet.Contains(normalized))
                        {
                            CoreLog.Error($"DeleteEntriesAsync: entry not found: {entryPath}");
                            throw new FileNotFoundException($"压缩包中不存在条目: {entryPath}", entryPath);
                        }
                    }

                    foreach (var name in allNames)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var normalized = ArchivePath.Normalize(name);
                        if (!deletedSet.Contains(normalized))
                        {
                            keepNames.Add(name);
                        }
                    }
                }

                keepEntryCount = keepNames.Count;
                if (keepEntryCount == 0)
                {
                    // 所有条目都被删除 — 删除原文件后返回
                    try { File.Delete(archivePath); } catch { CoreLog.Trace("ZipEngine: failed to delete empty archive '{0}'", archivePath); }
                    CoreLog.Info("DeleteEntriesAsync: all entries deleted, removed archive");
                    return;
                }

                // ── Check if source archive is encrypted ──
                bool sourceIsEncrypted = false;
                using (var checkArchive = OpenArchiveWithEncodingFallback(archivePath, password))
                {
                    sourceIsEncrypted = checkArchive.Entries.Any(e => e.IsEncrypted);
                }

                // Pass 2: 提取保留条目到临时目录（带进度）
                using (var archive = OpenArchiveWithEncodingFallback(archivePath, password))
                {
                    // 先算 totalKeepBytes
                    foreach (var entry in archive.Entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (entry.IsDirectory) continue;
                        var name = entry.Key ?? string.Empty;
                        if (keepNames.Contains(name))
                            totalKeepBytes += entry.Size;
                    }
                }

                workTotal = totalKeepBytes + totalKeepBytes;
                if (workTotal == 0) workTotal = 1;

                // Pass 3: 实际提取
                using (var archive = OpenArchiveWithEncodingFallback(archivePath, password))
                {
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = "正在提取保留条目...",
                        PercentComplete = 0
                    });

                    foreach (var entry in archive.Entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var entryName = entry.Key ?? string.Empty;

                        if (!keepNames.Contains(entryName))
                            continue;

                        if (entry.IsDirectory)
                        {
                            var dirPath = Path.Combine(tempDir, entryName);
                            if (!Directory.Exists(dirPath))
                                Directory.CreateDirectory(dirPath);
                            continue;
                        }

                        var outPath = Path.Combine(tempDir, entryName);
                        var outDir = Path.GetDirectoryName(outPath);
                        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                            Directory.CreateDirectory(outDir);

                        var entrySize = entry.Size;
                        using (var entryStream = entry.OpenEntryStream())
                        using (var outStream = File.Create(outPath))
                        {
                            var buffer = new byte[CopyBufferSize];
                            long entryProcessed = 0;
                            while (true)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var read = entryStream.Read(buffer, 0, buffer.Length);
                                if (read <= 0) break;
                                outStream.Write(buffer, 0, read);
                                entryProcessed += read;

                                var now = DateTime.Now;
                                if (now - lastReportTime >= reportInterval || entryProcessed >= entrySize)
                                {
                                    var pct = (double)(processedBytes + entryProcessed) / workTotal * 100;
                                    var filePct = entrySize > 0 ? (double)entryProcessed / entrySize * 100 : 100;
                                    progress?.Report(new ArchiveProgress
                                    {
                                        CurrentFile = "提取: " + entryName,
                                        PercentComplete = Math.Min(pct, 100),
                                        FilePercentComplete = filePct
                                    });
                                    lastReportTime = now;
                                }
                            }
                        }

                        processedBytes += entrySize;
                        try { File.SetLastWriteTime(outPath, entry.LastModifiedTime ?? DateTime.MinValue); } catch { CoreLog.Trace("ZipEngine: failed to set last write time for '{0}'", outPath); }
                    }
                }

                // === Phase 2: 重压缩保留条目 ===
                var compressFiles = new List<(string FullPath, string RelativePath)>();
                long compressTotalBytes = 0;
                foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                {
                    var relPath = Path.GetRelativePath(tempDir, file);
                    compressFiles.Add((file, relPath));
                    compressTotalBytes += new FileInfo(file).Length;
                }
                if (compressTotalBytes == 0) compressTotalBytes = 1;
                long compressProcessed = 0;

                CoreLog.Trace($"[TRACE] ZipEngine.DeleteEntriesAsync: Phase 2 — recompressing {compressFiles.Count} files, {compressTotalBytes} bytes");

                if (sourceIsEncrypted && !string.IsNullOrEmpty(password))
                {
                    // SharpSevenZip 加密路径
                    using (var fsOut = File.Create(tempArchive))
                    {
                        fsOut.Dispose();

                        SevenZipEngine.EnsureLibraryPath();

                        var s7zCompressor = new SharpSevenZipCompressor
                        {
                            ArchiveFormat = OutArchiveFormat.Zip,
                            ZipEncryptionMethod = ZipEncryptionMethod.Aes256,
                            CompressionMethod = CompressionMethod.Deflate,
                            CompressionLevel = SharpSevenZip.CompressionLevel.Normal,
                            IncludeEmptyDirectories = true,
                            DirectoryStructure = true,
                        };

                        var s7zAccumPct = 0.0;
                        // 单文件进度：PercentDelta 是「当前文件」的增量，故必须从文件起点单独累计。
                        // 不能复用 s7zAccumPct（那是整包累计），否则 FilePercentComplete 会等于总进度，
                        // UI 的行内底纹就退化成整包进度条。
                        var s7zFilePct = 0.0;
                        var s7zCurrentFile = "";
                        s7zCompressor.FileCompressionStarted += (_, e) =>
                        {
                            s7zCurrentFile = e.FileName ?? "";
                            s7zFilePct = 0;
                        };
                        s7zCompressor.Compressing += (_, e) =>
                        {
                            s7zAccumPct = Math.Min(100, s7zAccumPct + e.PercentDelta);
                            s7zFilePct = Math.Min(100, s7zFilePct + e.PercentDelta);
                            var cumProcessed = processedBytes + (long)(compressTotalBytes * s7zAccumPct / 100);
                            var pct = (double)cumProcessed / workTotal * 100;
                            progress?.Report(new ArchiveProgress
                            {
                                CurrentFile = s7zCurrentFile,
                                PercentComplete = Math.Min(pct, 100),
                                FilePercentComplete = s7zFilePct,
                            });
                        };

                        var commonRoot = tempDir.Length;
                        if (!tempDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                            commonRoot++;

                        var allFilePaths = compressFiles.Select(f => f.FullPath).ToArray();
                        if (allFilePaths.Length > 0)
                        {
                            s7zCompressor.CompressFilesEncrypted(
                                tempArchive, commonRoot,
                                password ?? "", allFilePaths);
                        }
                    }
                }
                else
                {
                    // 非加密路径：SharpCompress ZipWriter（原实现）
                    using (var fsOut = File.Create(tempArchive))
                    {
                        var writerOptions = new ZipWriterOptions(CompressionType.Deflate)
                        {
                            CompressionLevel = 6,
                            ArchiveEncoding = new ArchiveEncoding { Default = Encoding.UTF8 },
                        };
                        using var zipWriter = new ZipWriter(fsOut, writerOptions);

                        foreach (var (fullPath, relPath) in compressFiles)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var fi = new FileInfo(fullPath);
                            var entryPath = ArchivePath.Normalize(relPath);
                            var entryOptions = new ZipWriterEntryOptions
                            {
                                ModificationDateTime = fi.LastWriteTime,
                            };

                            using (var entryStream = zipWriter.WriteToStream(entryPath, entryOptions))
                            using (var fsInput = File.OpenRead(fullPath))
                            {
                                var buffer = new byte[CopyBufferSize];
                                long totalRead = 0;
                                var fiLen = fi.Length;

                                while (totalRead < fiLen)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    var read = fsInput.Read(buffer, 0, buffer.Length);
                                    if (read <= 0) break;
                                    entryStream.Write(buffer, 0, read);
                                    totalRead += read;
                                    compressProcessed += read;

                                    var now = DateTime.Now;
                                    if (now - lastReportTime >= reportInterval || totalRead >= fiLen)
                                    {
                                        var cumProcessed = processedBytes + compressProcessed;
                                        var pct = (double)cumProcessed / workTotal * 100;
                                        var filePct = fiLen > 0 ? (double)totalRead / fiLen * 100 : 100;
                                        progress?.Report(new ArchiveProgress
                                        {
                                            CurrentFile = relPath,
                                            PercentComplete = Math.Min(pct, 100),
                                            FilePercentComplete = filePct
                                        });
                                        lastReportTime = now;
                                    }
                                }
                            }
                        }
                    }
                }

                // === Phase 3: 原子替换（带重试，应对 SharpCompress 文件句柄释放延迟） ===
                for (int retry = 0; ; retry++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        File.Delete(archivePath);
                        File.Move(tempArchive, archivePath);
                        break;
                    }
                    catch (IOException) when (retry < 5)
                    {
                        Thread.Sleep(100);
                    }
                }

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = string.Empty,
                    PercentComplete = 100,
                    FilePercentComplete = 100
                });

                CoreLog.Info($"DeleteEntriesAsync: done, {entryPaths.Length} entries deleted ({keepEntryCount} kept), {sw.ElapsedMilliseconds}ms");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    try { Directory.Delete(tempDir, recursive: true); } catch { CoreLog.Trace("ZipEngine: failed to delete temp dir '{0}'", tempDir); }
                if (File.Exists(tempArchive))
                    try { File.Delete(tempArchive); } catch { CoreLog.Trace("ZipEngine: failed to delete temp archive '{0}'", tempArchive); }
            }
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
    }

    // ReadFileWithRetryZipOutputStream 已删除（SharpZipLib 加密回退已被 SharpSevenZip 替代）

    /// <summary>
    /// 带重试/跳过/中止的文件压缩读取（SharpCompress ZipWriter 路径）。
    /// 使用 WriteToStream 获取可写流以支持字节加权进度报告。
    /// 返回 false 表示跳过此文件。
    /// </summary>
    private bool ReadFileWithRetry(string fullPath, string relativePath,
        ArchiveOptions options, ZipWriter zipWriter, ref long processedBytes, long totalBytes,
        int totalFiles, ref int processedFiles,
        CancellationToken ct, IProgress<ArchiveProgress>? progress, ref DateTime lastReportTime)
    {
        int retries = 3;
        while (retries > 0)
        {
            try
            {
                var fi = new FileInfo(fullPath);
                // 自适应压缩：已压缩文件自动 Store（仅 Deflate/Deflate64 归档适用）。
                // 必须用 4 参重载（传入 MultiThreadedStoreFormatIds）而非 3 参：
                //   · 3 参仅查内置已压缩扩展名列表；4 参额外合并 MultiThreadedStoreFormatIds 自定义列表。
                //   · MT 模式的分拣（storeGroup/compressGroup 分流）在 ZIP 条目级别用 4 参判定；
                //     若此处写入阶段用 3 参重算，自定义列表命中的文件（如 .wav）会被降级重算为
                //     level>0 → 实际 Deflate 压缩 —— 分拣说 Store、写入却 Deflate，自打脸。
                //   · 非 MT 的普通自适应路径同样经此方法，4 参在无自定义列表时行为与 3 参完全一致，
                //     故统一用 4 参无副作用。
                int? entryLevel = null;
                if (options != null && options.AdaptiveCompression && !options.Encrypt)
                {
                    var method = options.ZipCompressionMethod?.ToLowerInvariant();
                    if (string.IsNullOrEmpty(method) || method == "deflate" || method == "deflate64")
                    {
                        entryLevel = ZipEntryClassifier.GetAdaptiveLevel(fullPath, options.CompressionLevel, true, options.MultiThreadedStoreFormatIds);
                        CoreLog.Trace("ZipEngine.AddToArchiveAsync: adaptive entry '{0}' level={1} (global={2}, adaptive={3})",
                            relativePath, entryLevel ?? options.CompressionLevel, options.CompressionLevel, options.AdaptiveCompression);
                    }
                    else
                    {
                        CoreLog.Trace("ZipEngine.AddToArchiveAsync: adaptive skipped for '{0}' (method={1}, not deflate)", relativePath, method);
                    }
                }
                var entryOptions = new ZipWriterEntryOptions
                {
                    ModificationDateTime = fi.LastWriteTime,
                    CompressionLevel = entryLevel,
                };

                var entryPath = ArchivePath.Normalize(relativePath);
                using (var entryStream = zipWriter.WriteToStream(entryPath, entryOptions))
                // 共享读：源文件可能正被 Word 等编辑器以写权限持有，File.OpenRead 会直接冲突
                using (var fsInput = SharedReadStream.OpenRead(fullPath))
                {
                    var buffer = new byte[CopyBufferSize];
                    long totalRead = 0;
                    var fiLen = fi.Length;

                    while (totalRead < fiLen)
                    {
                        ct.ThrowIfCancellationRequested();
                        var read = fsInput.Read(buffer, 0, buffer.Length);
                        if (read <= 0) break;
                        entryStream.Write(buffer, 0, read);
                        totalRead += read;
                        processedBytes += read;

                        var now = DateTime.Now;
                        if (now - lastReportTime >= TimeSpan.FromMilliseconds(100) || totalRead >= fiLen)
                        {
                            var pct = totalBytes > 0 ? (double)processedBytes / totalBytes * 100 : 0;
                            var filePct = fiLen > 0 ? (double)totalRead / fiLen * 100 : 100;
                            progress?.Report(new ArchiveProgress
                            {
                                CurrentFile = relativePath,
                                PercentComplete = pct,
                                FilePercentComplete = filePct,
                                TotalFiles = totalFiles,
                                ProcessedFiles = processedFiles
                            });
                            lastReportTime = now;
                        }
                    }
                }
                processedFiles++;
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                retries--;
                if (options?.ErrorResolver == null)
                {
                    if (retries <= 0) throw;
                    continue;
                }

                var action = options.ErrorResolver(new FileErrorInfo
                {
                    FilePath = fullPath,
                    ErrorMessage = ex.Message,
                    RetriesRemaining = retries
                });

                if (action == FileErrorAction.Retry)
                {
                    continue;
                }
                if (action == FileErrorAction.Skip)
                {
                    return false;
                }
                throw;
            }
        }
        return false;
    }

    /// <summary>
    /// 把待压文件按大小贪心分入<paramref name="degree"/> 组（LPT / longest-processing-time-first）。
    /// <para>
    /// 每轮取当前<b>未分配最大</b>的文件放入<b>累计字节最小</b>的组，
    /// 使最重文件尽早开工、避免长尾残留。空组在返回前被剔除，
    /// 因此返回的组数可能小于 <paramref name="degree"/>（调用方须以返回值为准）。
    /// </para>
    /// </summary>
    /// <param name="files">待分组文件。</param>
    /// <param name="degree">期望组数，&lt;= 1 时返回单组。</param>
    internal static List<List<(string FullPath, string RelativePath)>> SplitCompressGroup(
        IReadOnlyList<(string FullPath, string RelativePath)> files,
        int degree)
    {
        var result = new List<List<(string FullPath, string RelativePath)>>();
        if (files.Count == 0) return result;

        if (degree <= 1)
        {
            result.Add(files.ToList());
            return result;
        }

        var bins = new List<(long Bytes, List<(string, string)> Items)>();
        for (int i = 0; i < degree; i++)
            bins.Add((0L, new List<(string, string)>()));

        // 待分配集合按文件大小降序；同大小按路径序，保证结果确定性
        var pending = files
            .Select((f, idx) => (Item: f, Idx: idx, Size: SafeSize(f.FullPath)))
            .OrderByDescending(x => x.Size)
            .ThenBy(x => x.Item.FullPath, StringComparer.Ordinal)
            .ToList();

        foreach (var p in pending)
        {
            int target = 0;
            for (int i = 1; i < bins.Count; i++)
                if (bins[i].Bytes < bins[target].Bytes) target = i;

            bins[target].Items.Add(p.Item);
            bins[target] = (bins[target].Bytes + p.Size, bins[target].Items);
        }

        foreach (var b in bins)
            if (b.Items.Count > 0) result.Add(b.Items);

        return result;

        static long SafeSize(string path)
        {
            try { return new FileInfo(path).Length; }
            catch { return 0L; }
        }
    }

    /// <summary>
    /// MultiThreaded（7z mt=on）路径<b>前置</b>准入判定，由 <c>CompressAsync</c> 与
    /// <c>AddToArchiveAsync</c> 共用，避免两处准入条件漂移。只检查与分组/压缩方法无关的硬性条件：
    /// 开关本身、加密、分卷、ZIP32 规模上限。
    /// <para>
    /// 压缩方法（Deflate64 等）不在此处判定：7z 产物只有在<b>混合场景</b>下才会经过
    /// <see cref="ZipBinaryRewriter"/> copy-mode 重写，全可压缩场景直接 <c>File.Move</c>。
    /// 因此方法判定必须等分组完成后由 <see cref="CanCopyModeRewrite"/> 负责，
    /// 否则会误伤「Deflate64 + 全可压缩」这类本可正常走 7z 的组合。
    /// </para>
    /// </summary>
    /// <param name="options">压缩选项。</param>
    /// <param name="totalEntryCount">本次压缩将产生的文件条目总数（含 Store 组）。</param>
    /// <param name="totalSize">全部待压缩字节总和。</param>
    /// <returns>true 表示允许进入 7z mt=on 路径；false 表示应回落到标准串行路径。</returns>
    private static bool IsMultiThreadedEligible(ArchiveOptions options, int totalEntryCount, long totalSize)
    {
        if (!options.MultiThreadedCompression)
            return false;

        // 加密包由 SharpCompress ZipWriter 负责 AES，7z 阶段无法接管
        if (options.Encrypt && !string.IsNullOrEmpty(options.Password))
            return false;

        // 分卷依赖 SplitOutputStream 包装，MT 路径不参与
        if (options.SplitSize > 0)
        {
            CoreLog.Info("ZipEngine: MultiThreaded skipped — split archive (SplitSize>0) is handled by the serial path");
            return false;
        }

        // ZIP32 上限：条目数上限 65535，留出目录条目余量
        const int MaxZip32Entries = ushort.MaxValue - 256;
        if (totalEntryCount > MaxZip32Entries)
        {
            CoreLog.Info($"ZipEngine: MultiThreaded skipped — {totalEntryCount} entries exceed ZIP32 limit ({MaxZip32Entries})");
            return false;
        }

        // 总量 < 4GB 即可保证单条目也 < 4GB（deflate/store 输出不会大于输入），
        // 故无需逐文件 stat，避免大批量场景额外的 IO 开销
        const long MaxZip32Size = uint.MaxValue - 1L;
        if (totalSize >= MaxZip32Size)
        {
            CoreLog.Info($"ZipEngine: MultiThreaded skipped — total size {totalSize} exceeds ZIP32 limit ({MaxZip32Size})");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 判定所选 ZIP 压缩方法能否被 <see cref="ZipBinaryRewriter"/> 的 copy-mode 重写承载。
    /// <para>
    /// 仅在<b>混合场景</b>（StoreGroup 非空）下才会调用 copy-mode 重写：重写器逐条目原样复制
    /// 压缩数据（LFH + 压缩字节 + CDFH），从不解压也从不重压，因此方法码与复制逻辑无关，
    /// 只额外拒绝加密与 ZIP64（两者各有独立校验）。
    /// </para>
    /// <para>
    /// <b>deflate64 必须在此放行</b>：SharpCompress 的 ZipWriter 根本无法写 Deflate64
    /// （<c>ZipWriter.ToZipCompressionMethod</c> 无该映射，抛
    /// <see cref="SharpCompress.Common.InvalidFormatException"/>）。一旦 Deflate64 落入混合场景
    /// 并回落到串行路径，就必然硬失败——copy-mode 是它唯一可用的产出路径。
    /// </para>
    /// <para>
    /// BZip2(12)/LZMA(14)/PPMd(98) 暂不放开：它们回落串行后可由 SharpCompress 正常写出
    /// （级别已在 <c>ZipWriterOptions</c> 处降为 0），功能可用，只是失去 MT 加速；
    /// 在没有 method 9 级别的实测证据前不贸然放行。
    /// </para>
    /// </summary>
    /// <param name="zipCompressionMethod">用户选择的 ZIP 压缩方法（可为空）。</param>
    /// <returns>true 表示该方法可被 copy-mode 重写承载。</returns>
    private static bool CanCopyModeRewrite(string? zipCompressionMethod)
    {
        if (string.IsNullOrEmpty(zipCompressionMethod))
            return true; // 未指定 → 7z 默认 Deflate (method 8)

        var method = zipCompressionMethod.ToLowerInvariant();

        // store：全 Store 时 compressGroup 本就为空（自适应分类），此处放行以覆盖
        // AdaptiveCompression=false 的显式 store 场景（7z 产出 method 0，重写器可承载）
        // deflate64：SharpCompress 无法写 Deflate64，混合场景只能靠 copy-mode 承载（见上文）
        return method is "deflate" or "deflate64" or "store";
    }

    /// <summary>
    /// 记录「MT 因压缩方法不可 copy-mode 重写而跳过」的原因。
    /// </summary>
    private static void LogCopyModeUnsupported(string? zipCompressionMethod) =>
        CoreLog.Info(
            $"ZipEngine: MultiThreaded skipped — method '{zipCompressionMethod}' cannot be carried by copy-mode rewrite " +
            "(mixed Store/Compress only); using serial path");

    /// <summary>
    /// 使用 SharpSevenZip 多线程压缩一组文件到临时 ZIP。
    /// 用于 MultiThreadedCompression 模式：将需要压缩的文件通过 7z.dll mt=on 多线程压缩。
    /// </summary>
    /// <param name="files">待压缩的文件列表（FullPath + RelativePath）。</param>
    /// <param name="tempPath">临时 ZIP 输出路径。</param>
    /// <param name="options">压缩选项（压缩级别、方法等）。</param>
    /// <param name="progress">进度报告回调（可为 null）。</param>
    /// <param name="storeProcessedBytes">StoreGroup 已处理字节数（全局进度基线）。</param>
    /// <param name="totalBytes">全部文件总字节数（全局进度分母）。</param>
    /// <param name="totalFiles">全部文件总数。</param>
    /// <param name="storeProcessedFiles">StoreGroup 已处理文件数（全局进度基线）。</param>
    /// <param name="lastReportTime">上次进度报告时间（节流用，引用传递）。</param>
    internal static void CompressGroupWithSevenZip(
        List<(string FullPath, string RelativePath)> files,
        string tempPath,
        ArchiveOptions options,
        IProgress<ArchiveProgress>? progress,
        long storeProcessedBytes,
        long totalBytes,
        int totalFiles,
        int storeProcessedFiles,
        int? batchIndex,          // 组索引；null = 旧单组路径
        int? batchCount,          // 有效组数
        ref DateTime lastReportTime)
    {
        SevenZipEngine.EnsureLibraryPath();

        var compr = new SharpSevenZipCompressor
        {
            ArchiveFormat = OutArchiveFormat.Zip,
            CompressionLevel = MapCompressionLevelToS7Z(options.CompressionLevel),
            IncludeEmptyDirectories = true,
            DirectoryStructure = true,
        };

        // 多线程压缩：利用多核 CPU 并行压缩
        // mt 由调用方决定：N 组并行路径每组一个压缩器，并行度来自组间，
        // 组内必须 mt=off 以免 N × cores 过度订阅；旧单组路径保持 mt=on。
        compr.CustomParameters["mt"] = batchIndex.HasValue ? "off" : "on";

        // 处理 ZIP 压缩方法
compr.CompressionMethod = MapZipMethodToS7Z(options.ZipCompressionMethod);

        // 7z mt=on 多线程压缩时事件可能并发触发，用锁保护计数与节流，
        // 避免此前「同步调用 + 无进度事件」导致的进度条长时间停滞。
        var reportLock = new object();
        // 逐条目状态（D2）：待完成条目键 FIFO 队列（按镜像枚举顺序预填充，Finished 事件出队上报）
        var pendingEntryKeys = new Queue<(string Key, long Size)>();
        int startedFiles = 0;
        long completedBytes = 0;
        int completedFiles = 0;
        var localLastReportTime = lastReportTime; // 拷贝 ref 参数供 lambda 捕获

        // Started 只用于刷新「当前文件名」，绝不推进百分比：7z mt=on 下所有文件几乎同时开始，
        // 若把「已开始」的文件字节计入进度，进度条会瞬间抢跑到接近 100%（实测混合语料抢跑 43 个百分点）。
        compr.FileCompressionStarted += (_, e) =>
        {
            var name = e.FileName ?? "";
            lock (reportLock) { startedFiles++; }

            var now = DateTime.Now;
            if (now - localLastReportTime < TimeSpan.FromMilliseconds(100)) return;
            localLastReportTime = now;

            double pct; long doneBytes; int doneFiles;
            lock (reportLock)
            {
                doneBytes = storeProcessedBytes + completedBytes;
                doneFiles = storeProcessedFiles + completedFiles;
                pct = totalBytes > 0
                    ? Math.Min(100, (double)doneBytes / totalBytes * 100)
                    : (totalFiles > 0 ? (double)doneFiles / totalFiles * 100 : 0);
            }

            // 锁内只拷贝共享变量，释放锁后再上报，避免锁竞争（AGENTS.md：锁内 Report 曾致 25x 回退）
            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.IsNullOrEmpty(name) ? "" : Path.GetFileName(name),
                PercentComplete = pct,
                FilePercentComplete = null,
                TotalBytes = totalBytes,
                ProcessedBytes = doneBytes,
                TotalFiles = totalFiles,
                ProcessedFiles = doneFiles,
                BatchIndex = batchIndex,
                BatchCount = batchCount,
            });
        };

        // 【MT 路径无中段进度 —— 已实测确认，不要再尝试接Compressing】
        // 探针实测（60 文件 / 180MB / CompressDirectory / mt=on / SharpSevenZip 2.0.45）：
        //     started=60  compressing=0  finished=60   elapsed=1295ms
        // 7z 在 mt=on 下**完全不触发** Compressing 事件（PercentDelta 恒为 0），
        // 只有 FileCompressionStarted / FileCompressionFinished。因此本路径的中段进度
        // 只能来自 Started（100ms 节流），无法再细分 —— 这是 7z 绑定的限制，非本引擎缺陷。
        //
        // FilePercentComplete 恒为 null 的原因同理：mt=on 时所有文件几乎同时开始，
        // 字节无法归因到某个「当前文件」；且 Finished 事件不带文件名（故上方需 FIFO 队列）。
        // 若改为「把已开始的字节计入进度」，进度条会瞬间抢跑（实测混合语料 43pt）。
        //
        // 逐条目状态（D2）：7z 每完成一个文件触发 Finished（事件无文件名），按预填 FIFO 顺序出队。
        // 百分比按【已完成】字节推进（单调、不抢跑）；首个完成必定上报，保证进度条有中间态。
        // 出队在 reportLock 内，Report 在锁外，避免锁竞争（AGENTS.md：锁内 Report 曾致 25x 回退）
        compr.FileCompressionFinished += (_, _) =>
        {
            string? finishedEntryKey; long finishedSize;
            ArchiveProgress? completedReport = null;

            lock (reportLock)
            {
                if (pendingEntryKeys.Count == 0) return;
                (finishedEntryKey, finishedSize) = pendingEntryKeys.Dequeue();
                completedFiles++;
                completedBytes += finishedSize;

                var now = DateTime.Now;
                if (completedFiles > 1 && now - localLastReportTime < TimeSpan.FromMilliseconds(100)) return;
                localLastReportTime = now;

                var doneBytes = storeProcessedBytes + completedBytes;
                var doneFiles = storeProcessedFiles + completedFiles;
                completedReport = new ArchiveProgress
                {
                    EntryKey = finishedEntryKey,
                    EntryStatus = ArchiveEntryStatus.Completed,
                    CurrentFile = Path.GetFileName(finishedEntryKey),
                    PercentComplete = totalBytes > 0
                        ? Math.Min(100, (double)doneBytes / totalBytes * 100)
                        : (totalFiles > 0 ? (double)doneFiles / totalFiles * 100 : 0),
                    FilePercentComplete = null,
                    TotalBytes = totalBytes,
                    ProcessedBytes = doneBytes,
                    TotalFiles = totalFiles,
                    ProcessedFiles = doneFiles,
                    BatchIndex = batchIndex,
                    BatchCount = batchCount,
                };
            }

            if (!string.IsNullOrEmpty(finishedEntryKey))
                progress?.Report(completedReport);
        };

        // ── 路径修复：7z CompressFilesEncrypted 会剥离所有输入文件的最长公共前缀，
        //    当 CompressGroup 文件全在同一子目录时，整个目录被剥掉，条目只剩文件名。
        //    解决方案：创建镜像目录结构的临时目录，用 CompressDirectory 压缩——
        //    7z 保留目录内相对路径（如 testpath/文本/.gitignore）。
        var tempDir = Path.Combine(Path.GetTempPath(), $"mantiszip_mt_dir_{Guid.NewGuid():N}");
        try
        {
            foreach (var (fullPath, relativePath) in files)
            {
                var destPath = Path.Combine(tempDir, relativePath);
                var destDir = Path.GetDirectoryName(destPath)!;
                Directory.CreateDirectory(destDir);
                File.Copy(fullPath, destPath, overwrite: true);
            }

            var mirrorFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
            var mirrorCount = mirrorFiles.Length;
            CoreLog.Trace($"[TRACE] CompressGroupWithSevenZip: {mirrorCount} mirror files, tempDir={tempDir}, tempZip={tempPath}");

            if (mirrorCount > 0)
            {
                // 逐条目状态（D2）：按 GetFiles 枚举顺序（= 7z 压缩顺序，实测一致）预填 FIFO 队列，
                // FileCompressionFinished 事件逐个出队上报 Completed
                var relativePathByMirrorPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (_, relativePath) in files)
                {
                    relativePathByMirrorPath[Path.GetFullPath(Path.Combine(tempDir, relativePath))] = relativePath;
                }
                foreach (var mirrorPath in mirrorFiles)
                {
                    if (relativePathByMirrorPath.TryGetValue(mirrorPath, out var relativePath))
                    {
                        // 预填条目大小：Finished 事件按此 FIFO 出队以累计「已完成」字节
                        long mirrorSize = 0;
                        try { mirrorSize = new FileInfo(mirrorPath).Length; } catch { /* 读不到大小则按 0 贡献 */ }
                        pendingEntryKeys.Enqueue((relativePath, mirrorSize));
                    }
                }

                compr.CompressDirectory(tempDir, tempPath);
            }
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }

        // 打印 7z 生成的临时 ZIP 中的条目路径，用于排查路径翻倍问题
        try
        {
            using var verifyArchive = ZipArchive.OpenArchive(tempPath);
            foreach (var e in verifyArchive.Entries.Where(e => !e.IsDirectory))
                CoreLog.Trace($"[TRACE]   7z tempZip entry: Key={e.Key} → Normalize={ArchivePath.Normalize(e.Key ?? "")}");
        }
        catch (Exception ex) { CoreLog.Trace($"[TRACE]   7z tempZip verify failed: {ex.Message}"); }

        lastReportTime = localLastReportTime; // 写回 ref 参数
    }

    /// <summary>
    /// 解析有效并行组数：&lt;=0 取 CPU 数，上限 16，下限 1。
    /// 与解压侧 <c>ParallelExtractDegree</c> 的解析规则保持一致。
    /// </summary>
    internal static int ResolveParallelCompressDegree(int configured)
    {
        int n = configured;
        if (n <= 0) n = Environment.ProcessorCount;
        if (n > 16) n = 16;
        if (n < 1) n = 1;
        return n;
    }

    /// <summary>
    /// 取文件字节数；文件不存在/无权限时返回 0（进度计算用，不应中断压缩）。
    /// </summary>
    private static long SafeFileSize(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0L; }
    }

    /// <summary>
    /// 立即执行的 <see cref="IProgress{T}"/>：不像 <see cref="Progress{T}"/> 那样
    /// 投递到同步上下文，因此在 <c>Parallel.ForEachAsync</c> 的后台线程里也能即时上报。
    /// </summary>
    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    /// <summary>
    /// 使用二进制解析直接读取 ZIP 中央目录的通用位标记，
    /// 验证是否有任何条目的加密位（bit 0）被设置。
    /// 用于交叉校验 SharpCompress 报告的 IsEncrypted，防范假阳性。
    /// </summary>
    internal static bool VerifyZipEncryptionFlags(string archivePath)
    {
        try
        {
            using var fs = File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var (cdOffset, entryCount, _) = ZipBinaryRewriter.ReadEocd(fs);
            var entries = ZipBinaryRewriter.ReadCentralDirectory(fs, cdOffset, entryCount);
            bool encrypted = entries.Any(e => (e.Flags & 0x0001) != 0);
            CoreLog.Trace("VerifyZipEncryptionFlags: {0} entries, encrypted={1}", entryCount, encrypted);
            return encrypted;
        }
        catch (Exception ex)
        {
            // Zip64 或其它无法解析的情况 → 保守信任 SharpCompress
            CoreLog.Trace("VerifyZipEncryptionFlags: failed to verify (will trust SharpCompress): {0}", ex.Message);
            return true;
        }
    }
}
