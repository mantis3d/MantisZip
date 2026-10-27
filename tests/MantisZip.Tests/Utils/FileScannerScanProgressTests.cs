using System.Reflection;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Utils;
using Xunit;

namespace MantisZip.Tests.Utils;

/// <summary>
/// 扫描阶段进度上报：UI 的「扫描」统计卡需要「已发现文件数 + 已扫描字节」。
/// 真实扫描小目录往往不足 100ms 节流窗口、拿不到任何扫描帧，故直接对上报方法做单元测试
/// （<c>FileScanner</c> 是 internal，靠 InternalsVisibleTo 可见；上报方法私有，用反射调用）。
/// </summary>
public sealed class FileScannerScanProgressTests
{
    private sealed class SyncProgress : IProgress<ArchiveProgress>
    {
        public List<ArchiveProgress> Reports { get; } = new();

        // Progress&lt;T&gt; 会切线程投递，测试要同步收集
        public void Report(ArchiveProgress value) => Reports.Add(value);
    }

    private static MethodInfo GetReportMethod()
    {
        var method = typeof(FileScanner).GetMethod("ReportScanProgress", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return method!;
    }

    [Fact]
    public void ReportScanProgress_EmitsScanningPhase_WithFoundCountAndScannedBytes()
    {
        var progress = new SyncProgress();
        var lastReportTime = DateTime.Now.AddSeconds(-5); // 越过 100ms 节流，保证一定上报
        object?[] args = { progress, "sub/a.txt", 12, 3_000_000L, lastReportTime, TimeSpan.FromMilliseconds(100) };

        GetReportMethod().Invoke(null, args);

        var report = Assert.Single(progress.Reports);
        Assert.Equal(ArchivePhase.Scanning, report.Phase);
        Assert.Equal("sub/a.txt", report.CurrentFile);
        Assert.Equal(12, report.TotalFiles);
        // 关键：第三行「已扫描大小」的数据源
        Assert.Equal(3_000_000L, report.ScannedBytes);
        // 扫描阶段总数未知，主进度条走不定进度
        Assert.Equal(0, report.PercentComplete);
        // ref 参数回写：节流基准被刷新
        Assert.True((DateTime)args[4]! > lastReportTime);
    }

    [Fact]
    public void ReportScanProgress_ThrottlesWithinInterval()
    {
        var progress = new SyncProgress();
        object?[] args = { progress, "a.txt", 1, 10L, DateTime.Now, TimeSpan.FromMilliseconds(100) };

        GetReportMethod().Invoke(null, args);

        Assert.Empty(progress.Reports);
    }

    [Fact]
    public void CollectFiles_AccumulatesScannedBytesAsFoundFilesGrow()
    {
        var root = Path.Combine(Path.GetTempPath(), "mantiszip-scan-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "data");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "a.bin"), new byte[1000]);
            File.WriteAllBytes(Path.Combine(dir, "b.bin"), new byte[2000]);

            var (files, totalBytes) = FileScanner.CollectFiles(new[] { dir }, progress: null, CancellationToken.None);

            Assert.Equal(2, files.Count);
            // ScannedBytes 的取值就是这个累计值
            Assert.Equal(3000L, totalBytes);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* 清理失败不影响断言 */ }
        }
    }
}
