using MantisZip.Core.FileFilter;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// 压缩预览渐进式加载（浅层/宽度/占位/全量/过滤/装配）单元测试。
/// 使用 Path.GetTempPath() 下临时目录构造真实文件树，try/finally 清理。
/// </summary>
public class CompressPreviewProgressiveTests : IDisposable
{
    private readonly string _rootDir;

    public CompressPreviewProgressiveTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), $"MantisZipTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_rootDir)) Directory.Delete(_rootDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    #region 1. 浅层结构

    [Fact]
    public void BuildSourceSubtree_ShallowDepth_LimitsRecursion()
    {
        // root/{a/b/c.txt, a/d.txt, e.txt}
        var dirA = Path.Combine(_rootDir, "a");
        var dirAB = Path.Combine(dirA, "b");
        Directory.CreateDirectory(dirAB);
        File.WriteAllText(Path.Combine(dirAB, "c.txt"), "c");
        File.WriteAllText(Path.Combine(dirA, "d.txt"), "d");
        File.WriteAllText(Path.Combine(_rootDir, "e.txt"), "e");

        var result = ResultPreviewService.BuildSourceSubtree(_rootDir, maxDepth: 1, maxWidthPerDir: 20);

        Assert.NotNull(result);
        var root = result.Node;
        // 根目录下应含 a (目录) 和 e.txt
        var childNames = root.Children.OfType<PreviewTreeNode>().Select(c => c.Name).ToList();
        Assert.Contains("a", childNames);
        Assert.Contains("e.txt", childNames);

        // a 下含 d.txt + 1 个 IsLoadingPlaceholder，不含 b/c.txt
        var dirANode = root.Children.OfType<PreviewTreeNode>().First(c => c.Name == "a");
        var aChildNames = dirANode.Children.OfType<PreviewTreeNode>().Select(c => c.Name).ToList();
        Assert.Contains("d.txt", aChildNames);
        Assert.True(dirANode.Children.OfType<PreviewTreeNode>().Any(c => c.IsLoadingPlaceholder));
        Assert.DoesNotContain("b", aChildNames);
    }

    #endregion

    #region 2. 空目录不挂占位

    [Fact]
    public void BuildSourceSubtree_EmptyDir_NoPlaceholder()
    {
        var emptyDir = Path.Combine(_rootDir, "emptydir");
        Directory.CreateDirectory(emptyDir);

        var result = ResultPreviewService.BuildSourceSubtree(_rootDir, maxDepth: 1);

        Assert.NotNull(result);
        var emptyNode = result.Node.Children.OfType<PreviewTreeNode>().First(c => c.Name == "emptydir");
        Assert.Empty(emptyNode.Children);
        Assert.False(emptyNode.Children.OfType<PreviewTreeNode>().Any(c => c.IsLoadingPlaceholder));
    }

    #endregion

    #region 3. 宽度上限

    [Fact]
    public void BuildSourceSubtree_WidthLimit_TruncatesWithPlaceholder()
    {
        // 单目录 30 个文件，maxWidthPerDir: 20
        for (int i = 0; i < 30; i++)
            File.WriteAllText(Path.Combine(_rootDir, $"file{i:D2}.txt"), $"content{i}");

        var result = ResultPreviewService.BuildSourceSubtree(_rootDir, maxDepth: 1, maxWidthPerDir: 20);

        Assert.NotNull(result);
        var children = result.Node.Children.OfType<PreviewTreeNode>().ToList();
        // 应含 20 个文件 + 1 个占位 = 21
        Assert.Equal(21, children.Count);
        Assert.Single(children, c => c.IsLoadingPlaceholder);
    }

    [Fact]
    public void BuildSourceSubtree_WidthLimit_DirsPrioritizedOverFiles()
    {
        // 5 子目录 + 30 文件 → 5 目录 + 15 文件 + 占位
        for (int i = 0; i < 5; i++)
            Directory.CreateDirectory(Path.Combine(_rootDir, $"dir{i}"));
        for (int i = 0; i < 30; i++)
            File.WriteAllText(Path.Combine(_rootDir, $"file{i:D2}.txt"), $"content{i}");

        var result = ResultPreviewService.BuildSourceSubtree(_rootDir, maxDepth: 1, maxWidthPerDir: 20);

        Assert.NotNull(result);
        var children = result.Node.Children.OfType<PreviewTreeNode>().ToList();
        var dirCount = children.Count(c => c.IsDirectory);
        var fileCount = children.Count(c => !c.IsDirectory && !c.IsLoadingPlaceholder);
        var placeholderCount = children.Count(c => c.IsLoadingPlaceholder);

        Assert.Equal(5, dirCount); // 5 目录全部入列
        Assert.Equal(15, fileCount); // 30 文件截断到 15
        Assert.Equal(1, placeholderCount);
    }

    #endregion

    #region 4. 占位节点属性

    [Fact]
    public void LoadingPlaceholder_HasCorrectProperties()
    {
        // root/{a/b/c.txt, a/d.txt} → shallow → a 下含 b 的占位 + d.txt
        var dirA = Path.Combine(_rootDir, "a");
        var dirAB = Path.Combine(dirA, "b");
        Directory.CreateDirectory(dirAB);
        File.WriteAllText(Path.Combine(dirAB, "c.txt"), "c");
        File.WriteAllText(Path.Combine(dirA, "d.txt"), "d");

        var result = ResultPreviewService.BuildSourceSubtree(_rootDir, maxDepth: 1);

        Assert.NotNull(result);
        var dirANode = result.Node.Children.OfType<PreviewTreeNode>().First(c => c.Name == "a");
        var placeholder = dirANode.Children.OfType<PreviewTreeNode>().First(c => c.IsLoadingPlaceholder);

        Assert.True(placeholder.IsTruncatedNode);
        Assert.Equal("IconFolderSync", placeholder.IconKey);
        Assert.False(placeholder.IsTruncated);
        Assert.Equal(placeholder.DisplayLabel, LocalizationManager.T("Preview_Result_LoadingMore"));

        // ShallowClone 复制 IsLoadingPlaceholder
        var clone = placeholder.ShallowClone();
        Assert.True(clone.IsLoadingPlaceholder);
    }

    #endregion

    #region 5. 父目录不判空

    [Fact]
    public void DirectoryWithPlaceholder_IsNotEmpty_HasEmptyDirText()
    {
        // root/{a/b/c.txt, a/d.txt} → shallow → a 下含 b 的占位 + d.txt
        var dirA = Path.Combine(_rootDir, "a");
        var dirAB = Path.Combine(dirA, "b");
        Directory.CreateDirectory(dirAB);
        File.WriteAllText(Path.Combine(dirAB, "c.txt"), "c");
        File.WriteAllText(Path.Combine(dirA, "d.txt"), "d");

        var result = ResultPreviewService.BuildSourceSubtree(_rootDir, maxDepth: 1);

        Assert.NotNull(result);
        var dirANode = result.Node.Children.OfType<PreviewTreeNode>().First(c => c.Name == "a");

        // 含占位子节点 → 不是空目录
        Assert.False(dirANode.IsEmptyDirectory);
        // HasLoadingPlaceholderChild 生效 → DirectoryInfoText 为空
        Assert.Equal("", dirANode.DirectoryInfoText);
    }

    #endregion

    #region 6. 全量不变

    [Fact]
    public void BuildSourceSubtree_FullDepth_ContainsAllFiles_NoPlaceholder()
    {
        // root/{a/b/c.txt, a/d.txt, e.txt}
        var dirA = Path.Combine(_rootDir, "a");
        var dirAB = Path.Combine(dirA, "b");
        Directory.CreateDirectory(dirAB);
        File.WriteAllText(Path.Combine(dirAB, "c.txt"), "c");
        File.WriteAllText(Path.Combine(dirA, "d.txt"), "d");
        File.WriteAllText(Path.Combine(_rootDir, "e.txt"), "e");

        var result = ResultPreviewService.BuildSourceSubtree(_rootDir); // 默认全量

        Assert.NotNull(result);
        Assert.True(result.IsFull);

        // 递归收集所有叶子
        var allNames = CollectLeafNames(result.Node).ToList();
        Assert.Contains("c.txt", allNames);
        Assert.Contains("d.txt", allNames);
        Assert.Contains("e.txt", allNames);
        Assert.DoesNotContain(result.Node.Children.OfType<PreviewTreeNode>(), c => c.IsLoadingPlaceholder);
    }

    #endregion

    #region 7. 取消穿透

    [Fact]
    public void BuildSourceSubtree_CancelledToken_ThrowsOperationCanceled()
    {
        var dirA = Path.Combine(_rootDir, "a");
        Directory.CreateDirectory(dirA);
        File.WriteAllText(Path.Combine(dirA, "b.txt"), "b");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ResultPreviewService.BuildSourceSubtree(_rootDir, filter: null,
                maxDepth: int.MaxValue, maxWidthPerDir: int.MaxValue, cts.Token));
    }

    #endregion

    #region 8. 过滤一致性

    [Fact]
    public void BuildSourceSubtree_FilterExcludeExtension_MarksFilteredInBothShallowAndFull()
    {
        // root/{a.txt, b.log, subdir/c.txt, subdir/d.log}
        var subdir = Path.Combine(_rootDir, "subdir");
        Directory.CreateDirectory(subdir);
        File.WriteAllText(Path.Combine(_rootDir, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_rootDir, "b.log"), "b");
        File.WriteAllText(Path.Combine(subdir, "c.txt"), "c");
        File.WriteAllText(Path.Combine(subdir, "d.log"), "d");

        var filter = new FileFilterCriteria { ExcludeExtensions = { ".log" } };

        // 浅层
        var shallow = ResultPreviewService.BuildSourceSubtree(_rootDir, filter, maxDepth: 1);
        Assert.NotNull(shallow);
        var shallowFileNodes = CollectLeafNodes(shallow.Node).ToList();
        var shallowLogNodes = shallowFileNodes.Where(n => n.FullPath.Contains(".log")).ToList();
        Assert.All(shallowLogNodes, n => Assert.True(n.IsFilteredOut));

        // 全量
        var full = ResultPreviewService.BuildSourceSubtree(_rootDir, filter);
        Assert.NotNull(full);
        var fullLogNodes = CollectLeafNodes(full.Node).Where(n => n.FullPath.Contains(".log")).ToList();
        Assert.All(fullLogNodes, n => Assert.True(n.IsFilteredOut));
    }

    #endregion

    #region 9. 装配等价

    [Fact]
    public void BuildCompressPreview_ManualMode_MatchesAssembleFromSubtrees()
    {
        var subdir = Path.Combine(_rootDir, "subdir");
        Directory.CreateDirectory(subdir);
        File.WriteAllText(Path.Combine(_rootDir, "a.txt"), "aaa");
        File.WriteAllText(Path.Combine(subdir, "b.txt"), "bbb");

        var paths = new List<string> { _rootDir };

        // 方式 1：原始 BuildCompressPreview
        var (root1, plan1) = ResultPreviewService.BuildCompressPreview(
            paths, outputMode: Core.Abstractions.CompressOutputMode.Manual,
            outputPath: Path.Combine(_rootDir, "out.zip"));

        // 方式 2：BuildSourceSubtree 全量 + AssembleCompressPreview
        var subtrees = paths.Select(p => ResultPreviewService.BuildSourceSubtree(p)).ToList();
        var (root2, plan2) = ResultPreviewService.AssembleCompressPreview(
            paths, subtrees, Core.Abstractions.CompressOutputMode.Manual,
            outputPath: Path.Combine(_rootDir, "out.zip"),
            format: "zip", keepOriginalExtension: false, filter: null);

        // 结构等价：root 子节点数相同
        Assert.Equal(root1.Children.Count, root2.Children.Count);

        // Plan 的 Items 数相同
        Assert.Equal(plan1.Items.Count, plan2.Items.Count);

        // 同源的 IncludedFiles 一致（全量过滤未激活时为 null）
        for (int i = 0; i < plan1.Items.Count; i++)
        {
            var inc1 = plan1.Items[i].IncludedFiles;
            var inc2 = plan2.Items[i].IncludedFiles;
            if (inc1 == null)
                Assert.Null(inc2);
            else
                Assert.Equal(inc1.OrderBy(x => x), inc2!.OrderBy(x => x));
        }
    }

    #endregion

    #region Helpers

    private static IEnumerable<string> CollectLeafNames(PreviewTreeNode node)
    {
        if (node.Children.Count == 0)
        {
            if (!node.IsLoadingPlaceholder)
                yield return node.Name;
            yield break;
        }
        foreach (var child in node.Children.OfType<PreviewTreeNode>())
            foreach (var name in CollectLeafNames(child))
                yield return name;
    }

    private static IEnumerable<PreviewTreeNode> CollectLeafNodes(PreviewTreeNode node)
    {
        if (node.Children.Count == 0)
        {
            if (!node.IsLoadingPlaceholder)
                yield return node;
            yield break;
        }
        foreach (var child in node.Children.OfType<PreviewTreeNode>())
            foreach (var leaf in CollectLeafNodes(child))
                yield return leaf;
    }

    #endregion
}
