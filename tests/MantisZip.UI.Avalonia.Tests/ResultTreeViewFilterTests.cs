using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using MantisZip.Core.Abstractions;
using MantisZip.Core.FileFilter;
using MantisZip.UI.Avalonia.Controls;
using MantisZip.UI.Avalonia.Models;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

/// <summary>
/// ResultTreeView 精简模式与过滤/空目录隐藏的交互测试。
/// 复现用户报告：精简模式（CompactMode）下树是否仍然显示过滤前的内容。
/// </summary>
public class ResultTreeViewFilterTests
{
    private static ArchiveItem FileItem(string path) => new()
    {
        Name = System.IO.Path.GetFileName(path),
        FullPath = path,
        Size = 100,
        IsDirectory = false
    };

    private static ArchiveItem DirItem(string path) => new()
    {
        Name = System.IO.Path.GetFileName(path.TrimEnd('/')),
        FullPath = path,
        IsDirectory = true
    };

    /// <summary>将 AppIcons 资源合并进无头测试的 Application，满足 ResultTreeView.axaml 的 StaticResource 图标引用。</summary>
    private static void EnsureIconResources()
    {
        if (Application.Current == null) return;
        try
        {
            var icons = (ResourceDictionary)AvaloniaXamlLoader.Load(
                new Uri("avares://MantisZip.UI.Avalonia/Resources/Icons/AppIcons.axaml"));
            Application.Current.Resources.MergedDictionaries.Add(icons);
        }
        catch
        {
            // 资源已存在或无法加载：不阻塞测试主逻辑
        }
    }

    private static List<PreviewTreeNode> CollectVisible(ResultTreeView view)
    {
        var result = new List<PreviewTreeNode>();
        foreach (var node in view.DisplayNodes)
            CollectRecursive(node, result);
        return result;
    }

    private static void CollectRecursive(PreviewTreeNode node, List<PreviewTreeNode> result)
    {
        result.Add(node);
        foreach (var child in node.Children.OfType<PreviewTreeNode>())
            CollectRecursive(child, result);
    }

    private static PreviewTreeNode BuildFilteredRoot()
    {
        var entries = new[]
        {
            DirItem("root"),
            FileItem("root/a.txt"),
            FileItem("root/b.log"),          // 被过滤（.log）
            DirItem("root/sub"),
            FileItem("root/sub/c.txt"),
            FileItem("root/sub/d.log"),      // 被过滤（.log）
            DirItem("root/emptydir"),        // 子树无文件 → 空目录
        };
        var filter = new FileFilterCriteria { ExcludeExtensions = new List<string> { ".log" } };
        return ResultPreviewService.BuildExtractPreview(entries, @"D:\out", rootName: "out", filter: filter);
    }

    [AvaloniaFact]
    public void CompactMode_RemovesFilteredFiles()
    {
        EnsureIconResources();
        var view = new ResultTreeView
        {
            Root = BuildFilteredRoot(),
            ShowFilteredGhosts = false,
            CompactMode = true // 默认精简模式
        };

        var visible = CollectVisible(view);
        var visibleFiles = visible.Where(n => !n.IsDirectory && n.Children.Count == 0 && !n.IsTruncated)
            .Select(n => n.FullPath).ToList();

        // 被过滤的 .log 文件绝不应出现在精简模式的显示树中
        Assert.DoesNotContain(visibleFiles, f => f.EndsWith(".log"));
        Assert.Contains("root/a.txt", visibleFiles);
        Assert.Contains("root/sub/c.txt", visibleFiles);
    }

    [AvaloniaFact]
    public void CompactMode_PrunesEmptyDirectories()
    {
        EnsureIconResources();
        var view = new ResultTreeView
        {
            Root = BuildFilteredRoot(),
            ShowFilteredGhosts = false,
            CompactMode = true
        };

        var visible = CollectVisible(view);
        var visibleDirs = visible.Where(n => n.IsDirectory).Select(n => n.FullPath).ToList();

        // 空目录（子树无文件）在精简模式 + 过滤项隐藏时应被移除；含文件的目录保留
        Assert.DoesNotContain("root/emptydir", visibleDirs);
        Assert.Contains("root", visibleDirs);
        Assert.Contains("root/sub", visibleDirs);
    }

    [AvaloniaFact]
    public void TogglingCompactMode_KeepsFilterApplied()
    {
        EnsureIconResources();
        var view = new ResultTreeView
        {
            Root = BuildFilteredRoot(),
            ShowFilteredGhosts = false,
            CompactMode = true
        };

        // 切换到 Full 模式
        view.CompactMode = false;
        var fullVisible = CollectVisible(view);
        Assert.DoesNotContain(fullVisible, n => n.FullPath.EndsWith(".log"));

        // 再切回精简模式
        view.CompactMode = true;
        var compactVisible = CollectVisible(view);
        Assert.DoesNotContain(compactVisible, n => n.FullPath.EndsWith(".log"));
    }

    [AvaloniaFact]
    public void GhostsShown_KeepsFilteredFilesAndEmptyDirs()
    {
        EnsureIconResources();
        var view = new ResultTreeView
        {
            Root = BuildFilteredRoot(),
            ShowFilteredGhosts = true, // 显示过滤项：空目录与灰显文件都应保留
            CompactMode = true
        };

        var visible = CollectVisible(view);
        Assert.Contains(visible, n => n.FullPath.EndsWith("b.log"));
        Assert.Contains(visible, n => n.FullPath == "root/emptydir");
    }

