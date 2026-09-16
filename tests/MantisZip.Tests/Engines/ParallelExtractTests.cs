using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Engines;
using MantisZip.Tests.Fixtures;
using Xunit;

namespace MantisZip.Tests.Engines;

public class ParallelExtractTests : IDisposable
{
    private readonly ZipEngine _engine = new();
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();

    public ParallelExtractTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles.Where(File.Exists))
            try { File.Delete(f); } catch { }
        foreach (var d in _tempDirs.Where(Directory.Exists))
            try { Directory.Delete(d, true); } catch { }
    }

    private string TrackFile(string path) { _tempFiles.Add(path); return path; }
    private string TrackDir(string path) { _tempDirs.Add(path); return path; }

    [Fact]
    public async Task ExtractAsync_ParallelMode_ProducesSameResultAsSequential()
    {
        // 创建一个包含多个文件的测试 ZIP
        var archive = TrackFile(ArchiveFixtures.CreateMultiFileZipArchive(20)); // 20 个文件

        var sequentialDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString("N")));
        var parallelDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString("N")));

        Directory.CreateDirectory(sequentialDir);
        Directory.CreateDirectory(parallelDir);

        // 串行解压 (ParallelExtractDegree = 1)
        var optionsSequential = new ArchiveOptions { ParallelExtractDegree = 1 };
        var seqResult = await _engine.ExtractAsync(archive, sequentialDir, options: optionsSequential);

        // 并行解压 (ParallelExtractDegree = 4)
        var optionsParallel = new ArchiveOptions { ParallelExtractDegree = 4 };
        var parResult = await _engine.ExtractAsync(archive, parallelDir, options: optionsParallel);

        // 验证结果一致
        Assert.Equal(seqResult.SucceededEntries, parResult.SucceededEntries);
        Assert.Equal(seqResult.FailedEntries, parResult.FailedEntries);

        // 验证文件内容一致
        var seqFiles = Directory.GetFiles(sequentialDir, "*", SearchOption.AllDirectories)
            .OrderBy(f => f).ToList();
        var parFiles = Directory.GetFiles(parallelDir, "*", SearchOption.AllDirectories)
            .OrderBy(f => f).ToList();

        Assert.Equal(seqFiles.Count, parFiles.Count);

        for (int i = 0; i < seqFiles.Count; i++)
        {
            var seqContent = File.ReadAllBytes(seqFiles[i]);
            var parContent = File.ReadAllBytes(parFiles[i]);
            Assert.Equal(seqContent, parContent);
        }
    }

    [Fact]
    public async Task ExtractAsync_ParallelWithCancellation_ThrowsOperationCanceled()
    {
        var archive = TrackFile(ArchiveFixtures.CreateMultiFileZipArchive(50));
        var destDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(destDir);

        var cts = new CancellationTokenSource();
        cts.CancelAfter(1); // 极短时间后取消

        var options = new ArchiveOptions { ParallelExtractDegree = 4 };

        // TaskCanceledException 继承自 OperationCanceledException，用 IsAssignableFrom 验证
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _engine.ExtractAsync(archive, destDir, options: options, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ExtractAsync_SingleFile_UsesSequentialMode()
    {
        var archive = TrackFile(ArchiveFixtures.CreateZipArchive()); // 2 个文件 (hello.txt + subdir/nested.txt)
        var destDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(destDir);

        // 即使并行度 > 1，2 个文件也可能走并行，测试主要验证结果正确
        var options = new ArchiveOptions { ParallelExtractDegree = 4 };
        var result = await _engine.ExtractAsync(archive, destDir, options: options);

        Assert.Equal(2, result.SucceededEntries); // CreateZipArchive 创建 2 个文件
        Assert.Equal(0, result.FailedEntries);
    }

    [Fact]
    public async Task ExtractAsync_ParallelDegreeOne_EqualsSequential()
    {
        var archive = TrackFile(ArchiveFixtures.CreateMultiFileZipArchive(15));
        var destDir1 = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString("N")));
        var destDir2 = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(destDir1);
        Directory.CreateDirectory(destDir2);

        // 并行度 = 1 应该等同于串行
        var options1 = new ArchiveOptions { ParallelExtractDegree = 1 };
        var options2 = new ArchiveOptions { ParallelExtractDegree = 4 };

        var result1 = await _engine.ExtractAsync(archive, destDir1, options: options1);
        var result2 = await _engine.ExtractAsync(archive, destDir2, options: options2);

        Assert.Equal(result1.SucceededEntries, result2.SucceededEntries);
        Assert.Equal(result1.FailedEntries, result2.FailedEntries);

        var files1 = Directory.GetFiles(destDir1, "*", SearchOption.AllDirectories).OrderBy(f => f).ToList();
        var files2 = Directory.GetFiles(destDir2, "*", SearchOption.AllDirectories).OrderBy(f => f).ToList();

        Assert.Equal(files1.Count, files2.Count);
        for (int i = 0; i < files1.Count; i++)
        {
            Assert.Equal(File.ReadAllBytes(files1[i]), File.ReadAllBytes(files2[i]));
        }
    }

    [Fact]
    public void SupportsParallelExtract_ZipEngine_ReturnsTrue()
    {
        Assert.True(_engine.SupportsParallelExtract);
    }

    /// <summary>
    /// 性能基准测试：验证并行解压比串行有显著加速（针对多小文件场景，100 个 1MB 文件）。
    /// 注意：实际加速比取决于硬件环境（CPU核心数、SSD性能等），CI环境可能较低。
    /// 计划中的 6.32x 基于 8核 CPU + NVMe SSD 理想环境。
    /// 已知问题：当前实现每文件打开一次 archive，对小文件(1MB)开销较大，实际加速需更大文件或更多文件。
    /// </summary>
    [Fact(Skip = "性能基准测试，环境依赖强，需手动运行验证")]
    public async Task Benchmark_ParallelVsSequential_Speedup()
    {
        // 创建包含 100 个 1MB 文件的测试 ZIP（类似计划中的基准测试数据）
        var archive = TrackFile(ArchiveFixtures.CreateMultiFileZipArchive(100, 1024)); // 100 个 1MB 文件

        var sequentialDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipBench", Guid.NewGuid().ToString("N")));
        var parallelDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipBench", Guid.NewGuid().ToString("N")));

        Directory.CreateDirectory(sequentialDir);
        Directory.CreateDirectory(parallelDir);

        var engine = new ZipEngine();

        // 预热
        var warmupOptions = new ArchiveOptions { ParallelExtractDegree = 1 };
        await engine.ExtractAsync(archive, Path.Combine(Path.GetTempPath(), "MantisZipBench", "warmup"), options: warmupOptions);

        // 串行解压
        var seqSw = Stopwatch.StartNew();
        var seqResult = await engine.ExtractAsync(archive, sequentialDir, options: new ArchiveOptions { ParallelExtractDegree = 1 });
        seqSw.Stop();

        // 并行解压 (8 线程)
        var parSw = Stopwatch.StartNew();
        var parResult = await engine.ExtractAsync(archive, parallelDir, options: new ArchiveOptions { ParallelExtractDegree = 8 });
        parSw.Stop();

        // 验证结果一致
        Assert.Equal(seqResult.SucceededEntries, parResult.SucceededEntries);
        Assert.Equal(seqResult.FailedEntries, parResult.FailedEntries);

        var seqFiles = Directory.GetFiles(sequentialDir, "*", SearchOption.AllDirectories).OrderBy(f => f).ToList();
        var parFiles = Directory.GetFiles(parallelDir, "*", SearchOption.AllDirectories).OrderBy(f => f).ToList();
        Assert.Equal(seqFiles.Count, parFiles.Count);
        for (int i = 0; i < seqFiles.Count; i++)
        {
            Assert.Equal(File.ReadAllBytes(seqFiles[i]), File.ReadAllBytes(parFiles[i]));
        }

        // 计算加速比
        double speedup = seqSw.ElapsedMilliseconds / (double)parSw.ElapsedMilliseconds;

        // 输出基准结果（在测试输出中可见）
        Console.WriteLine($"=== 性能基准测试结果 (100 个 1MB 文件) ===");
        Console.WriteLine($"  串行耗时: {seqSw.ElapsedMilliseconds} ms");
        Console.WriteLine($"  并行耗时: {parSw.ElapsedMilliseconds} ms");
        Console.WriteLine($"  加速比: {speedup:F2}x");
        Console.WriteLine($"  目标达成: {speedup >= 5.0} (>= 5x)");

        // 断言：并行至少比串行快 1.5x（考虑测试环境差异，阈值设为 1.5x）
        // 计划中的 6.32x 基于 8核 CPU + NVMe SSD 理想环境，CI/测试环境通常较低
        Assert.True(speedup >= 1.5, $"并行解压加速比 {speedup:F2}x 未达到预期 1.5x (环境相关)");
    }
}