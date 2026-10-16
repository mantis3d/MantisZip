using System.Text;
using ICSharpCode.SharpZipLib.Zip;
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

    [Fact]
    public async Task FindOptimalDegreeAsync_MaliciousEntryKeys_ReturnsNegativeAndNoEscape()
    {
        // 构造含 ../ 逃逸条目的 ZIP：全部配置应失败返回负数，且目标目录父级不出现逃逸文件
        var archive = Path.Combine(NewTempDir(), "evil.zip");
        CreateZipWithTraversalEntry(archive);
        var dest = TrackDir(NewTempDir());
        var parent = Directory.GetParent(dest)!.FullName;
        var escaped = Path.Combine(parent, "evil.txt");

        var result = await ParallelDegreeOptimizer.FindOptimalDegreeAsync(
            archive, dest, password: null, progress: null, CancellationToken.None);

        Assert.True(result < 0, $"malicious archive should return negative, got {result}");
        Assert.False(File.Exists(escaped), "Zip Slip escape file must not be written outside dest");
    }

    [Fact]
    public async Task FindOptimalDegreeAsync_PreExistingUserDirs_Survive()
    {
        // 数据丢失回归：目标目录下预建的 __parallel_test_{degree} 用户目录（同名/并发场景），
        // 预测试既不能写入其中，也不能在清理时递归删除
        var archive = ArchiveFixtures.CreateMultiFileZipArchive(30, 10);
        var dest = TrackDir(NewTempDir());

        var sentinels = new List<string>();
        foreach (var degree in ParallelDegreeOptimizer.GetTestConfigs(Environment.ProcessorCount))
        {
            var userDir = Path.Combine(dest, $"__parallel_test_{degree}");
            Directory.CreateDirectory(userDir);
            var sentinel = Path.Combine(userDir, "user_sentinel.txt");
            File.WriteAllText(sentinel, "pre-existing user content");
            sentinels.Add(sentinel);
        }

        var result = await ParallelDegreeOptimizer.FindOptimalDegreeAsync(
            archive, dest, password: null, progress: null, CancellationToken.None);

        Assert.InRange(result, 1, 16);
        foreach (var s in sentinels)
            Assert.True(File.Exists(s), $"pre-existing user file must survive: {s}");
    }

    /// <summary>
    /// 创建含 4 个普通小文件 + 1 个 ../ 逃逸条目（key 原样写入）的 ZIP。
    /// </summary>
    private static void CreateZipWithTraversalEntry(string archivePath)
    {
        using var fs = File.Create(archivePath);
        using var zipStream = new ZipOutputStream(fs);
        zipStream.SetLevel(1);

        var buffer = new byte[1024];
        for (int i = 0; i < 4; i++)
        {
            zipStream.PutNextEntry(new ZipEntry($"file{i}.txt"));
            zipStream.Write(buffer, 0, buffer.Length);
            zipStream.CloseEntry();
        }

        zipStream.PutNextEntry(new ZipEntry("../evil.txt"));
        var evilBytes = Encoding.UTF8.GetBytes("evil");
        zipStream.Write(evilBytes, 0, evilBytes.Length);
        zipStream.CloseEntry();
    }
}
