using System.Reflection;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// 合并（copy-mode 重写）阶段的进度回归：
/// ① 重写器必须上报**裸条目名**（不带「复制: 」/「压缩: 」前缀，规则 13）与**字节**进度；
/// ② 引擎的合并适配器必须把字节进度映射到 [组阶段终点, 100]，并透传文件名、按 Store 条目推进文件计数。
/// 回归背景：自适应 + 多线程压缩时，被判定「仅存储」的大量文件在合并阶段没有任何进度上报，
/// 进度窗口停在最后一个普通文件的名字上。
/// </summary>
public class ZipBinaryRewriterProgressTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles.Where(File.Exists))
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }
    }

    private string TrackFile(string path) { _tempFiles.Add(path); return path; }

    private static readonly Type RewriterType =
        typeof(ZipEngine).Assembly.GetType("MantisZip.Core.Utils.ZipBinaryRewriter")!;

    private static Task<RewriteResult> InvokeRewriteAsync(
        string sourcePath,
        string destPath,
        List<NewEntry> addEntries,
        IProgress<ArchiveProgress> progress)
    {
        var method = RewriterType.GetMethod("RewriteAsync",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            [
                typeof(string), typeof(string), typeof(HashSet<string>), typeof(List<NewEntry>),
                typeof(Encoding), typeof(string), typeof(IProgress<ArchiveProgress>),
                typeof(CancellationToken)
            ],
            modifiers: null)!;
        return (Task<RewriteResult>)method.Invoke(null, [
            sourcePath, destPath, null, addEntries, Encoding.UTF8, null, progress, CancellationToken.None
        ])!;
    }

    /// <summary>造一个含两个条目的 copy-mode 源 ZIP（ZIP64 关闭）。</summary>
    private string CreateSourceZip()
    {
        var path = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid():N}.zip"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var fs = File.Create(path);
        using var zip = new ZipOutputStream(fs) { UseZip64 = UseZip64.Off };
        zip.SetLevel(0);
        foreach (var name in new[] { "hello.txt", "subdir/nested.txt" })
        {
            zip.PutNextEntry(new ZipEntry(name));
            var bytes = Encoding.UTF8.GetBytes("payload-" + name);
            zip.Write(bytes, 0, bytes.Length);
            zip.CloseEntry();
        }
        return path;
    }

    [Fact]
    public async Task RewriteAsync_ReportsBareEntryNamesAndByteProgress()
    {
        var sourceZip = CreateSourceZip();
        var destZip = TrackFile(Path.Combine(Path.GetTempPath(), "MantisZipTest", $"{Guid.NewGuid():N}.zip"));

        // 模拟「仅存储」的大照片：走 addEntries + Store
        var payload = new byte[256 * 1024];
        Random.Shared.NextBytes(payload);
        var addEntries = new List<NewEntry>
        {
            new("photos/big.jpg", new MemoryStream(payload), DateTime.Now, payload.Length, Store: true)
        };

        var reports = new List<ArchiveProgress>();
        var collector = new InlineCollector(reports);

        await InvokeRewriteAsync(sourceZip, destZip, addEntries, collector);

        var named = reports.Where(r => !string.IsNullOrEmpty(r.CurrentFile)).ToList();
        Assert.NotEmpty(named);

        // ① 裸条目名：不得带 Core 里硬编码的中文前缀（规则 13）
        Assert.DoesNotContain(named, r => r.CurrentFile!.Contains("复制:"));
        Assert.DoesNotContain(named, r => r.CurrentFile!.Contains("压缩:"));
        Assert.Contains(named, r => r.CurrentFile == "hello.txt");
        Assert.Contains(named, r => r.CurrentFile == "photos/big.jpg");

        // ② 字节进度：单调不减，且最后一条覆盖全部待移动字节
        var byteReports = reports.Where(r => r.TotalBytes > 0).ToList();
        Assert.NotEmpty(byteReports);
        for (int i = 1; i < byteReports.Count; i++)
            Assert.True(byteReports[i].ProcessedBytes >= byteReports[i - 1].ProcessedBytes,
                $"字节进度回退：{byteReports[i - 1].ProcessedBytes} → {byteReports[i].ProcessedBytes}");
        Assert.Equal(byteReports[^1].TotalBytes, byteReports[^1].ProcessedBytes);

        // ③ 新增（Store）条目写完即报 Completed，条目行可实时变绿
        Assert.Contains(reports, r =>
            r.EntryStatus == ArchiveEntryStatus.Completed && r.EntryKey == "photos/big.jpg");
    }

    /// <summary>把进度回调收集到列表（测试用最简 IProgress 实现）。</summary>
    private sealed class InlineCollector(List<ArchiveProgress> sink) : IProgress<ArchiveProgress>
    {
        public void Report(ArchiveProgress value) => sink.Add(value);
    }

    // ══════════════════════════════════════════════════════════════════
    // 引擎侧合并适配器
    // ══════════════════════════════════════════════════════════════════

    private static readonly Type ZipEngineType = typeof(ZipEngine);

    private static IProgress<ArchiveProgress> CreateMergeAdapter(
        IProgress<ArchiveProgress> progress,
        double groupEndPercent,
        long displayTotalBytes,
        string[] storeKeys,
        int baseProcessedFiles,
        int totalFiles)
    {
        var method = ZipEngineType.GetMethod("CreateMergeProgressAdapter",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (IProgress<ArchiveProgress>)method.Invoke(null, [
            progress, groupEndPercent, displayTotalBytes, storeKeys, baseProcessedFiles, totalFiles
        ])!;
    }

    [Fact]
    public void MergeAdapter_PassesFileNamesThroughAndCountsStoredEntries()
    {
        var reports = new List<ArchiveProgress>();
        var adapter = CreateMergeAdapter(new InlineCollector(reports),
            groupEndPercent: 20.0,
            displayTotalBytes: 1000,
            storeKeys: ["photos/big.jpg", "photos/small.jpg"],
            baseProcessedFiles: 2,
            totalFiles: 4);

        // 合并阶段第 1 帧：按字节推进到一半
        adapter.Report(new ArchiveProgress
        {
            CurrentFile = "photos/big.jpg",
            ProcessedBytes = 50,
            TotalBytes = 100,
            FilePercentComplete = 55
        });

        // 条目完成事件：文件计数应向前走一格
        adapter.Report(new ArchiveProgress
        {
            EntryKey = "photos/big.jpg",
            EntryStatus = ArchiveEntryStatus.Completed
        });

        var first = reports[0];
        Assert.Equal("photos/big.jpg", first.CurrentFile);       // 文件名必须透传（此前被清空）
        Assert.Equal(60.0, first.PercentComplete, 3);            // 20 + 80*0.5
        Assert.Equal(2, first.ProcessedFiles);
        Assert.Equal(4, first.TotalFiles);

        var afterEntry = reports[^1];
        Assert.Equal(3, afterEntry.ProcessedFiles);              // 2 基础 + 1 个 Store 条目
        Assert.Equal(4, afterEntry.TotalFiles);
        Assert.Equal("photos/big.jpg", afterEntry.CurrentFile);  // 完成事件不清文件名
        // 完成事件不带字节 → 百分比不得回退
        Assert.True(afterEntry.PercentComplete >= first.PercentComplete);
    }

    [Fact]
    public void MergeAdapter_IgnoresEntryCompletionForNonStoreKeys()
    {
        var reports = new List<ArchiveProgress>();
        var adapter = CreateMergeAdapter(new InlineCollector(reports),
            groupEndPercent: 0.0, displayTotalBytes: 100, storeKeys: ["store.jpg"],
            baseProcessedFiles: 1, totalFiles: 2);

        // 压缩条目的复制完成事件不应被计成 Store 文件
        adapter.Report(new ArchiveProgress { EntryKey = "hello.txt", EntryStatus = ArchiveEntryStatus.Completed });

        Assert.Equal(1, reports[^1].ProcessedFiles);
    }

    /// <summary>
    /// 端到端：自适应 + 多线程压缩「少量普通文件 + 大量 jpg」时，合并阶段必须持续上报
    /// Store 文件名与文件计数（回归：此前进度窗口停在最后一个普通文件上、计数冻住）。
    /// </summary>
    [Fact]
    public async Task AdaptiveMultiThread_ReportsStoreFileNamesDuringMerge()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MantisZipTest", "mz_store_progress_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        // 2 个可压缩文本 + 6 个「已压缩」jpg（随机字节，扩展名命中 SSOT → 仅存储）
        for (int i = 0; i < 2; i++)
            await File.WriteAllTextAsync(Path.Combine(dir, $"text{i}.txt"), new string('a', 64 * 1024));
        for (int i = 0; i < 6; i++)
        {
            var payload = new byte[32 * 1024];
            Random.Shared.NextBytes(payload);
            await File.WriteAllBytesAsync(Path.Combine(dir, $"photo{i}.jpg"), payload);
        }

        var outZip = TrackFile(Path.Combine(dir, "out.zip"));
        var reports = new List<ArchiveProgress>();

        try
        {
            await new ZipEngine().CompressAsync(
                Directory.GetFiles(dir).Where(f => !f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToArray(),
                outZip,
                new ArchiveOptions
                {
                    CompressionLevel = 5,
                    AdaptiveCompression = true,
                    MultiThreadedCompression = true,
                    ParallelCompressDegree = 2,
                },
                new InlineCollector(reports));

            // 合并阶段（Plan 3 → 100）必须出现 Store 文件名
            var storeNames = reports
                .Where(r => !string.IsNullOrEmpty(r.CurrentFile) && r.CurrentFile!.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.CurrentFile!)
                .Distinct()
                .ToList();
            Assert.True(storeNames.Count > 0,
                "合并阶段没有上报任何 jpg 文件名；采样=" + string.Join(", ", reports.Select(r => r.CurrentFile).Where(n => n != null).Distinct().Take(10)));

            // 文件计数在合并阶段持续前进（不是结束后一次性跳到总数）
            int maxProcessedDuringMerge = reports
                .Where(r => !string.IsNullOrEmpty(r.CurrentFile) && r.CurrentFile!.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.ProcessedFiles)
                .DefaultIfEmpty(0)
                .Max();
            Assert.True(maxProcessedDuringMerge > 2,
                $"合并阶段已处理数应超过 2（2 个文本文件之后），实际 {maxProcessedDuringMerge}");

            // 产物完整：8 个条目都在
            using var za = System.IO.Compression.ZipFile.OpenRead(outZip);
            Assert.Equal(8, za.Entries.Count);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }
}
