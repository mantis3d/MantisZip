using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// ApplyConflictMarkers 冲突检测优化的单元测试。
/// 验证优化点 ① destDir 不存在跳过、② 过滤项跳过、③ 父目录不存在子树短路。
/// </summary>
public class ExtractConflictMarkerTests : IDisposable
{
    private readonly string _tempRoot;

    public ExtractConflictMarkerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"mantiszip_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>辅助：创建目录+文件结构。</summary>
    private (string destDir, string fileA, string fileB, string fileC, string dirX) SetupDest()
    {
        var dest = Path.Combine(_tempRoot, "dest");
        var dirX = Path.Combine(dest, "X");
        var fileA = Path.Combine(dest, "a.txt");
        var fileB = Path.Combine(dest, "X", "b.txt");
        var fileC = Path.Combine(dest, "X", "Y", "c.txt");

        Directory.CreateDirectory(Path.Combine(dest, "X", "Y"));
        File.WriteAllText(fileA, "a");
        File.WriteAllText(fileB, "b");
        File.WriteAllText(fileC, "c");

        return (dest, fileA, fileB, fileC, dirX);
    }

    /// <summary>辅助：创建仅目录结构（无文件）。</summary>
    private (string destDir, string dirX, string dirXY) SetupDirsOnly()
    {
        var dest = Path.Combine(_tempRoot, "dest_dirs");
        var dirX = Path.Combine(dest, "X");
        var dirXY = Path.Combine(dest, "X", "Y");

        Directory.CreateDirectory(dirXY);

        return (dest, dirX, dirXY);
    }

    /// <summary>辅助：构建测试预览树（3 层：root → X → Y）。</summary>
    private static PreviewTreeNode BuildTestTree()
    {
        var root = new PreviewTreeNode { Name = "dest", FullPath = "dest", IsExpanded = true };
        var x = new PreviewTreeNode { Name = "X", FullPath = "X", IsDirectory = true };
        var b = new PreviewTreeNode { Name = "b.txt", FullPath = "X/b.txt" };
        var y = new PreviewTreeNode { Name = "Y", FullPath = "X/Y", IsDirectory = true };
        var c = new PreviewTreeNode { Name = "c.txt", FullPath = "X/Y/c.txt" };
        var a = new PreviewTreeNode { Name = "a.txt", FullPath = "a.txt" };

        y.Children.Add(c);
        x.Children.Add(b);
        x.Children.Add(y);
        root.Children.Add(x);
        root.Children.Add(a);
        return root;
    }

    private static PreviewTreeNode Find(PreviewTreeNode root, string fullPath)
        => FindRecursive(root, fullPath) ?? throw new InvalidOperationException($"Node '{fullPath}' not found");

    private static PreviewTreeNode? FindRecursive(PreviewTreeNode node, string fullPath)
    {
        foreach (var child in node.Children.OfType<PreviewTreeNode>())
        {
            if (child.FullPath == fullPath) return child;
            var found = FindRecursive(child, fullPath);
            if (found != null) return found;
        }
        return null;
    }

    // ─────────────────────────────────────────────
    // ① destDir 不存在 → 零 I/O
    // ─────────────────────────────────────────────

    [Fact]
    public void DestDirNotExist_AllFalse()
    {
        var tree = BuildTestTree();
        var fakeDir = Path.Combine(_tempRoot, "nonexistent");

        ResultPreviewService.ApplyConflictMarkers(tree, fakeDir);

        // 所有节点 ExistsAtDestination 应保持默认 false
        Assert.All(tree.Children.OfType<PreviewTreeNode>(), c => Assert.False(c.ExistsAtDestination));
    }

    [Fact]
    public void DestDirNull_AllFalse()
    {
        var tree = BuildTestTree();

        ResultPreviewService.ApplyConflictMarkers(tree, null!);

        Assert.All(tree.Children.OfType<PreviewTreeNode>(), c => Assert.False(c.ExistsAtDestination));
    }

    // ─────────────────────────────────────────────
    // ② 过滤项跳过检查
    // ─────────────────────────────────────────────

    [Fact]
    public void FilteredOutFile_NotChecked()
    {
        var (destDir, _, _, _, _) = SetupDest();
        var tree = BuildTestTree();

        // 标记 a.txt 为过滤项
        var a = Find(tree, "a.txt");
        a.IsFilteredOut = true;

        ResultPreviewService.ApplyConflictMarkers(tree, destDir);

        // a.txt 存在但被标记为过滤 → ExistsAtDestination 应为 false（跳过检查）
        Assert.False(a.ExistsAtDestination);
        // X/b.txt 存在且未过滤 → true
        Assert.True(Find(tree, "X/b.txt").ExistsAtDestination);
    }

