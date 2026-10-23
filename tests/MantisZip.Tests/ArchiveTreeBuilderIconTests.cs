using System.Collections.Generic;
using MantisZip.Core.Abstractions;
using MantisZip.Core.Services;
using Xunit;

namespace MantisZip.Tests;

/// <summary>
/// 目录树节点图标契约：根节点代表压缩包本身（IconArchive），其余目录为 IconFolder。
/// 主窗口目录树（ArchiveContentView）按此键经 GeometryResourceConverter 绑定 PathIcon。
/// </summary>
public class ArchiveTreeBuilderIconTests
{
    [Fact]
    public void RootNode_RepresentsArchive_UsesArchiveIcon()
    {
        var root = ArchiveTreeBuilder.BuildTree([], "sample");

        Assert.Equal(string.Empty, root.FullPath);
        Assert.Equal("IconArchive", root.IconKey);
        // 与既有 emoji 属性保持同一语义（根=📦，目录=📁）
        Assert.Equal("📦", root.Icon);
    }

    [Fact]
    public void DirectoryNode_UsesFolderIcon()
    {
        var items = new List<ArchiveItem>
        {
            new() { FullPath = "docs/", IsDirectory = true, Name = "docs" }
        };

        var root = ArchiveTreeBuilder.BuildTree(items, "sample");
        var child = Assert.Single(root.Children);

        Assert.Equal("docs", child.FullPath);
        Assert.Equal("IconFolder", child.IconKey);
        Assert.Equal("📁", child.Icon);
    }

    [Fact]
    public void NestedDirectoryNodes_AllUseFolderIcon()
    {
        var items = new List<ArchiveItem>
        {
            new() { FullPath = "a/b/c.txt", IsDirectory = false, Name = "c.txt" }
        };

        var root = ArchiveTreeBuilder.BuildTree(items, "sample");
        var level1 = Assert.Single(root.Children);
        var level2 = Assert.Single(level1.Children);

        Assert.Equal("IconFolder", level1.IconKey);
        Assert.Equal("IconFolder", level2.IconKey);
        Assert.Equal("IconArchive", root.IconKey);
    }
}
