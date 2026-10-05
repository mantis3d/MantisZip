using MantisZip.Core.Engines;
using Xunit;

namespace MantisZip.Tests.Engines;

/// <summary>
/// N 组并行压缩的分组策略测试（Task 1）。
/// 覆盖 <see cref="ZipEngine.SplitCompressGroup"/> 的纯函数行为：
/// 串行退化、完整划分、空组剔除、组数随 degree 增长、LPT 最重文件优先。
/// </summary>
public class SplitCompressGroupTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "mz_split_" + Guid.NewGuid().ToString("N"));

    public SplitCompressGroupTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best-effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 按给定字节数<b>真实落盘</b>后返回 (FullPath, RelativePath)。
    /// 必须真实落盘：分组内部用 <c>FileInfo.Length</c> 取大小，虚构路径会让所有文件
    /// 大小恒为 0，LPT 退化为「全进第 0 组」，使组数断言必然失败。
    /// </summary>
    private (string FullPath, string RelativePath)[] MakeFiles(params long[] sizes)
    {
        var result = new (string FullPath, string RelativePath)[sizes.Length];
        for (int i = 0; i < sizes.Length; i++)
        {
            string rel = $"f{i}.bin";
            string full = Path.Combine(_dir, rel);
            File.WriteAllBytes(full, new byte[sizes[i]]);
            result[i] = (full, rel);
        }
        return result;
    }

    [Fact]
    public void Degree1_ReturnsSingleGroupWithAllFiles()
    {
        var files = MakeFiles(10, 20, 30);
        var groups = ZipEngine.SplitCompressGroup(files, 1);

        Assert.Single(groups);
        Assert.Equal(3, groups[0].Count);
    }

    [Fact]
    public void GroupsPartitionAllFiles_ExactlyOnce()
    {
        var files = MakeFiles(10, 20, 30, 40, 50, 60, 70);
        var groups = ZipEngine.SplitCompressGroup(files, 3);

        var flat = groups.SelectMany(g => g.Select(f => f.RelativePath)).OrderBy(x => x);
        Assert.Equal(files.Select(f => f.RelativePath).OrderBy(x => x), flat);
    }

    [Fact]
    public void EmptyGroupsAreDropped()
    {
        var files = MakeFiles(10, 20);
        var groups = ZipEngine.SplitCompressGroup(files, 8);

        Assert.All(groups, g => Assert.NotEmpty(g));
    }

    [Fact]
    public void LargerDegreeYieldsMoreGroups()
    {
        var files = MakeFiles(1, 2, 3, 4, 5, 6, 7, 8);
        Assert.True(ZipEngine.SplitCompressGroup(files, 2).Count
                    < ZipEngine.SplitCompressGroup(files, 4).Count);
    }

    [Fact]
    public void HeaviestFileAloneInItsGroup_LongestProcessingTimeMinimized()
    {
        // 贪心 LPT：最大文件先进最轻的桶
        var files = MakeFiles(100, 1, 1, 1);
        var groups = ZipEngine.SplitCompressGroup(files, 2);

        Assert.Equal(2, groups.Count);
        Assert.Single(groups.First(g => g.Any(f => f.RelativePath == "f0.bin")));
    }
}