using MantisZip.Core.Services;
using MantisZip.Tests.Fixtures;
using Xunit;

namespace MantisZip.Tests.Services;

public class ParallelDegreeOptimizerTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var d in _tempDirs.Where(Directory.Exists))
            try { Directory.Delete(d, true); } catch { }
    }

    private string TrackDir(string path) { _tempDirs.Add(path); return path; }

    private static string NewTempDir()
        => Directory.CreateTempSubdirectory("MantisZipOptTest_").FullName;

    [Fact]
    public void GetTestConfigs_HighCoreCount_Returns4Candidates()
    {
        var configs = ParallelDegreeOptimizer.GetTestConfigs(16);
        Assert.Equal(new[] { 4, 8, 12, 16 }, configs);
    }

    [Fact]
    public void GetTestConfigs_MidCoreCount_ReturnsDeduplicated()
    {
        var configs = ParallelDegreeOptimizer.GetTestConfigs(10);
        // [2, 4, 8, 10] — 10 不与前面重复
        Assert.Equal(new[] { 2, 4, 8, 10 }, configs);
    }

    [Fact]
    public void GetTestConfigs_MidCoreCountDedup_CapsAt4()
    {
        // ProcessorCount=8 → [2, 4, 8, 8] 去重后 [2, 4, 8] 只有 3 个
        var configs = ParallelDegreeOptimizer.GetTestConfigs(8);
        Assert.Equal(new[] { 2, 4, 8 }, configs);
    }

    [Fact]
    public void GetTestConfigs_LowCoreCount_Returns1Based()
    {
        var configs = ParallelDegreeOptimizer.GetTestConfigs(4);
        Assert.Equal(new[] { 1, 2, 4 }, configs);
    }

    [Fact]
    public void ShouldPreTest_LargeArchiveWithPreTestOn_ReturnsTrue()
        => Assert.True(ParallelDegreeOptimizer.ShouldPreTest(
            2L * 1024 * 1024 * 1024, preTestEnabled: true, supportsParallel: true));

    [Fact]
    public void ShouldPreTest_SmallArchive_ReturnsFalse()
        => Assert.False(ParallelDegreeOptimizer.ShouldPreTest(
            100L * 1024 * 1024, preTestEnabled: true, supportsParallel: true));

    [Fact]
    public void ShouldPreTest_PreTestDisabled_ReturnsFalse()
        => Assert.False(ParallelDegreeOptimizer.ShouldPreTest(
            2L * 1024 * 1024 * 1024, preTestEnabled: false, supportsParallel: true));

    [Fact]
    public void ShouldPreTest_EngineNotSupportsParallel_ReturnsFalse()
        => Assert.False(ParallelDegreeOptimizer.ShouldPreTest(
            2L * 1024 * 1024 * 1024, preTestEnabled: true, supportsParallel: false));

    [Fact]
    public void ShouldPreTest_ThresholdBoundary_1GBExactly_True()
        => Assert.True(ParallelDegreeOptimizer.ShouldPreTest(
            1L * 1024 * 1024 * 1024, preTestEnabled: true, supportsParallel: true));

    [Fact]
    public void ShouldPreTest_JustBelow1GB_False()
        => Assert.False(ParallelDegreeOptimizer.ShouldPreTest(
            1L * 1024 * 1024 * 1024 - 1, preTestEnabled: true, supportsParallel: true));

    [Fact]
    public async Task FindOptimalDegreeAsync_SmallZip_ReturnsValidDegree()
    {
        // 用小 ZIP 跑一次真实预测试：不检查返回值是否"最优"，
        // 只验证不抛异常、返回值在测试配置集合内、临时目录已清理
        var archive = ArchiveFixtures.CreateMultiFileZipArchive(30, 10); // 30 文件 x 10KB
        var dest = TrackDir(NewTempDir());

        var result = await ParallelDegreeOptimizer.FindOptimalDegreeAsync(
            archive, dest, password: null, progress: null, CancellationToken.None);

        Assert.InRange(result, 1, 16);
        // 预测试临时目录应已全部删除
        var leftovers = Directory.GetDirectories(dest, "__parallel_test_*");
        Assert.Empty(leftovers);
    }

    [Fact]
    public async Task FindOptimalDegreeAsync_CorruptArchive_ReturnsNegative()
    {
        var corrupt = Path.Combine(NewTempDir(), "corrupt.zip");
        File.WriteAllBytes(corrupt, new byte[] { 1, 2, 3, 4, 5 });
        var dest = TrackDir(NewTempDir());

        var result = await ParallelDegreeOptimizer.FindOptimalDegreeAsync(
            corrupt, dest, password: null, progress: null, CancellationToken.None);

        Assert.True(result < 0, $"corrupt archive should return negative, got {result}");
    }

    [Fact]
    public async Task FindOptimalDegreeAsync_Cancelled_ThrowsAndCleansUp()
    {
        var archive = ArchiveFixtures.CreateMultiFileZipArchive(30, 10);
        var dest = TrackDir(NewTempDir());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ParallelDegreeOptimizer.FindOptimalDegreeAsync(
                archive, dest, password: null, progress: null, cts.Token));

        var leftovers = Directory.GetDirectories(dest, "__parallel_test_*");
        Assert.Empty(leftovers);
    }
}
