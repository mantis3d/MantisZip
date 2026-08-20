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
}