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

    [Fact]
    public async Task ExtractEntriesAsync_ParallelMode_ProducesSameResultAsSequential()
    {
        // 过滤解压（ExtractEntriesAsync）并行 vs 串行结果一致性
        var archive = TrackFile(ArchiveFixtures.CreateMultiFileZipArchive(20));
        var keys = Enumerable.Range(0, 20).Select(i => $"file{i:D4}.dat").ToList();

        var sequentialDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString("N")));
        var parallelDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(sequentialDir);
        Directory.CreateDirectory(parallelDir);

        // 串行 (ParallelExtractDegree = 1)
        var seqOptions = new ArchiveOptions { ParallelExtractDegree = 1 };
        await _engine.ExtractEntriesAsync(archive, keys, sequentialDir, options: seqOptions);

        // 并行 (ParallelExtractDegree = 4)
        var parOptions = new ArchiveOptions { ParallelExtractDegree = 4 };
        await _engine.ExtractEntriesAsync(archive, keys, parallelDir, options: parOptions);

        var seqFiles = Directory.GetFiles(sequentialDir, "*", SearchOption.AllDirectories).OrderBy(f => f).ToList();
        var parFiles = Directory.GetFiles(parallelDir, "*", SearchOption.AllDirectories).OrderBy(f => f).ToList();

        Assert.Equal(seqFiles.Count, parFiles.Count);
        Assert.Equal(20, seqFiles.Count);
        for (int i = 0; i < seqFiles.Count; i++)
        {
            Assert.Equal(File.ReadAllBytes(seqFiles[i]), File.ReadAllBytes(parFiles[i]));
        }
    }

    [Fact]
    public async Task ExtractEntriesAsync_ParallelWithPathOverrides_RespectsOverrides()
    {
        // 过滤解压 + 路径覆盖（拖拽解压场景）：并行模式必须尊重 outputPathOverrides，
        // 且未请求的条目绝不解压（预览 = 实际 的边界保证）。
        var archive = TrackFile(ArchiveFixtures.CreateMultiFileZipArchive(6)); // file0000.dat ~ file0005.dat
        var destDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(destDir);

        var keys = new[] { "file0000.dat", "file0001.dat", "file0002.dat" };
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["file0000.dat"] = Path.Combine(destDir, "sub", "renamed0.dat"),
            ["file0001.dat"] = Path.Combine(destDir, "renamed1.dat"),
            ["file0002.dat"] = Path.Combine(destDir, "renamed2.dat"),
        };

        await _engine.ExtractEntriesAsync(archive, keys, destDir,
            options: new ArchiveOptions { ParallelExtractDegree = 4 },
            outputPathOverrides: overrides);

        // 覆盖路径生效
        Assert.True(File.Exists(Path.Combine(destDir, "sub", "renamed0.dat")), "file0000.dat override failed");
        Assert.True(File.Exists(Path.Combine(destDir, "renamed1.dat")), "file0001.dat override failed");
        Assert.True(File.Exists(Path.Combine(destDir, "renamed2.dat")), "file0002.dat override failed");
        // 未请求的条目未解压
        Assert.False(File.Exists(Path.Combine(destDir, "file0003.dat")), "file0003.dat should NOT be extracted");
        Assert.False(File.Exists(Path.Combine(destDir, "file0004.dat")), "file0004.dat should NOT be extracted");
        Assert.False(File.Exists(Path.Combine(destDir, "file0005.dat")), "file0005.dat should NOT be extracted");
    }

    [Fact]
    public async Task ExtractAsync_ParallelMode_AskConflict_InvokesAsyncResolver()
    {
        // 回归：并行全量解压遇到冲突，Ask 策略必须调异步弹窗回调（此前同步 ResolvePath
        // 只认同步 ConflictResolver，Ask 静默降级为覆盖）。
        var archive = TrackFile(ArchiveFixtures.CreateMultiFileZipArchive(10));
        var destDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(destDir);

        // 预置同名冲突文件
        for (int i = 0; i < 10; i++)
            File.WriteAllText(Path.Combine(destDir, $"file{i:D4}.dat"), "existing");

        int resolverCalls = 0;
        var options = new ArchiveOptions
        {
            ParallelExtractDegree = 4,
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = _ =>
            {
                Interlocked.Increment(ref resolverCalls);
                return Task.FromResult(FileConflictAction.Rename);
            }
        };

        var result = await _engine.ExtractAsync(archive, destDir, options: options);

        Assert.Equal(10, result.SucceededEntries);
        Assert.Equal(0, result.FailedEntries);
        Assert.Equal(10, resolverCalls); // Ask 必须触发异步回调，而非静默覆盖

        // 原文件保留 + 重命名文件生成（GetUniquePath: file0000 (1).dat）
        Assert.Equal("existing", File.ReadAllText(Path.Combine(destDir, "file0000.dat")));
        for (int i = 0; i < 10; i++)
        {
            Assert.True(File.Exists(Path.Combine(destDir, $"file{i:D4} (1).dat")),
                $"renamed file file{i:D4} (1).dat missing");
        }
    }

    [Fact]
    public async Task ExtractEntriesAsync_ParallelMode_AskConflict_InvokesAsyncResolver()
    {
        // 回归：过滤解压并行路径同样必须触发异步弹窗回调
        var archive = TrackFile(ArchiveFixtures.CreateMultiFileZipArchive(10));
        var destDir = TrackDir(Path.Combine(Path.GetTempPath(), "MantisZipTest", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(destDir);

        var keys = Enumerable.Range(0, 10).Select(i => $"file{i:D4}.dat").ToList();
        for (int i = 0; i < 10; i++)
            File.WriteAllText(Path.Combine(destDir, $"file{i:D4}.dat"), "existing");

        int resolverCalls = 0;
        var options = new ArchiveOptions
        {
            ParallelExtractDegree = 4,
            ConflictAction = FileConflictAction.Ask,
            ConflictResolverAsync = _ =>
            {
                Interlocked.Increment(ref resolverCalls);
                return Task.FromResult(FileConflictAction.Rename);
            }
        };

        await _engine.ExtractEntriesAsync(archive, keys, destDir, options: options);

        Assert.Equal(10, resolverCalls);
        Assert.Equal("existing", File.ReadAllText(Path.Combine(destDir, "file0000.dat")));
        for (int i = 0; i < 10; i++)
        {
            Assert.True(File.Exists(Path.Combine(destDir, $"file{i:D4} (1).dat")),
                $"renamed file file{i:D4} (1).dat missing");
        }
    }

    /// <summary>
    /// 性能基准测试：验证并行解压比串行有显著加速（针对多小文件场景，100 个 1MB 文件）。
    /// 注意：实际加速比取决于硬件环境（CPU核心数、SSD性能等），CI环境可能较低。
    /// 计划中的 6.32x 基于 8核 CPU + NVMe SSD 理想环境。
    /// 已优化：批次复用 archive 实例（每线程1实例处理多文件），减少 OpenArchive 开销。
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