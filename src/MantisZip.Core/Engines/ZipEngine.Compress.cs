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
/// ZipEngine 的 压缩族部分（CompressAsync + 分卷清理 + 编码映射，自 ZipEngine.cs 拆分，纯移动）。
/// </summary>
public partial class ZipEngine
{
    public async Task CompressAsync(string[] sourcePaths, string outputPath, ArchiveOptions options, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        CoreLog.Entry();
        CoreLog.Info($"CompressAsync: [{string.Join("; ", sourcePaths)}] -> {outputPath}, level={options.CompressionLevel}, split={options.SplitSize}");
        var sw = Stopwatch.StartNew();

        // async lambda：N 组并行分支内部需 await Parallel.ForEachAsync / RewriteAsync
        await Task.Run(async () =>
        {
            // 阶段 1：扫描源目录（总数未知 → UI 显「正在扫描文件…」+ 不定进度条 + 已发现计数）
            progress.ReportPhase(ArchivePhase.Scanning);

            // 收集所有文件（使用 FileScanner 共享工具，边发现边报告进度）
            var (files, totalBytes) = FileScanner.CollectFiles(sourcePaths, progress, cancellationToken, options.FileWhitelist);

            if (files.Count == 0)
            {
                CoreLog.Info("CompressAsync: no files to compress, returning");
                return;
            }

            CoreLog.Info($"CompressAsync: {files.Count} files to compress, {totalBytes} bytes total");

            // 阶段 2：正式写出压缩数据
            progress.ReportPhase(ArchivePhase.Compressing);

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

                    // 与 MT 路径一致：7z 默认按系统 ANSI 写 ZIP 条目名（中文 Windows 上为 GBK/bit0），
                    // 会让默认 UTF-8 选择静默失效；显式 GBK 则保留 7z 的系统 ANSI 行为。
                    if (!string.Equals(options.FileNameEncoding, "gbk", StringComparison.OrdinalIgnoreCase))
                        s7zCompressor.CustomParameters["cu"] = "on";

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
                    // 剔除的同时经 CreateSkippedReporter 上报逐条目 Skipped + 原因（spec §5.3）。
                    var validated = ReadErrorHandler.FilterUnreadableFiles(sourceFilePaths, options, cancellationToken,
                        ReadErrorHandler.CreateSkippedReporter(progress, sourceFilePaths));
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
                    // Deflate64 且全部条目 Store 时无处可压，会回落到串行 ZipWriter；
                    // 但 ZipWriter 连 Store 条目也无法用 Deflate64 writer 构造（method 9 无映射），
                    // 故此处标记改用 None writer —— 反正没有任何 Deflate64 压缩数据需要写。
                    bool deflate64StoreOnly = false;
                    if (IsMultiThreadedEligible(options, files.Count, totalBytes))
                    {
                        // MT 的镜像拷贝 / Store 合并不走 ReadFileWithRetry，无法逐文件弹「重试/跳过/中止」，
                        // 且镜像用 FileShare.Read 比串行 SharedReadStream 更严格 —— 源文件被编辑器占用时
                        // 会直接让整次压缩失败（串行则会弹窗并跳过）。这里先按串行同款语义做一次可读性
                        // 预检：不可读文件经 ErrorResolver 处理后剔除，并上报 Skipped。
                        var readablePaths = ReadErrorHandler.FilterUnreadableFiles(
                            files.Select(f => f.FullPath), options, cancellationToken,
                            ReadErrorHandler.CreateSkippedReporter(progress, sourcePaths));
                        if (readablePaths.Count != files.Count)
                        {
                            int removed = files.Count - readablePaths.Count;
                            var readableSet = new HashSet<string>(readablePaths, StringComparer.OrdinalIgnoreCase);
                            files = files.Where(f => readableSet.Contains(f.FullPath)).ToList();
                            totalFiles = files.Count;
                            totalBytes = files.Sum(f => SafeFileSize(f.FullPath));
                            CoreLog.Info($"CompressAsync: MT pre-check removed {removed} unreadable file(s), {files.Count} left");
                        }

                        var storeGroup = new List<(string FullPath, string RelativePath)>();
                        var compressGroup = new List<(string FullPath, string RelativePath)>();
                        foreach (var file in files)
                        {
                            var level = AdaptiveRuleMatcher.ResolveLevel(file.FullPath, options.CompressionLevel, options.AdaptiveCompression, options.AdaptiveSmartDetect, options.AdaptiveOverrides, options.CustomFormats);
                            if (level == 0) storeGroup.Add(file);
                            else compressGroup.Add(file);
                        }

                        CoreLog.Trace($"[TRACE] CompressAsync StoreGroup: {storeGroup.Count}, CompressGroup: {compressGroup.Count}");

                        // 全部条目都是 Store → 无可压缩内容，7z 阶段没有意义。
                        // 回落到标准串行路径（ReadFileWithRetry 内部同样应用自适应分类）。
                        if (compressGroup.Count == 0)
                        {
                            deflate64StoreOnly = string.Equals(options.ZipCompressionMethod, "deflate64", StringComparison.OrdinalIgnoreCase);
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

                            // ── N 组并行分支 ──
                            // 复用既有回退判定（IsMultiThreadedEligible 的 7 条已完成）；
                            // 此处仅追加「组数」维度：分组后有效组 < 2 则走下方既有单组路径。
                            // Deflate64 兜底路径可能是在 MT 关闭时进入的（7z 是唯一能写 method 9 的路径），
                            // 此时强制 1 组，避免把「关闭多线程」的用户意外带回 N 组并行。
                            int degree = options.MultiThreadedCompression
                                ? ResolveParallelCompressDegree(options.ParallelCompressDegree)
                                : 1;
                            var groups = SplitCompressGroup(compressGroup, degree);

                            // ── 闸门：copy-mode 承载能力 ──
                            // 既有 CanCopyModeRewrite 只守「混合场景」，因为单组全可压缩场景是
                            // File.Move 直出、从不经过重写器。N 组把这个前提打破了：**每个**场景
                            // 都要经多源 copy-mode 合并，而 copy-mode 只接受 method 0/8/9。
                            // BZip2/LZMA/PPMd 今天由 7z 直写 + Move 能正常产出，若不补这道闸门，
                            // N 组下会抛 ZipCopyModeException —— 属于「本来能用变成硬失败」的回归。
                            bool nGroupEligible = groups.Count >= 2
                                && CanCopyModeRewrite(options.ZipCompressionMethod);
                            if (groups.Count >= 2 && !nGroupEligible)
                                LogCopyModeUnsupported(options.ZipCompressionMethod);

                            if (nGroupEligible)
                            {
                                CoreLog.Info($"CompressAsync: NGroup branch — {groups.Count} groups (degree={degree}), {compressGroup.Count} compress / {storeGroup.Count} store");

                                // 每组一个 tempZip，组内 mt=off；并行度来自组间而非 7z 内部线程
                                var tempDir = Path.Combine(Path.GetTempPath(),
                                    "mz_ngroup_" + Guid.NewGuid().ToString("N"));
                                Directory.CreateDirectory(tempDir);
                                var tempZips = new string[groups.Count];

                                // 进度分母 = 压缩组 + Store 组的**输入**字节之和。
                                // 必须含 storeGroup：既有单组路径传的是含 Store 的 totalBytes 且
                                // storeProcessedBytes=0；若这里只算 compressGroup，自适应 Store 占比高时
                                // 分母偏小，与今天的口径不一致，合并阶段也会凭空多出一截进度。
                                long compressTotalBytes = compressGroup.Sum(f => SafeFileSize(f.FullPath));
                                long storeTotalBytes = storeGroup.Sum(f => SafeFileSize(f.FullPath));
                                long globalTotal = compressTotalBytes + storeTotalBytes;
                                if (globalTotal <= 0) globalTotal = 1;

                                // 已结束组的输入字节累计（Interlocked 保证并发安全）
                                long finishedInputBytes = 0;

                                try
                                {
                                    await Parallel.ForEachAsync(
                                        Enumerable.Range(0, groups.Count),
                                        new ParallelOptions
                                        {
                                            MaxDegreeOfParallelism = groups.Count,
                                            CancellationToken = cancellationToken
                                        },
                                        async (i, ct) =>
                                        {
                                            var groupTotal = groups[i].Sum(f => SafeFileSize(f.FullPath));
                                            var localLastReport = DateTime.Now;
                                            tempZips[i] = Path.Combine(tempDir, $"g{i}.zip");

                                            // 把组内**局部**字节进度换算为**全局**字节加权进度，
                                            // 同时携带 BatchIndex/BatchCount 供 UI 建立并更新通道行。
                                            var adapter = new InlineProgress<ArchiveProgress>(local =>
                                            {
                                                long localRealDone = Math.Min(local.ProcessedBytes, groupTotal);
                                                long localDone = groupTotal > 0 ? (long)Math.Round(groupTotal * Math.Clamp(local.PercentComplete, 0.0, 100.0) / 100.0) : 0;
                                                long globalDone = Interlocked.Read(ref finishedInputBytes) + localDone;

                                                progress?.Report(new ArchiveProgress
                                                {
                                                    // 组阶段封顶 95%，余下留给合并阶段。注意分母含 Store 组，
                                                    // 故组阶段结束时实际停在 95 * compressTotal/globalTotal
                                                    // ——自适应 Store 占比高时会明显低于 95，这是正确的：
                                                    // 余下百分比对应合并阶段真实要拷贝的 Store 字节。
                                                    PercentComplete = Math.Min(95.0, globalDone * 100.0 / globalTotal),
                                                    ProcessedBytes = globalDone,
                                                    TotalBytes = globalTotal,
                                                    CurrentFile = local.CurrentFile,
                                                    EntryKey = local.EntryKey,
                                                    // 逐条目终态（D2）透传：与既有单组路径一致，否则 N 组下
                                                    // 压缩条目的 Completed 事件会在适配层丢失。
                                                    EntryStatus = local.EntryStatus,
                                                    // 漏拷修复（D4 几何修复配套）：组内当前文件的字节进度透传，
                                                    // 否则详细模式左区文件底纹在 N 组压缩下恒为 0。
                                                    FilePercentComplete = local.FilePercentComplete,
                                                    FileTotalBytes = local.FileTotalBytes,
                                                    BatchProcessedBytes = localDone,
                                                    BatchTotalBytes = groupTotal,
                                                    CompressionRatio = TryFileOutputRatio(tempZips[i], localRealDone),
                                                    BatchIndex = i,
                                                    BatchCount = groups.Count,
                                                    BatchPercentComplete = groupTotal > 0
                                                        ? Math.Min(100.0, localDone * 100.0 / groupTotal)
                                                        : 0.0,
                                                });
                                            });

                                            await Task.Run(() => CompressGroupWithSevenZip(
                                                groups[i], tempZips[i], options, adapter,
                                                0, groupTotal, groups[i].Count, 0,
                                                i, groups.Count, null, ref localLastReport), ct);

                                            // 该组完成：把它的输入字节并入全局已完成基数
                                            Interlocked.Add(ref finishedInputBytes, groupTotal);
                                        });

                                    // 阶段 3：合并（静默期较长 → 状态行显「正在收尾…」）
                                    progress.ReportPhase(ArchivePhase.Finalizing);

                                    // ── 合并：N 个 tempZip + Store 条目 → 最终 ZIP（copy-mode，从不解压也从不重压）──
                                    //
                                    // 【必须带 storeGroup】既有单组路径把 Store 条目作为 addEntries 交给
                                    // 重写器。N 组分支整体替换了那段 try，若 addEntries 传 null，
                                    // 开启自适应时所有「已压缩文件」（jpg/mp4/zip…）会从产物中静默消失。
                                    var storeStreams = new List<Stream>(storeGroup.Count);
                                    try
                                    {
                                        var storeEntries = new List<NewEntry>(storeGroup.Count);
                                        foreach (var (fullPath, relativePath) in storeGroup)
                                        {
                                            cancellationToken.ThrowIfCancellationRequested();
                                            // 必须保持打开直到 RewriteAsync 完成（ZipBinaryRewriter 契约）
                                            var storeStream = SharedReadStream.OpenRead(fullPath);
                                            storeStreams.Add(storeStream);
                                            storeEntries.Add(new NewEntry(
                                                // 条目名编码固定 UTF-8：与 7z 产物保持一致（混用编码会破坏条目名）
                                                EntryName: ArchivePath.Normalize(relativePath),
                                                Data: storeStream,
                                                LastModified: File.GetLastWriteTime(fullPath),
                                                Size: storeStream.Length,
                                                Store: true));
                                        }

                                        // 合并阶段进度：重写器只上报固定的 92/94/97/100、**无字节信息**。
                                        // 直接转发会与组阶段的字节加权百分比打架 —— 自适应 Store 占比高时
                                        // 组阶段才停在 ~10%，突然跳到 92%。故线性重映射到 [组阶段终点, 100]：
                                        // 既单调不回退，也让长合并阶段仍有进度反馈。
                                        double groupEndPercent = Math.Min(95.0, compressTotalBytes * 100.0 / globalTotal);
                                        var mergeAdapter = new InlineProgress<ArchiveProgress>(raw =>
                                        {
                                            double p = Math.Clamp(raw.PercentComplete, 0, 100);
                                            double pct = groupEndPercent + (100.0 - groupEndPercent) * (p / 100.0);
                                            progress?.Report(new ArchiveProgress
                                            {
                                                PercentComplete = pct,
                                                ProcessedBytes = (long)(globalTotal * pct / 100.0),
                                                TotalBytes = globalTotal,
                                                CurrentFile = string.Empty,
                                                EntryKey = raw.EntryKey,
                                                EntryStatus = raw.EntryStatus,
                                            });
                                        });

                                        await ZipBinaryRewriter.RewriteAsync(
                                            sourcePaths: tempZips.Where(File.Exists).ToArray(),
                                            destPath: outputPath,
                                            keepEntryNames: null,
                                            addEntries: storeEntries,
                                            encoding: Encoding.UTF8,
                                            comment: options.Comment,
                                            progress: mergeAdapter,
                                            cancellationToken: cancellationToken);
                                    }
                                    finally
                                    {
                                        foreach (var s in storeStreams) { try { s.Dispose(); } catch { } }
                                    }

                                    // 逐条目状态（D2）：与既有单组路径一致，Store 条目在合并后报 Completed
                                    foreach (var (_, relativePath) in storeGroup)
                                    {
                                        progress?.Report(new ArchiveProgress
                                        {
                                            EntryKey = relativePath,
                                            EntryStatus = ArchiveEntryStatus.Completed
                                        });
                                    }

                                    progress?.Report(new ArchiveProgress
                                    {
                                        PercentComplete = 100,
                                        FilePercentComplete = 100,
                                        ProcessedBytes = globalTotal,
                                        TotalBytes = globalTotal,
                                        TotalFiles = totalFiles,
                                        ProcessedFiles = totalFiles,
                                        CurrentFile = string.Empty,
                                    });
                                }
                                finally
                                {
                                    try { Directory.Delete(tempDir, true); } catch { /* best-effort */ }
                                }

                                processedBytes = totalBytes;
                                processedFiles = totalFiles;
                                return; // N 组路径完成，跳过单组 7z 与标准 foreach
                            }

                            var tempZip = Path.Combine(Path.GetTempPath(), $"mantiszip_mt_{Guid.NewGuid():N}.zip");
                            try
                            {
                                CoreLog.Info($"CompressAsync: MultiThreaded mode — {storeGroup.Count} store, {compressGroup.Count} compress via 7z mt=on");
                                CompressGroupWithSevenZip(compressGroup, tempZip, options, progress, 0, totalBytes, totalFiles, 0, null, null,
                                    options.MultiThreadedCompression ? null : "1", ref lastReportTime);

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
                                            var storeStream = SharedReadStream.OpenRead(fullPath);
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

                    var activeWriterOptions = deflate64StoreOnly
                        ? new ZipWriterOptions(CompressionType.None)
                        {
                            CompressionLevel = 0,
                            ArchiveComment = options.Comment ?? "",
                            ArchiveEncoding = writerOptions.ArchiveEncoding,
                        }
                        : writerOptions;
                    using var zipWriter = new ZipWriter(fsOut, activeWriterOptions);

                    long skippedBytes = 0; // 因读取失败被跳过文件的字节：仅用于推进总进度；不计入 ProcessedFiles，也不污染压缩率
                    foreach (var (fullPath, relativePath) in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!ReadFileWithRetry(fullPath, relativePath, options, zipWriter,
                                ref processedBytes, totalBytes, totalFiles, ref processedFiles,
                                cancellationToken, progress, ref lastReportTime, out var skipReason, fsOut))
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            // 逐条目状态（D2）：读取被跳过 → Skipped（带原因类别，spec §5.3）
                            var skippedFileSize = SafeFileSize(fullPath);
                            progress?.Report(new ArchiveProgress { EntryKey = relativePath, EntryStatus = ArchiveEntryStatus.Skipped, FileTotalBytes = skippedFileSize, EntryFailureReason = skipReason ?? EntryFailureReason.Other });
                            // 主进度报告：跳过条目也刷新进度（连续大量跳过时窗口长时间无更新、看似卡死）。
                            // 跳过不计入 ProcessedFiles（已处理），但计入字节进度 skippedBytes 使总进度条推进；
                            // 压缩率仍按实际压缩字节 processedBytes 计算，不受跳过影响。
                            skippedBytes += skippedFileSize;
                            progress?.Report(new ArchiveProgress
                            {
                                CurrentFile = relativePath,
                                PercentComplete = totalBytes > 0
                                    ? Math.Min((double)(processedBytes + skippedBytes) / totalBytes * 100, 100)
                                    : 100,
                                FilePercentComplete = 100,
                                TotalFiles = totalFiles,
                                ProcessedFiles = processedFiles,
                                TotalBytes = totalBytes,
                                ProcessedBytes = processedBytes + skippedBytes,
                                FileTotalBytes = skippedFileSize,
                                BatchProcessedBytes = processedBytes + skippedBytes,
                                BatchTotalBytes = totalBytes,
                                CompressionRatio = TryStreamOutputRatio(fsOut, processedBytes)
                            });
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
                                ProcessedFiles = processedFiles,
                                TotalBytes = totalBytes,
                                ProcessedBytes = processedBytes,
                                FileTotalBytes = SafeFileSize(fullPath),
                                BatchProcessedBytes = processedBytes,
                                BatchTotalBytes = totalBytes,
                                CompressionRatio = TryStreamOutputRatio(fsOut, processedBytes)
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

            // 阶段 3：后置回填（读中央目录 → 期间也属收尾，状态行显「正在收尾…」）
            progress.ReportPhase(ArchivePhase.Finalizing);

            // 后置回填（压缩率计划 D2）：成品 ZIP 写出后读中央目录，逐条目回填压缩后大小 + 整体汇总。
            // 仅 ZIP（本引擎）；分卷跳过（不读 .zip.001）；加密包中央目录未加密，无需密码；异常吞掉仅影响展示。
            if (options.SplitSize <= 0)
            {
                try
                {
                    long totalCompressed = 0;
                    using (var finalArchive = OpenArchiveWithEncodingFallback(outputPath, options.Password))
                    {
                        foreach (var entry in finalArchive.Entries)
                        {
                            if (entry.IsDirectory) continue;
                            var key = ArchivePath.Normalize(entry.Key);
                            totalCompressed += entry.CompressedSize;
                            progress?.Report(new ArchiveProgress
                            {
                                EntryKey = key,
                                EntryStatus = ArchiveEntryStatus.Completed,
                                EntryCompressedBytes = entry.CompressedSize,
                            });
                        }
                    }
                    if (totalCompressed > 0)
                        progress?.Report(new ArchiveProgress
                        {
                            TotalBytes = totalBytes,
                            TotalCompressedBytes = totalCompressed,
                        });
                }
                catch (Exception ex)
                {
                    CoreLog.Trace("CompressAsync: post-pass compressed-size fill skipped: {0}", ex.Message);
                }
            }

            CoreLog.Info($"CompressAsync: done, {processedBytes}/{totalBytes} bytes, {sw.ElapsedMilliseconds}ms");
        }, cancellationToken).ConfigureAwait(false);

        CoreLog.Exit();
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

}