    [AvaloniaFact]
    public void DepthTruncation_LabelCountsOnlyVisible()
    {
        EnsureIconResources();
        var entries = new[]
        {
            DirItem("root"),
            DirItem("root/deep"),
            FileItem("root/deep/visible.txt"),
            FileItem("root/deep/hidden.log"),  // 被过滤（.log）
            DirItem("root/deep/emptydir"),     // 空目录
        };
        var filter = new FileFilterCriteria { ExcludeExtensions = new List<string> { ".log" } };
        var view = new ResultTreeView
        {
            MaxDepth = 1, // 强制在第一层目录触发深度截断
            ShowFilteredGhosts = false,
            CompactMode = true,
            Root = ResultPreviewService.BuildExtractPreview(entries, @"D:\out", rootName: "out", filter: filter)
        };

        var placeholder = CollectVisible(view).FirstOrDefault(n => n.IsTruncated);
        Assert.NotNull(placeholder);
        // 可见后代只有 deep + visible.txt = 2（hidden.log 被过滤、emptydir 为空目录均不计入）
        Assert.Equal(2, placeholder.TruncatedDepth);
    }

    [AvaloniaFact]
    public void ItemTruncation_EmptyDirsNotCounted()
    {
        EnsureIconResources();
        var entries = new List<ArchiveItem>
        {
            DirItem("root"),
            DirItem("root/emptydir1"),
            DirItem("root/emptydir2"),
            DirItem("root/emptydir3"),
        };
        for (int i = 1; i <= 6; i++)
            entries.Add(FileItem($"root/f{i}.txt"));

        var view = new ResultTreeView
        {
            MaxItemsPerDirectory = 5,
            ShowFilteredGhosts = false,
            CompactMode = true,
            Root = ResultPreviewService.BuildExtractPreview(entries, @"D:\out", rootName: "out", filter: null)
        };

        // root 有 9 个子项（6 文件 + 3 空目录），但空目录裁剪后只剩 6 个可见文件，
        // 超出 5 的只有 1 个 —— 空目录不计入截断标签
        var placeholder = CollectVisible(view).FirstOrDefault(n => n.IsTruncated);
        Assert.NotNull(placeholder);
        Assert.Equal(1, placeholder.TruncatedCount);
    }

    [AvaloniaFact]
    public void ItemTruncation_CountsVisibleFiles()
    {
        EnsureIconResources();
        var entries = new List<ArchiveItem> { DirItem("root") };
        for (int i = 1; i <= 8; i++)
            entries.Add(FileItem($"root/f{i}.txt"));

        var view = new ResultTreeView
        {
            MaxItemsPerDirectory = 5,
            ShowFilteredGhosts = false,
            CompactMode = true,
            Root = ResultPreviewService.BuildExtractPreview(entries, @"D:\out", rootName: "out", filter: null)
        };

        // 纯文件场景：8 个可见文件，超出 5 的 3 个应全部计入
        var placeholder = CollectVisible(view).FirstOrDefault(n => n.IsTruncated);
        Assert.NotNull(placeholder);
        Assert.Equal(3, placeholder.TruncatedCount);
    }

    [AvaloniaFact]
    public void DepthTruncation_NoPhantomZeroLevelLabel()
    {
        EnsureIconResources();
        // deep 在 MaxDepth=1 处触发深度截断，但子树只有空目录（无文件）——
        // 空目录裁剪后 deep 整体应消失，绝不应出现"还有 0 层"占位符
        var entries = new[]
        {
            DirItem("root"),
            FileItem("root/keep.txt"),
            DirItem("root/deep"),
            DirItem("root/deep/emptydir1"),
            DirItem("root/deep/emptydir2"),
        };

        var view = new ResultTreeView
        {
            MaxDepth = 1,
            ShowFilteredGhosts = false,
            CompactMode = true,
            Root = ResultPreviewService.BuildExtractPreview(entries, @"D:\out", rootName: "out", filter: null)
        };

        var visible = CollectVisible(view);
        // 不存在"还有 0 层"幽灵占位符
        Assert.DoesNotContain(visible, n => n.IsTruncated && n.TruncatedDepth == 0);
        // deep 及其空目录子树整体被裁剪
        Assert.DoesNotContain(visible, n => n.FullPath.StartsWith("root/deep"));
        // 真实文件保留（root 因深度截断被占位符替换，其占位符计 1 层）
        Assert.Contains(visible, n => n.IsTruncated && n.TruncatedDepth == 1);
    }

    [AvaloniaFact]
    public void Summary_FileCount_ExcludesFilteredAndEmptyDirs()
    {
        EnsureIconResources();
        var view = new ResultTreeView
        {
            ShowFilteredGhosts = false,
            CompactMode = true,
            Root = BuildFilteredRoot() // a.txt + b.log(过滤) + sub(c.txt + d.log(过滤)) + emptydir
        };

        // 过滤后真实操作数据 = a.txt + c.txt = 2 个文件：
        // b.log/d.log（被过滤）与 emptydir（空目录）均不应计入摘要的文件总数
        Assert.Contains("2 个文件", view.SummaryText);
    }
}
