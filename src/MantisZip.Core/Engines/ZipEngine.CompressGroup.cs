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
/// ZipEngine 的 分卷/多线程压缩组与增删条目辅助部分（自 ZipEngine.cs 拆分，纯移动）。
/// </summary>
public partial class ZipEngine
{
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

                    // 文件名编码：显式设置优先（与 CompressAsync 的 options.FileNameEncoding 同源，
                    // 拖拽添加由 AddFilesToArchiveAsync 透传 AppSettings.ZipEncoding）；
                    // 未设置时才按包内 bit 11 启发式推断。
                    var encoding = ResolveFileNameEncoding(options.FileNameEncoding, archivePath);

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

                            // ── N 组并行分支（AddToArchiveAsync 接入 N 组，与 CompressAsync 同构）──
                            // 复用既有回退判定（IsMultiThreadedEligible 已在上方完成）；
                            // 此处仅追加「组数」维度：分组后有效组 < 2 则走下方既有单组路径。
                            int degree = ResolveParallelCompressDegree(options.ParallelCompressDegree);
                            var groups = SplitCompressGroup(compressGroup, degree);

                            // ── 闸门：copy-mode 承载能力 ──
                            // N 组下每个场景都要经多源 copy-mode 合并，而 copy-mode 只接受 method 0/8/9。
                            // 不满足时回落到下方既有单组路径（7z 直写 + Move，不经重写器）。
                            bool nGroupEligible = groups.Count >= 2
                                && CanCopyModeRewrite(options.ZipCompressionMethod);
                            if (groups.Count >= 2 && !nGroupEligible)
                                LogCopyModeUnsupported(options.ZipCompressionMethod);

