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
}
