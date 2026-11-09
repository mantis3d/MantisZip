using MantisZip.Core.Utils;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 骨架期「快照式渐进上屏」与「进度同步到显示层克隆体」的单元测试。
/// 背景：屏幕上的树是 ResultTreeView 对原始树的深拷贝，任何改节点属性期望上屏的功能
/// 都必须经 <see cref="PreviewTreeNode.Origin"/> 回指同步，否则会像骨架进度那样「计划完成但看不见」。
/// </summary>
public class SkeletonSnapshotProgressTests : IDisposable
{
    private readonly string _rootDir;

    public SkeletonSnapshotProgressTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), $"MantisZipSnapshotTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_rootDir)) Directory.Delete(_rootDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    /// <summary>在给定子目录下造 count 个 16 字节的小文件。</summary>
    private void CreateFiles(string relativeDir, int count)
    {
        var dir = Path.Combine(_rootDir, relativeDir);
        Directory.CreateDirectory(dir);
        for (var i = 0; i < count; i++)
            File.WriteAllBytes(Path.Combine(dir, $"f{i:D2}.bin"), new byte[16]);
    }

    [Fact]
    public void BuildSourceSubtree_WithSnapshotThresholds_EmitsGrowingSnapshots()
    {
        CreateFiles("d0", 4);
        CreateFiles("d1", 4);
        CreateFiles("d2", 4);

        var snapshots = new List<PreviewTreeNode>();
        var result = ResultPreviewService.BuildSourceSubtree(
            _rootDir,
            ct: CancellationToken.None,
            snapshotSink: snapshots.Add,
            snapshotInterval: TimeSpan.Zero,
            snapshotMinNewFiles: 2);

        Assert.NotNull(result);
        Assert.True(snapshots.Count >= 3, $"阈值调低后应至少交付 3 份快照，实际 {snapshots.Count}");

        // 每份快照都是一个独立实例，且目录节点带「还在加载」标记
        foreach (var snapshot in snapshots)
        {
            Assert.NotSame(result.Node, snapshot);
            Assert.True(snapshot.IsSkeletonPartial);
            Assert.Same(result.Node, snapshot.Origin);
            Assert.NotNull(snapshot.CurrentSkeletonProgress);
        }

        // 文件计数单调递增：树只会长，不会缩
        var counts = snapshots.Select(s => s.CurrentSkeletonProgress!.FileCount).ToList();
        for (var i = 1; i < counts.Count; i++)
            Assert.True(counts[i] > counts[i - 1], $"快照文件数应递增：{string.Join(" -> ", counts)}");

        // 子节点数不减少
        var childCounts = snapshots.Select(s => s.Children.Count).ToList();
        for (var i = 1; i < childCounts.Count; i++)
            Assert.True(childCounts[i] >= childCounts[i - 1], $"快照子节点数不应减少：{string.Join(" -> ", childCounts)}");

        // 最终建成的树含全部 12 个文件（子孙统计由装配阶段填充，这里手动触发）
        ResultPreviewService.RecalculateDescendantStats(result.Node);
        Assert.Equal(12, result.Node.TotalDescendantCount);
    }

    [Fact]
    public void BuildSourceSubtree_WithoutSnapshotSink_NeverBuildsSnapshots()
    {
        CreateFiles("only", 6);

        var result = ResultPreviewService.BuildSourceSubtree(_rootDir, ct: CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.Node.IsSkeletonPartial, "没有快照接收器时不应给树打骨架标记");
        ResultPreviewService.RecalculateDescendantStats(result.Node);
        Assert.Equal(6, result.Node.TotalDescendantCount);
    }

    [Fact]
    public void CloneForSnapshot_MarksDirectoriesPartialAndKeepsProgressOnRoot()
    {
        var live = new PreviewTreeNode { Name = "root", FullPath = "root", IsDirectory = true };
        var file = new PreviewTreeNode { Name = "a.txt", FullPath = "root/a.txt", IsDirectory = false };
        live.Children.Add(file);
        var progress = new SkeletonProgress(5, 100);

        var clone = ResultPreviewService.CloneForSnapshot(live, progress);

        Assert.NotSame(live, clone);
        Assert.True(clone.IsSkeletonPartial);
        Assert.Same(live, clone.Origin);
        Assert.Equal(progress, clone.CurrentSkeletonProgress);

        // 只有根节点带累计进度；文件节点既不是骨架快照的一部分，也各自没有计数
        var cloneFile = Assert.IsType<PreviewTreeNode>(Assert.Single(clone.Children));
        Assert.False(cloneFile.IsSkeletonPartial);
        Assert.Null(cloneFile.CurrentSkeletonProgress);
        Assert.Same(file, cloneFile.Origin);
    }

    [Fact]
    public void DirectoryInfoText_SkeletonPartialWithoutPlaceholder_ShowsProgress()
    {
        // 快照树不挂占位子节点，仍必须显示「加载中 N 个文件，X」
        var node = new PreviewTreeNode
        {
            Name = "root",
            FullPath = "root",
            IsDirectory = true,
            IsSkeletonPartial = true
        };
        node.CurrentSkeletonProgress = new SkeletonProgress(3, 1024);

        Assert.Equal(
            LocalizationManager.T("Preview_Result_SkeletonProgress", 3, FormatUtil.FormatSize(1024)),
            node.DirectoryInfoText);
    }

    [Fact]
    public void SyncSkeletonProgressFromOrigins_CopiesLatestProgressIntoClones()
    {
        var live = new PreviewTreeNode
        {
            Name = "root",
            FullPath = "root",
            IsDirectory = true,
            IsSkeletonPartial = true
        };
        live.Children.Add(new PreviewTreeNode { Name = "a.txt", FullPath = "root/a.txt", IsDirectory = false });

        var clone = live.ShallowClone();
        Assert.Same(live, clone.Origin);
        Assert.Null(clone.CurrentSkeletonProgress);

        live.CurrentSkeletonProgress = new SkeletonProgress(7, 700);
        PreviewTreeNode.SyncSkeletonProgressFromOrigins(new[] { clone });

        Assert.Equal(live.CurrentSkeletonProgress, clone.CurrentSkeletonProgress);
        Assert.Contains("7", clone.DirectoryInfoText);
        Assert.Contains("7", live.DirectoryInfoText);
    }

    [Fact]
    public void SyncSkeletonProgressFromOrigins_LeavesUnsyncedClonesWithoutOriginUntouched()
    {
        // Origin 为 null（例如还没走过 ShallowClone 的手工节点）时不应抛异常，也不应写入进度
        var node = new PreviewTreeNode { Name = "plain", FullPath = "plain", IsDirectory = true };

        PreviewTreeNode.SyncSkeletonProgressFromOrigins(new[] { node });

        Assert.Null(node.CurrentSkeletonProgress);
        Assert.Null(node.Origin);
    }
}