                            if (nGroupEligible)
                            {
                                CoreLog.Info($"AddToArchiveAsync: NGroup branch — {groups.Count} groups (degree={degree}), {compressGroup.Count} compress / {storeGroup.Count} store");

                                // fsOut 是 File.Create 出来的空文件句柄（FileShare.None）；
                                // 合并目标就是 tempArchive，必须先释放句柄，否则重写器无法创建目标文件。
                                // FileStream.Dispose 幂等，外层 using 再次 Dispose 无副作用。
                                fsOut.Dispose();

                                // 独立临时目录：外层已有 tempDir（Phase 1/2 内容目录），勿 shadow
                                var ngTempDir = Path.Combine(Path.GetTempPath(),
                                    "mz_ngroup_" + Guid.NewGuid().ToString("N"));
                                Directory.CreateDirectory(ngTempDir);
                                var tempZips = new string[groups.Count];

                                // 进度分母复用外层 compressTotalBytes（已含 Store 组与旧条目，
                                // 与既有单组路径口径一致，且已按 ==0 保护为 1）。
                                long ngCompressBytes = compressGroup.Sum(f => SafeFileSize(f.FullPath));
                                // 已结束组的输入字节累计（Interlocked 保证并发安全）
                                long finishedInputBytes = 0;

                                try
                                {
                                    // ── 组间并行：每组一个 7z 产物，组内 mt=off（见 CompressGroupWithSevenZip）──
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
                                            tempZips[i] = Path.Combine(ngTempDir, $"g{i}.zip");

                                            // 把组内**局部**字节进度换算为**全局**字节加权进度，
                                            // 同时携带 BatchIndex/BatchCount 供 UI 建立并更新通道行。
                                            var adapter = new InlineProgress<ArchiveProgress>(local =>
                                            {
                                                long localRealDone = Math.Min(local.ProcessedBytes, groupTotal);
                                                long localDone = groupTotal > 0 ? (long)Math.Round(groupTotal * Math.Clamp(local.PercentComplete, 0.0, 100.0) / 100.0) : 0;
                                                long globalDone = Interlocked.Read(ref finishedInputBytes) + localDone;

                                                progress?.Report(new ArchiveProgress
                                                {
                                                    // 组阶段封顶 95%，余下留给合并阶段。分母含 Store 组，
                                                    // 故组阶段结束时实际停在 95 * ngCompress/total —— 正确：
                                                    // 余下百分比对应合并阶段真实要拷贝的 Store 字节。
                                                    PercentComplete = Math.Min(95.0, globalDone * 100.0 / compressTotalBytes),
                                                    ProcessedBytes = globalDone,
                                                    TotalBytes = compressTotalBytes,
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
                                                i, groups.Count, ref localLastReport), ct);

                                            // 该组完成：把它的输入字节并入全局已完成基数
                                            Interlocked.Add(ref finishedInputBytes, groupTotal);
                                        });

                                    // ── 一次合并：N 个 tempZip + Store 条目 → tempArchive ──
                                    // （对比旧路径的「单组 tempZip 再合并」；此处全部组一次性合并，
                                    //   避免 N 次串行重写。Store 条目作为 addEntries 走 copy-mode 追加。）
                                    var ngStoreStreams = new List<FileStream>(storeGroup.Count);
                                    try
                                    {
                                        var storeEntries = new List<NewEntry>(storeGroup.Count);
                                        foreach (var (fullPath, relativePath) in storeGroup)
                                        {
                                            cancellationToken.ThrowIfCancellationRequested();
                                            // 必须保持打开直到 RewriteAsync 完成（ZipBinaryRewriter 契约），故不能 using
                                            var storeStream = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                                            ngStoreStreams.Add(storeStream);
                                            storeEntries.Add(new NewEntry(
                                                // 条目名编码固定 UTF-8：与 7z 产物保持一致（混用编码会破坏条目名）
                                                EntryName: ArchivePath.Normalize(relativePath),
                                                Data: storeStream,
                                                LastModified: File.GetLastWriteTime(fullPath),
                                                Size: storeStream.Length,
                                                Store: true));
                                        }

                                        // 合并阶段进度：重写器只上报固定的 92/94/97/100、无字节信息。
                                        // 直接转发会与组阶段的字节加权百分比打架，故线性重映射到
                                        // [组阶段终点, 100]：既单调不回退，也让长合并阶段仍有进度反馈。
                                        double ngGroupEnd = Math.Min(95.0, ngCompressBytes * 100.0 / compressTotalBytes);
                                        var mergeAdapter = new InlineProgress<ArchiveProgress>(raw =>
                                        {
                                            double p = Math.Clamp(raw.PercentComplete, 0, 100);
                                            double pct = ngGroupEnd + (100.0 - ngGroupEnd) * (p / 100.0);
                                            progress?.Report(new ArchiveProgress
                                            {
                                                PercentComplete = pct,
                                                ProcessedBytes = (long)(compressTotalBytes * pct / 100.0),
                                                TotalBytes = compressTotalBytes,
                                                CurrentFile = string.Empty,
                                                EntryKey = raw.EntryKey,
                                                EntryStatus = raw.EntryStatus,
                                            });
                                        });

                                        await ZipBinaryRewriter.RewriteAsync(
                                            sourcePaths: tempZips.Where(File.Exists).ToArray(),
                                            destPath: tempArchive,
                                            keepEntryNames: null,
                                            addEntries: storeEntries,
                                            encoding: Encoding.UTF8,
                                            comment: options.Comment,
                                            progress: mergeAdapter,
                                            cancellationToken: cancellationToken);
                                    }
                                    finally
                                    {
                                        // Store 组的 FileStream 必须在 RewriteAsync 之后统一 Dispose，
                                        // 否则 Store 组大时句柄泄漏。
                                        foreach (var s in ngStoreStreams) { try { s.Dispose(); } catch { } }
                                    }

                                    // 逐条目状态（D2）：Store 条目在合并后报 Completed，与既有单组路径一致
                                    foreach (var (_, relativePath) in storeGroup)
                                    {
                                        progress?.Report(new ArchiveProgress
                                        {
                                            EntryKey = relativePath,
                                            EntryStatus = ArchiveEntryStatus.Completed
                                        });
                                    }

                                    compressProcessed = compressTotalBytes;

                                    progress?.Report(new ArchiveProgress
                                    {
                                        CurrentFile = string.Empty,
                                        PercentComplete = 100,
                                        FilePercentComplete = 100
                                    });
                                }
                                finally
                                {
                                    try { Directory.Delete(ngTempDir, true); } catch { /* best-effort */ }
                                }
                            }
                            else
                            {
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
                                    // 统一用 4 参重载（传 MultiThreadedStoreFormatIds），与 MT 分拣（CompressAsync/AddToArchiveAsync）
                                    // 及本方法写入阶段同口径；无自定义列表时行为与 3 参完全一致。
                                    entryLevel = ZipEntryClassifier.GetAdaptiveLevel(fullPath, options.CompressionLevel, true, options.MultiThreadedStoreFormatIds);
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

    public async Task DeleteEntriesAsync(string archivePath, string[] entryPaths, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default, ArchiveOptions? options = null)
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

                    // 文件名编码：与 AddToArchiveAsync 同源，显式设置优先。
                    // 删除会整包重写，此处若猜错编码会把存活的条目降级成乱码。
                    var encoding = ResolveFileNameEncoding(options?.FileNameEncoding, archivePath);

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
        CancellationToken ct, IProgress<ArchiveProgress>? progress, ref DateTime lastReportTime,
        out EntryFailureReason? skipReason, Stream? fsOut = null)
    {
        skipReason = null;
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
                                ProcessedFiles = processedFiles,
                                TotalBytes = totalBytes,
                                ProcessedBytes = processedBytes,
                                FileTotalBytes = fiLen,
                                BatchProcessedBytes = processedBytes,
                                BatchTotalBytes = totalBytes,
                                CompressionRatio = TryStreamOutputRatio(fsOut, processedBytes)
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
                // 在重试/决策前先分类原因（spec §5.3）：即便最终 Retry 成功，分类也与其它埋点一致
                skipReason = FailureReasonMapper.FromException(ex);
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

    /// <summary>流式输出压缩率（输出字节/输入字节×100）；不可 seek / 未创建 / 分卷（Length 抛异常）→ null（Rule 6 隐藏）。</summary>
    private static double? TryStreamOutputRatio(Stream? output, long inputBytes)
    {
        if (inputBytes <= 0) return null;
        try
        {
            if (output is null || !output.CanSeek) return null;
            var len = output.Length;
            if (len <= 0) return null;
            return Math.Clamp((double)len / inputBytes * 100.0, 0, 100);
        }
        catch (NotSupportedException) { return null; }   // SplitOutputStream.Length 抛异常 → 分卷隐藏
        catch (IOException) { return null; }
        catch (ObjectDisposedException) { return null; }
    }

    /// <summary>文件输出压缩率（文件长度/输入字节×100）；文件尚未创建/被占用 → null（隐藏压缩率段）。</summary>
    private static double? TryFileOutputRatio(string? path, long inputBytes)
    {
        if (inputBytes <= 0 || string.IsNullOrEmpty(path)) return null;
        try
        {
            var len = new FileInfo(path).Length;
            return len > 0 ? Math.Clamp((double)len / inputBytes * 100.0, 0, 100) : null;
        }
        catch (Exception) { return null; }
    }

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

        // 多线程压缩（方案 C）：N 组路径每组 mt=核数/组数，使总并行度恒 ≈ 核数；
        // 旧单组路径保持 mt=on（= 全部核）。
        // ⚠ 未验证：默认路径下多为 no-op（核数 ≤ 16 且组数 = 核数时 mt=1）；
        //   收益仅可能出现在「核数 > 16」或「文件数 < 组数」场景，不得当性能依据。
        compr.CustomParameters["mt"] = batchIndex.HasValue
            ? Math.Max(1, Environment.ProcessorCount / Math.Max(1, batchCount ?? 1)).ToString()
            : "on";

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

        // A 方案：把该组总体进度区间 [overallBasePct, groupEndPct] 的前半段（MirrorWeight）分给镜像拷贝、
        // 后半段分给 7z 压缩，使两阶段都推进总体且单调不回退（原先镜像阶段冻结在基线、7z 再从基线重起）。
        long groupBytes = 0;
        foreach (var (gp, _) in files) groupBytes += SafeFileSize(gp);
        double overallBasePct = totalBytes > 0 ? (double)storeProcessedBytes / totalBytes * 100.0 : 0.0;
        double groupRangePct = totalBytes > 0 ? (double)groupBytes / totalBytes * 100.0 : 0.0;
        const double MirrorWeight = 0.5;
        double mirrorCeilPct = overallBasePct + groupRangePct * MirrorWeight;
        double groupEndPct = overallBasePct + groupRangePct;
        long mirroredTotal = 0;

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
                // 已完成字节占比 → 映射到该组区间后半段（[mirrorCeilPct, groupEndPct]），与镜像阶段衔接
                double frac = groupBytes > 0
                    ? Math.Min(1.0, (double)completedBytes / groupBytes)
                    : (totalFiles > 0 ? Math.Min(1.0, (double)completedFiles / totalFiles) : 0.0);
                pct = mirrorCeilPct + frac * (groupEndPct - mirrorCeilPct);
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
                FileTotalBytes = 0,
                BatchProcessedBytes = doneBytes,
                BatchTotalBytes = totalBytes,
                CompressionRatio = TryFileOutputRatio(tempPath, doneBytes),
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
                // 已完成字节占比 → 映射到该组区间后半段（[mirrorCeilPct, groupEndPct]）
                double doneFrac = groupBytes > 0
                    ? Math.Min(1.0, (double)completedBytes / groupBytes)
                    : (totalFiles > 0 ? Math.Min(1.0, (double)completedFiles / totalFiles) : 0.0);
                completedReport = new ArchiveProgress
                {
                    EntryKey = finishedEntryKey,
                    EntryStatus = ArchiveEntryStatus.Completed,
                    CurrentFile = Path.GetFileName(finishedEntryKey),
                    PercentComplete = mirrorCeilPct + doneFrac * (groupEndPct - mirrorCeilPct),
                    FilePercentComplete = null,
                    TotalBytes = totalBytes,
                    ProcessedBytes = doneBytes,
                    TotalFiles = totalFiles,
                    ProcessedFiles = doneFiles,
                    FileTotalBytes = finishedSize,
                    BatchProcessedBytes = doneBytes,
                    BatchTotalBytes = totalBytes,
                    CompressionRatio = TryFileOutputRatio(tempPath, doneBytes),
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
            // 镜像拷贝：把每个文件复制到临时目录（为让 7z 保留相对路径）。
            // 此阶段可能占大头（1GB 语料实测 ~12s），原先 File.Copy 无任何上报 →
            // 简约/详细面板整段「卡住」。改为分块拷贝并节流上报「当前文件 + 当前文件百分比」；
            // 总体进度（PercentComplete）按已拷贝字节映射到该组区间前半段（[overallBasePct, mirrorCeilPct]），
            // 与随后 7z 阶段的后半段衔接（A 方案，两阶段均推进、单调不回退）。
            var copyBuffer = new byte[4 * 1024 * 1024];

            foreach (var (fullPath, relativePath) in files)
            {
                var destPath = Path.Combine(tempDir, relativePath);
                var destDir = Path.GetDirectoryName(destPath)!;
                Directory.CreateDirectory(destDir);

                long fileLen = 0;
                try { fileLen = new FileInfo(fullPath).Length; } catch { /* 读不到大小则按 0 处理 */ }
                long copied = 0;

                using (var src = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    int read;
                    while ((read = src.Read(copyBuffer, 0, copyBuffer.Length)) > 0)
                    {
                        dst.Write(copyBuffer, 0, read);
                        copied += read;
                        mirroredTotal += read;

                        var now = DateTime.Now;
                        if (now - localLastReportTime < TimeSpan.FromMilliseconds(100) && copied < fileLen) continue;
                        localLastReportTime = now;

                        // 已拷贝字节占比 → 映射到该组区间前半段（单调；跨文件亦不回退）
                        double mirrorFrac = groupBytes > 0 ? Math.Min(1.0, (double)mirroredTotal / groupBytes) : 0.0;
                        double mirrorPct = overallBasePct + mirrorFrac * (mirrorCeilPct - overallBasePct);

                        progress?.Report(new ArchiveProgress
                        {
                            CurrentFile = Path.GetFileName(relativePath),
                            FilePercentComplete = fileLen > 0
                                ? Math.Min(100.0, (double)copied / fileLen * 100.0)
                                : 100.0,
                            PercentComplete = mirrorPct,
                            ProcessedBytes = storeProcessedBytes,
                            TotalBytes = totalBytes,
                            TotalFiles = totalFiles,
                            ProcessedFiles = storeProcessedFiles,
                            FileTotalBytes = fileLen,
                            BatchProcessedBytes = storeProcessedBytes,
                            BatchTotalBytes = totalBytes,
                            BatchIndex = batchIndex,
                            BatchCount = batchCount,
                        });
                    }
                }

                // File.Copy 会保留源文件最后写入时间；分块拷贝需手动补回，
                // 否则归档内条目的时间戳会全部变成「拷贝时刻」。
                try { File.SetLastWriteTime(destPath, File.GetLastWriteTime(fullPath)); } catch { /* best-effort */ }
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
    /// 解析有效并行组数：显式值 1..16 照旧生效；&lt;=0 取引擎默认
    /// <c>clamp(ProcessorCount, 4, 16)</c>（下限 4 避开已证实的 deg=2 陷阱，
    /// 上限 16 与解压侧一致）。与解压侧 <c>ParallelExtractDegree</c> 的解析规则保持一致。
    /// </summary>
    internal static int ResolveParallelCompressDegree(int configured)
    {
        int n = configured;
        if (n <= 0) n = Math.Clamp(Environment.ProcessorCount, 4, 16);
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
    /// 构造启用官方 CRC 校验的提取选项。
    /// <para>
    /// ⚠️ <c>IArchiveEntry.OpenEntryStream()</c> 返回的是**裸流，不校验 CRC**——
    /// 引擎曾在测试/解压路径直接使用它，导致「数据被改动但仍能解码」的损坏包
    /// （典型：stored 条目字节翻转、CRC 字段错）被静默解出而不报错。
    /// SharpCompress 的校验能力只挂在官方的 <c>WriteTo/WriteToAsync</c> 扩展方法上，
    /// 由 <see cref="ExtractionOptions.CheckCrc"/> 触发（默认 true，此处显式指定以防上游改默认）。
    /// 该校验路径内部已正确处理目录条目、CRC 不可用、以及 WinZip AES AE-2
    /// （其 CRC 字段按设计为 0）等边界，故对加密包不会误报。
    /// </para>
    /// BufferSize 沿用 <see cref="CopyBufferSize"/> 以保持吞吐（进度上报粒度随之与旧实现一致）。
    /// </summary>
    private static ExtractionOptions CreateCrcExtractionOptions() =>
        new() { CheckCrc = true, BufferSize = CopyBufferSize };

    /// <summary>
    /// 删除 CRC 校验失败而写出的损坏文件（best-effort）。
    /// CRC 校验发生在条目读完时，此时目标文件已含**内容错误**的完整数据；
    /// 保留它会让用户误以为解压成功，故直接删除、只留失败明细。
    /// </summary>
    private static void TryDeleteCorruptOutput(string path)
    {
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { CoreLog.Trace("TryDeleteCorruptOutput: failed to delete '{0}': {1}", path, ex.Message); }
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
