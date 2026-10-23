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
public partial class ZipEngine : IArchiveEngine
{
    private const int CopyBufferSize = 4194304;

    public bool CanHandle(ArchiveFormat format) => format == ArchiveFormat.Zip;

    public bool CanAdd(ArchiveFormat format) => format == ArchiveFormat.Zip;

    public bool CanDelete(ArchiveFormat format) => format == ArchiveFormat.Zip;

    /// <summary>
    /// ZIP 引擎支持并行解压（多实例模式，每个线程独立打开 archive）。
    /// </summary>
    public bool SupportsParallelExtract => true;

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
        progress.ReportPhase(ArchivePhase.Testing);

        var result = await Task.Run(() =>
        {
            try
            {
                using var archive = OpenArchiveWithEncodingFallback(archivePath, password);

                var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
                int totalFiles = entries.Count;
                int processedFiles = 0;
                int failedFiles = 0;

                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entryKey = entry.Key ?? "";
                    long entrySize = entry.Size;

                    // 文件开始：文件进度条归零
                    progress?.Report(new ArchiveProgress
                    {
                        EntryKey = entryKey,
                        CurrentFile = entryKey,
                        PercentComplete = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 100,
                        FilePercentComplete = 0,
                        TotalFiles = totalFiles,
                        ProcessedFiles = processedFiles,
                    });

                    try
                    {
                        // 用官方 CheckCrc 路径把条目完整写出以验证数据完整性。
                        // 注意：OpenEntryStream() 是裸流、不校验 CRC（旧注释的假设不成立），
                        // 只有 WriteTo + CheckCrc=true 才会在读完时对比条目 CRC，
                        // 不一致时抛 InvalidFormatException → 由下方 catch 归类为失败。
                        entry.WriteTo(Stream.Null, CreateCrcExtractionOptions(), new InlineProgress<ProgressReport>(rep =>
                        {
                            var filePct = entrySize > 0 ? (double)rep.BytesTransferred / entrySize * 100 : 100;
                            progress?.Report(new ArchiveProgress
                            {
                                EntryKey = entryKey,
                                CurrentFile = entryKey,
                                PercentComplete = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 100,
                                FilePercentComplete = filePct,
                                TotalFiles = totalFiles,
                                ProcessedFiles = processedFiles,
                            });
                        }));

                        processedFiles++;
                        progress?.Report(new ArchiveProgress
                        {
                            EntryKey = entryKey,
                            EntryStatus = ArchiveEntryStatus.Completed,
                            CurrentFile = entryKey,
                            PercentComplete = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 100,
                            FilePercentComplete = 100,
                            FileTotalBytes = entrySize,
                            TotalFiles = totalFiles,
                            ProcessedFiles = processedFiles,
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // 逐条目测试失败定位（spec §12 Follow-up #6）：报告失败条目 + 原因类别，继续校验其余条目
                        failedFiles++;
                        processedFiles++;
                        CoreLog.Error($"TestArchiveAsync: entry '{entryKey}' failed", ex);
                        progress?.Report(new ArchiveProgress
                        {
                            EntryKey = entryKey,
                            EntryStatus = ArchiveEntryStatus.Failed,
                            EntryFailureReason = FailureReasonMapper.FromException(ex),
                            CurrentFile = entryKey,
                            PercentComplete = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 100,
                            FilePercentComplete = 100,
                            FileTotalBytes = entrySize,
                            TotalFiles = totalFiles,
                            ProcessedFiles = processedFiles,
                            EntryErrorDetail = ex.Message,
                            EntryErrorHResult = ex.HResult,
                        });
                    }
                }

                CoreLog.Info($"TestArchiveAsync: {(failedFiles > 0 ? "failed" : "passed")}, {totalFiles} entries verified, failedEntries={failedFiles}");
                return failedFiles == 0;
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
    /// 合并（copy-mode 重写）阶段的进度适配器：把重写器的**字节**进度映射到
    /// 「组阶段终点 → 100」区间，同时透传当前文件名、条目状态与文件计数。
    /// <para>
    /// 背景：合并阶段此前把 <c>CurrentFile</c> 清空、也不报 <c>ProcessedFiles</c>，
    /// 于是「自适应把大量 jpg 判为仅存储」时，用户只看到最后一个普通文件的名字一直挂着、
    /// 进度条几乎不动。重写器本身逐条目上报，这里把它接回全局口径。
    /// </para>
    /// </summary>
    /// <param name="progress">外层进度上报（全局百分比 / 全局字节口径）</param>
    /// <param name="groupEndPercent">组阶段结束时的全局百分比（合并阶段起点）</param>
    /// <param name="displayTotalBytes">外层展示用总字节（喂统计卡与速度，不能用合并字节冒充）</param>
    /// <param name="storeKeys">Store 条目名集合：重写器每写完一个条目报 Completed，据此推进文件计数</param>
    /// <param name="baseProcessedFiles">进入合并阶段前已统计的文件数（≤0 时不报文件计数）</param>
    /// <param name="totalFiles">本次操作总文件数（≤0 时不报文件计数）</param>
    private static IProgress<ArchiveProgress> CreateMergeProgressAdapter(
        IProgress<ArchiveProgress>? progress,
        double groupEndPercent,
        long displayTotalBytes,
        IReadOnlyCollection<string> storeKeys,
        int baseProcessedFiles,
        int totalFiles)
    {
        var storeSet = new HashSet<string>(storeKeys, StringComparer.OrdinalIgnoreCase);
        int storeDone = 0;
        double lastFrac = 0;
        string? lastName = null;   // 无文件名的收尾类上报沿用上一个名字，避免下游把它当成「无文件」

        return new InlineProgress<ArchiveProgress>(raw =>
        {
            // Store 条目写完 → 文件计数向前走一格（重写器在条目结束时上报 Completed）
            if (raw.EntryStatus == ArchiveEntryStatus.Completed
                && !string.IsNullOrEmpty(raw.EntryKey)
                && storeSet.Contains(raw.EntryKey))
            {
                storeDone++;
            }

            if (!string.IsNullOrEmpty(raw.CurrentFile))
                lastName = raw.CurrentFile;

            // 优先按字节推进；重写器未带字节（收尾类上报）时保持上一次进度，绝不回退
            double frac = raw.TotalBytes > 0
                ? Math.Clamp((double)raw.ProcessedBytes / raw.TotalBytes, 0, 1)
                : Math.Clamp(raw.PercentComplete, 0, 100) / 100.0;
            if (frac < lastFrac) frac = lastFrac;
            lastFrac = frac;

            double pct = Math.Min(100.0, groupEndPercent + (100.0 - groupEndPercent) * frac);

            var report = new ArchiveProgress
            {
                PercentComplete = pct,
                ProcessedBytes = (long)(displayTotalBytes * pct / 100.0),
                TotalBytes = displayTotalBytes,
                CurrentFile = lastName ?? string.Empty,
                FilePercentComplete = raw.FilePercentComplete,
                FileTotalBytes = raw.FileTotalBytes,
                EntryKey = raw.EntryKey,
                EntryStatus = raw.EntryStatus,
            };

            if (totalFiles > 0)
            {
                report.TotalFiles = totalFiles;
                report.ProcessedFiles = Math.Min(totalFiles, Math.Max(0, baseProcessedFiles) + storeDone);
            }

            progress?.Report(report);
        });
    }
}