    [Fact]
    public void FilteredOutDir_SubtreeNotChecked()
    {
        var (destDir, _, _, _, _) = SetupDest();
        var tree = BuildTestTree();

        // 标记 X 为过滤项 → X 及其子树都不应检查
        var x = Find(tree, "X");
        x.IsFilteredOut = true;

        ResultPreviewService.ApplyConflictMarkers(tree, destDir);

        Assert.False(x.ExistsAtDestination);
        Assert.False(Find(tree, "X/b.txt").ExistsAtDestination);
        Assert.False(Find(tree, "X/Y/c.txt").ExistsAtDestination);
    }

    // ─────────────────────────────────────────────
    // ③ 父目录不存在 → 子树短路
    // ─────────────────────────────────────────────

    [Fact]
    public void ParentNotExist_SubtreeSkipped()
    {
        // dest 只有 a.txt，没有 X 目录
        var dest = Path.Combine(_tempRoot, "dest_nox");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "a.txt"), "a");

        var tree = BuildTestTree(); // 含 X/b.txt 和 X/Y/c.txt

        ResultPreviewService.ApplyConflictMarkers(tree, dest);

        // a.txt 存在 → true
        Assert.True(Find(tree, "a.txt").ExistsAtDestination);
        // X 不存在 → false，且子树全部 false
        Assert.False(Find(tree, "X").ExistsAtDestination);
        Assert.False(Find(tree, "X/b.txt").ExistsAtDestination);
        Assert.False(Find(tree, "X/Y/c.txt").ExistsAtDestination);
    }

    // ─────────────────────────────────────────────
    // 两阶段：depth 2 vs 全量
    // ─────────────────────────────────────────────

    [Fact]
    public void MaxDepth2_CheckedShallowOnly()
    {
        var (destDir, _, _, _, _) = SetupDest();
        var tree = BuildTestTree();

        // depth 2: root(0) → X(1) → X/b.txt(2) ✅ 检查
        //          root(0) → X(1) → X/Y(2) ✅ 检查
        //          root(0) → X(1) → X/Y(2) → X/Y/c.txt(3) ❌ 不检查
        ResultPreviewService.ApplyConflictMarkers(tree, destDir, maxDepth: 2);

        Assert.True(Find(tree, "a.txt").ExistsAtDestination);          // depth 1
        Assert.True(Find(tree, "X").ExistsAtDestination);              // depth 1
        Assert.True(Find(tree, "X/b.txt").ExistsAtDestination);       // depth 2
        Assert.True(Find(tree, "X/Y").ExistsAtDestination);           // depth 2
        // depth 3: c.txt 应保持默认 false（未检查）
        Assert.False(Find(tree, "X/Y/c.txt").ExistsAtDestination);
    }

    [Fact]
    public void MaxDepth1_DirectChildrenOnly()
    {
        var (destDir, _, _, _, _) = SetupDest();
        var tree = BuildTestTree();

        // depth 1: 仅 root 的直接子项
        ResultPreviewService.ApplyConflictMarkers(tree, destDir, maxDepth: 1);

        Assert.True(Find(tree, "a.txt").ExistsAtDestination);   // depth 1
        Assert.True(Find(tree, "X").ExistsAtDestination);       // depth 1
        // depth 2+: 全部保持 false
        Assert.False(Find(tree, "X/b.txt").ExistsAtDestination);
        Assert.False(Find(tree, "X/Y").ExistsAtDestination);
        Assert.False(Find(tree, "X/Y/c.txt").ExistsAtDestination);
    }

    [Fact]
    public void FullDepth_AllChecked()
    {
        var (destDir, _, _, _, _) = SetupDest();
        var tree = BuildTestTree();

        ResultPreviewService.ApplyConflictMarkers(tree, destDir);

        Assert.True(Find(tree, "a.txt").ExistsAtDestination);
        Assert.True(Find(tree, "X").ExistsAtDestination);
        Assert.True(Find(tree, "X/b.txt").ExistsAtDestination);
        Assert.True(Find(tree, "X/Y").ExistsAtDestination);
        Assert.True(Find(tree, "X/Y/c.txt").ExistsAtDestination);
    }

    /// <summary>纯 LINQ 扩展用于 ChildrenOfType 遍历。</summary>
    private static class ChildrenOfTypeExtension
    {
    }
}
