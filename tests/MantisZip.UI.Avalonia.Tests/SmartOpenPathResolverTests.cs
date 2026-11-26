using System.Collections.Generic;
using System.IO;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Services;
using Xunit;

namespace MantisZip.UI.Avalonia.Tests;

public class SmartOpenPathResolverTests
{
    private static ArchiveItem File(string name, string fullPath = "")
        => new() { Name = name, FullPath = fullPath, IsDirectory = false };

    private static ArchiveItem Dir(string name, string fullPath = "")
        => new() { Name = name, FullPath = fullPath, IsDirectory = true };

    [Fact]
    public void GetCommonRootDirectory_AllShareRoot_ReturnsRoot()
    {
        var entries = new List<ArchiveItem>
        {
            File("my_project/a.txt"),
            File("my_project/sub/b.txt"),
            File("my_project/c.txt")
        };
        Assert.Equal("my_project", SmartOpenPathResolver.GetCommonRootDirectory(entries));
    }

    [Fact]
    public void GetCommonRootDirectory_MixedRoots_ReturnsNull()
    {
        var entries = new List<ArchiveItem>
        {
            File("projA/a.txt"),
            File("projB/b.txt")
        };
        Assert.Null(SmartOpenPathResolver.GetCommonRootDirectory(entries));
    }

    [Fact]
    public void GetCommonRootDirectory_RootLevelFile_ReturnsNull()
    {
        var entries = new List<ArchiveItem>
        {
            File("a.txt"),
            File("my_project/b.txt")
        };
        Assert.Null(SmartOpenPathResolver.GetCommonRootDirectory(entries));
    }

    [Fact]
    public void GetCommonRootDirectory_OnlyRootLevelFiles_ReturnsNull()
    {
        var entries = new List<ArchiveItem>
        {
            File("a.txt"),
            File("b.txt")
        };
        Assert.Null(SmartOpenPathResolver.GetCommonRootDirectory(entries));
    }

    [Fact]
    public void GetCommonRootDirectory_SkipsDirectoryEntries()
    {
        var entries = new List<ArchiveItem>
        {
            Dir("my_project/"),
            File("my_project/a.txt"),
            File("my_project/b.txt")
        };
        Assert.Equal("my_project", SmartOpenPathResolver.GetCommonRootDirectory(entries));
    }

    [Fact]
    public void GetCommonRootDirectory_EmptyList_ReturnsNull()
    {
        Assert.Null(SmartOpenPathResolver.GetCommonRootDirectory(new List<ArchiveItem>()));
    }

    [Fact]
    public void GetCommonRootDirectory_AllDirectories_ReturnsNull()
    {
        var entries = new List<ArchiveItem>
        {
            Dir("my_project/"),
            Dir("other/")
        };
        Assert.Null(SmartOpenPathResolver.GetCommonRootDirectory(entries));
    }

    // ===== ResolveOpenPath（已知根策略时的纯函数版本，智能解压两档用它）=====

    [Fact]
    public void ResolveOpenPath_AddedRoot_OpensThatFolder()
    {
        // 散装包加了「backup」根 → 内容都在 dest\backup 下，打开它
        var result = SmartOpenPathResolver.ResolveOpenPath(@"D:\dl", stripRoot: null, addRoot: "backup", commonRoot: null);
        Assert.Equal(Path.Combine(@"D:\dl", "backup"), result);
    }

    [Fact]
    public void ResolveOpenPath_StrippedRoot_OpensDest()
    {
        // 剥了根 → 内容摊平进 dest，再进一层就是空的
        var result = SmartOpenPathResolver.ResolveOpenPath(
            @"D:\dl", stripRoot: "photos", addRoot: null, commonRoot: "photos");
        Assert.Equal(@"D:\dl", result);
    }

    [Fact]
    public void ResolveOpenPath_StructuralNoop_FallsBackToCommonRoot()
    {
        // 智能加根遇单根包：结构没动，包自带的根还在 dest 下面一层
        var result = SmartOpenPathResolver.ResolveOpenPath(
            @"D:\dl", stripRoot: null, addRoot: null, commonRoot: "photos");
        Assert.Equal(Path.Combine(@"D:\dl", "photos"), result);
    }

    [Fact]
    public void ResolveOpenPath_StructuralNoopNoCommonRoot_OpensDest()
    {
        var result = SmartOpenPathResolver.ResolveOpenPath(
            @"D:\dl", stripRoot: null, addRoot: null, commonRoot: null);
        Assert.Equal(@"D:\dl", result);
    }

    [Fact]
    public void ResolveOpenPath_BothRootsGiven_AddWins()
    {
        // 理论上不会同时给；给两份时以「加了根」为准（内容确实在那一层）
        var result = SmartOpenPathResolver.ResolveOpenPath(
            @"D:\dl", stripRoot: "photos", addRoot: "backup", commonRoot: "photos");
        Assert.Equal(Path.Combine(@"D:\dl", "backup"), result);
    }

    [Fact]
    public void ResolveOpenPath_EmptyStringsTreatedAsNoop()
    {
        var result = SmartOpenPathResolver.ResolveOpenPath(
            @"D:\dl", stripRoot: "", addRoot: "", commonRoot: "");
        Assert.Equal(@"D:\dl", result);
    }
}